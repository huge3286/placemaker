// Citymaker — the runtime lot prefabs of a build (GPL-3.0):
//  * "Citymaker Area": the ONE selectable thing of a build (owner lot).
//  * "Citymaker Zone Block" (s19): invisible PHYSICAL lots over the build outside its zoning cutouts, so the
//    roads' zone cells under a park/plaza are blocked and zoned buildings cannot grow there.
//
// Owner lot model and settings ported from ParkingLotTool's ParkingLotLotArea (KruemelGames, GPL-3.0),
// which measured them in game (save/load passed 2026-08-11):
//
//  * Lot type: the default tool and the bulldozer raycast AreaTypeMask.Lots (a Surface is never
//    clickable in the default tool, RESEARCH-NOTES 7). All vanilla lots are industry/extractor lots
//    with their own economy, so we register our own.
//  * m_AllowEditing MUST stay true: false sets GeometryFlags.HiddenIngame, and Areas.RaycastJobs
//    discards hidden areas outside the editor → invisible AND unclickable. Invisibility comes from
//    transparent AreaColorData instead; the selection colours stay visible so selection shows.
//  * m_AllowOverlap = true removes GeometryFlags.PhysicalGeometry: the lot never collides with our
//    own surfaces/areas or the city.
//  * No CanOverrideObjects on the lot: PLT measured props of neighbouring zoning vanishing with it.
//  * m_MaxRadius is only checked for lots owned by a Transform (ValidationHelpers): irrelevant here.
//
// Zone block lot (s19, decompiled 1.6.2f1, RESEARCH-NOTES-ADDENDUM 67–72): Game.Zones.CellBlockJobs blocks every
// zone cell under an area whose prefab has GeometryFlags.PhysicalGeometry; AreaInitializeSystem gives that flag to
// every LotData prefab unless m_AllowOverlap. So this lot keeps m_AllowOverlap = false. Physical lots also take
// part in validation (they report overlaps with nets, non-overridable objects and other lots, and they would
// demolish spawnable buildings): the build emits them in their own transaction AFTER the build was applied,
// ignores their overlap errors (Apply builds errored temporaries), and cancels a piece that would remove a
// building. Overridable props/trees are ignored without CanOverrideObjects (cleared below as for the owner).
// Fully transparent, also when selected: the owner lot is what the player sees and selects.
//
// Engine rules (RESEARCH-NOTES 1–3): AddPrefab in PrefabUpdate BEFORE PrefabInitializeSystem, never
// use it in the frame it was added, and register both unconditionally at startup so saves that
// reference them by name always resolve.
//
// Phase: PrefabUpdate, before Game.Prefabs.PrefabInitializeSystem.

using System;
using Game;
using Game.Prefabs;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Scripting;

namespace Citymaker.Game.Groups
{
    public sealed partial class OwnerAreaPrefabSystem : GameSystemBase
    {
        public const string PrefabName = "Citymaker Area";
        public const string BlockerPrefabName = "Citymaker Zone Block";
        private const int ReadyDelayFrames = 3;

        /// <summary>Setting: build an owner lot so the result is one selectable/bulldozable thing.</summary>
        public static bool FeatureEnabled = true;

        private PrefabSystem _prefabs;
        private Entity _prefab = Entity.Null;
        private Entity _blocker = Entity.Null;
        private int _registeredFrame = -1;
        private int _blockerFrame = -1;
        private bool _failed;
        private bool _blockerFailed;
        private bool _announced;
        private bool _blockerAnnounced;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (_prefab == Entity.Null && !_failed)
            {
                try { _prefab = Register(PrefabName, true, new Color(1f, 1f, 1f, 0.12f), new Color(1f, 1f, 1f, 0.6f), out _registeredFrame); }
                catch (Exception e)
                {
                    Mod.Log.Error(e, "Citymaker could not register the owner lot prefab; builds stay selectable per part");
                }
                if (_prefab == Entity.Null) _failed = true;
            }
            if (_blocker == Entity.Null && !_blockerFailed)
            {
                try { _blocker = Register(BlockerPrefabName, false, new Color(0f, 0f, 0f, 0f), new Color(0f, 0f, 0f, 0f), out _blockerFrame); }
                catch (Exception e)
                {
                    Mod.Log.Error(e, "Citymaker could not register the zone block prefab; builds will not block zone cells");
                }
                if (_blocker == Entity.Null) _blockerFailed = true;
            }
        }

        /// <summary>The owner lot prefab when it can be used (archetype built, type Lot), else Entity.Null.</summary>
        public Entity ReadyPrefab => FeatureEnabled ? Ready(_prefab, _registeredFrame, ref _announced, PrefabName, false) : Entity.Null;

        /// <summary>(s19) The zone block lot prefab when usable (type Lot with PhysicalGeometry), else Entity.Null.</summary>
        public Entity ReadyBlockerPrefab => Ready(_blocker, _blockerFrame, ref _blockerAnnounced, BlockerPrefabName, true);

        public bool IsOwnerPrefab(Entity prefab) => prefab != Entity.Null && prefab == _prefab;

        public bool IsBlockerPrefab(Entity prefab) => prefab != Entity.Null && prefab == _blocker;

        private Entity Ready(Entity prefab, int frame, ref bool announced, string name, bool physical)
        {
            if (prefab == Entity.Null || UnityEngine.Time.frameCount - frame < ReadyDelayFrames) return Entity.Null;
            if (!EntityManager.Exists(prefab) || !EntityManager.HasComponent<AreaData>(prefab)
                || !EntityManager.GetComponentData<AreaData>(prefab).m_Archetype.Valid
                || !EntityManager.HasComponent<AreaGeometryData>(prefab)) return Entity.Null;
            var geometry = EntityManager.GetComponentData<AreaGeometryData>(prefab);
            if (geometry.m_Type != global::Game.Areas.AreaType.Lot) return Entity.Null;
            if (physical && (geometry.m_Flags & global::Game.Areas.GeometryFlags.PhysicalGeometry) == 0) return Entity.Null;
            if (!announced)
            {
                announced = true;
                if ((geometry.m_Flags & global::Game.Areas.GeometryFlags.CanOverrideObjects) != 0)
                {
                    geometry.m_Flags &= ~global::Game.Areas.GeometryFlags.CanOverrideObjects;
                    EntityManager.SetComponentData(prefab, geometry);
                }
                Mod.Log.Info($"Citymaker lot '{name}' is ready (flags {geometry.m_Flags}).");
            }
            return prefab;
        }

        private Entity Register(string name, bool overlap, Color selection, Color selectionEdge, out int frame)
        {
            frame = UnityEngine.Time.frameCount;
            // After a world reload the prefab may already exist: just map it.
            if (_prefabs.TryGetPrefab(new PrefabID(nameof(LotPrefab), name), out var existing) && existing != null
                && _prefabs.TryGetEntity(existing, out var existingEntity))
                return existingEntity;
            var lot = ScriptableObject.CreateInstance<LotPrefab>();
            lot.name = name;
            lot.m_MaxRadius = 0f;
            lot.m_AllowOverlap = overlap;
            lot.m_AllowEditing = true;
            lot.m_OnWater = false;
            lot.m_Color = new Color(0f, 0f, 0f, 0f);
            lot.m_EdgeColor = new Color(0f, 0f, 0f, 0f);
            lot.m_SelectionColor = selection;
            lot.m_SelectionEdgeColor = selectionEdge;
            lot.m_RangeColor = new Color(0f, 0f, 0f, 0f);
            if (!Citymaker.Game.Assets.AssetCatalogSystem.AddOwnPrefab(_prefabs, lot))
            {
                UnityEngine.Object.Destroy(lot);
                Mod.Log.Warn($"Citymaker could not add the lot prefab '{name}'.");
                return Entity.Null;
            }
            var e = _prefabs.GetEntity(lot);
            Mod.Log.Info($"Citymaker registered the lot prefab '{name}' (usable from the next frames).");
            return e;
        }
    }
}
