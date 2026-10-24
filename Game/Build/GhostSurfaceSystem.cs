// Citymaker — real surface textures for ghost (Temp) surfaces (GPL-3.0).
//
// Decompiled 1.6.2f1: Game.Rendering.AreaBatchSystem builds its render batches from
//   m_UpdatedQuery = { All Batch, Any Updated|Deleted, None Temp }   (allocation/culling of changed areas)
//   m_BatchQuery   = { All Batch, None Temp }                         (batch remapping)
// and Game.Areas.SearchSystem (the culling quad tree) also excludes Temp. So vanilla never renders a
// temporary surface's material — the area tool only shows the AreaBufferSystem overlay fill. Every
// other part of AreaBatchSystem is agnostic of Temp (no Temp reads in its jobs): an Updated area is
// allocated and marked visible by QueryCullingJob directly, a Deleted one is freed.
//
// So: (1) this system tags OUR temporary surfaces (new ones, Temp.m_Original == Null, created while the
// ghost service has content) with CitymakerGhostSurface in ModificationEnd — the same frame they are
// Created/Updated, before PreCulling; (2) once, it replaces AreaBatchSystem's two queries by the union
// "vanilla desc OR { Batch, Temp, CitymakerGhostSurface }" (built with the system's own GetEntityQuery
// via reflection, so dependency tracking stays correct). Untagged temporaries (vanilla tools) are not
// affected. A tagged ghost is never culled by the tree (not in it) — fine for a preview.
// When ClearTool deletes the temporary, Deleted + tag still matches → its allocation is freed. An
// instantly built ghost (Apply converts the Temp in place) matches the vanilla desc from then on.
// Any failure (fields renamed in a game update) disables the feature; surfaces then show as before.

using System;
using System.Reflection;
using Game;
using Game.Common;
using Game.Rendering;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace Citymaker.Game.Build
{
    /// <summary>Marks a temporary surface of the ghost preview (not serialized; temporaries are never saved).</summary>
    public struct CitymakerGhostSurface : IComponentData { }

    public sealed partial class GhostSurfaceSystem : GameSystemBase
    {
        /// <summary>Setting hook: render ghost surfaces with their real material (default OFF: suspected in a native crash, playtest 4b).</summary>
        public static bool FeatureEnabled = false;

        private BuildService _build;
        private EntityQuery _untagged;
        private bool _patched, _patchFailed;

        public bool Patched => _patched;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _build = World.GetOrCreateSystemManaged<BuildService>();
            _untagged = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<global::Game.Areas.Batch>(), ComponentType.ReadOnly<Updated>() },
                None = new[] { ComponentType.ReadOnly<CitymakerGhostSurface>(), ComponentType.ReadOnly<Deleted>() },
            });
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (!FeatureEnabled || _patchFailed || _build == null || !_build.GhostActive) return;
            if (!_patched && !TryPatch()) return;
            if (_untagged.IsEmptyIgnoreFilter) return;
            using var entities = _untagged.ToEntityArray(Allocator.Temp);
            using var temps = _untagged.ToComponentDataArray<Temp>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (temps[i].m_Original != Entity.Null) continue;   // edits of existing areas: the original still renders
                if ((temps[i].m_Flags & (TempFlags.Delete | TempFlags.Hidden)) != 0) continue;
                EntityManager.AddComponent<CitymakerGhostSurface>(entities[i]);
            }
        }

        private bool TryPatch()
        {
            try
            {
                var abs = World.GetExistingSystemManaged<AreaBatchSystem>();
                if (abs == null) { _patchFailed = true; return false; }
                var t = typeof(AreaBatchSystem);
                var updated = t.GetField("m_UpdatedQuery", BindingFlags.Instance | BindingFlags.NonPublic);
                var batch = t.GetField("m_BatchQuery", BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo getQuery = null;
                foreach (var m in typeof(ComponentSystemBase).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    if (m.Name != "GetEntityQuery") continue;
                    var ps = m.GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType == typeof(EntityQueryDesc[])) { getQuery = m; break; }
                }
                if (updated == null || batch == null || getQuery == null)
                {
                    _patchFailed = true;
                    Mod.Log.Warn("Citymaker: AreaBatchSystem layout changed; ghost surfaces show without texture.");
                    return false;
                }
                var updatedQ = (EntityQuery)getQuery.Invoke(abs, new object[] { new[]
                {
                    new EntityQueryDesc
                    {
                        All = new[] { ComponentType.ReadOnly<global::Game.Areas.Batch>() },
                        Any = new[] { ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Deleted>() },
                        None = new[] { ComponentType.ReadOnly<Temp>() },
                    },
                    new EntityQueryDesc
                    {
                        All = new[] { ComponentType.ReadOnly<global::Game.Areas.Batch>(), ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<CitymakerGhostSurface>() },
                        Any = new[] { ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Deleted>() },
                    },
                } });
                var batchQ = (EntityQuery)getQuery.Invoke(abs, new object[] { new[]
                {
                    new EntityQueryDesc
                    {
                        All = new[] { ComponentType.ReadOnly<global::Game.Areas.Batch>() },
                        None = new[] { ComponentType.ReadOnly<Temp>() },
                    },
                    new EntityQueryDesc
                    {
                        All = new[] { ComponentType.ReadOnly<global::Game.Areas.Batch>(), ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<CitymakerGhostSurface>() },
                    },
                } });
                updated.SetValue(abs, updatedQ);
                batch.SetValue(abs, batchQ);
                _patched = true;
                Mod.Log.Info("Citymaker: ghost surfaces render with their real materials (AreaBatchSystem queries widened).");
                return true;
            }
            catch (Exception e)
            {
                _patchFailed = true;
                Mod.Log.Warn("Citymaker could not enable textured ghost surfaces: " + e.Message);
                return false;
            }
        }
    }
}
