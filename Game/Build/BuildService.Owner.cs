// Citymaker — the owner lot of a build (US7) and clearing existing trees/props (US6) (GPL-3.0).
//
// Owner lot: a "Citymaker Area" lot (OwnerAreaPrefabSystem) is emitted over the build outline in the
// same transaction as everything else. It is tagged CitymakerMember{Auxiliary, OwnerArea} and its
// entity is stored in the record (CitymakerGroup.Owner). It is the ONE selectable thing: the lot is
// raycastable by the default tool and the bulldozer (AreaTypeMask.Lots), the RaycastLiftPatch lifts
// hits on any member to it, and bulldozing it cascades to the whole group (GroupRegistrySystem →
// GroupRemovalSystem, phase-correct). Members are deliberately NOT given an Owner:
//   * nets with an area owner lose zone blocks and vanilla upgrade/connection behaviour;
//   * surfaces/objects owned by a lot would be deleted with it when a save is loaded without the mod
//     (unknown prefab) — without Owner they degrade to plain city parts (RESEARCH-NOTES 4);
//   * objects owned by an area get scattered by SubObjectSystem.RelocateSubObjects (PLT measured).
// Parking bays keep their bare carrier (Owner+Attached), which LaneConnectionSystem needs.
//
// Clearing: trees, plants and loose props/decals inside the outline get vanilla delete definitions
// (exactly what BulldozeToolSystem.AddEntity emits for a top-level object), so they disappear on the
// same Apply as the build appears — never earlier, never without the build.

using System;
using System.Collections.Generic;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Tools;
using Citymaker.Core;
using Citymaker.Core.Build;
using Citymaker.Core.Geometry;
using Citymaker.Game.Groups;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Citymaker.Game.Build
{
    public sealed partial class BuildService
    {
        /// <summary>Setting "Clear existing trees and props" (default on).</summary>
        public static bool ClearExistingObjects = true;

        private const long OwnerKey = 1L << 61;
        private const int MaxClearedObjects = 20000;

        private OwnerAreaPrefabSystem _ownerPrefabs;
        private Entity _ownerPrefab;
        private bool _ownerSuppressed;     // the owner ring itself was rejected: build without it
        private Entity _ownerTemp;
        private int _cleared;
        private EntityQuery _clearQuery;

        private void InitOwner()
        {
            _ownerPrefabs = World.GetOrCreateSystemManaged<OwnerAreaPrefabSystem>();
            _clearQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<global::Game.Objects.Object>(), ComponentType.ReadOnly<Transform>(), ComponentType.ReadOnly<PrefabRef>() },
                Any = new[] { ComponentType.ReadOnly<Plant>(), ComponentType.ReadOnly<Static>() },
                None = new[]
                {
                    ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<global::Game.Buildings.Building>(), ComponentType.ReadOnly<global::Game.Objects.UtilityObject>(),
                    ComponentType.ReadOnly<Attached>(), ComponentType.ReadOnly<global::Game.Routes.TransportStop>(),
                    ComponentType.ReadOnly<CitymakerMember>(), ComponentType.ReadOnly<Secondary>(),
                },
            });
        }

        private void ResetOwner()
        {
            _ownerPrefab = Entity.Null;
            _ownerTemp = Entity.Null;
            _ownerLevel = 0;
            _ownerDefinition = Entity.Null;
            _cleared = 0;
        }

        /// <summary>Outline of the build in XZ (drawn area, else hull of everything planned). Null if none.</summary>
        private float2[] BuildOutline()
        {
            var ring = _request.Area?.Ring;
            if (ring != null && ring.Length >= 3) return Poly.To2D(ring);
            var pts = new List<float2>();
            foreach (var l in _request.Plan.Layers)
            {
                foreach (var s in l.Surfaces) if (s?.Ring != null) foreach (var p in s.Ring) pts.Add(p.xz);
                foreach (var a in l.Areas) if (a?.Ring != null) foreach (var p in a.Ring) pts.Add(p.xz);
                foreach (var n in l.Nets) if (n != null) { pts.Add(n.A.xz); pts.Add(n.D.xz); }
                foreach (var o in l.Objects) if (o != null) pts.Add(o.Position.xz);
            }
            if (pts.Count < 3) return null;
            var hull = AreaRings.ConvexHull(pts);
            return hull.Length >= 3 ? hull : null;
        }

        // Owner lot outside validation (playtest: "the game rejected the owner lot" → builds 4–6 could not
        // be selected or bulldozed as one). A top-level Lot area errors on ANY overlap with another lot
        // (Game.Areas.ValidationHelpers.AreaIterator: same AreaType, top-level → OverlapExisting), e.g. a
        // neighbouring build's owner lot or a planned lot area of this build, and a validation error
        // used to drop it. Now the plan is validated WITHOUT the owner; the owner definition is emitted
        // after validation passed (Step.OwnerLot), its own errors are inspected separately: overlap is
        // accepted (an invisible selection lot may overlap), a shape error retries with a simpler ring
        // (OwnerRings levels), and only after the last level the build goes on without it (Temp Cancel).

        private const int OwnerMaterializeFrames = 12;
        private const int OwnerValidateFrames = 3;
        private int _ownerLevel;
        private Entity _ownerDefinition;
        private int _ownerSince;

        /// <summary>Emits the owner lot definition at the current ring level. False when there is nothing to emit.</summary>
        private bool EmitOwnerArea()
        {
            if (LooseBuild) return false;   // roads are just roads: nothing to select as one thing
            if (_ownerSuppressed || _ownerPrefabs == null) return false;
            _ownerPrefab = _ownerPrefabs.ReadyPrefab;
            if (_ownerPrefab == Entity.Null) return false;
            var outline = BuildOutline();
            if (outline == null) return false;
            float2[] ring = null;
            while (_ownerLevel < OwnerRings.Levels && ring == null)
            {
                ring = OwnerRings.Candidate(outline, _ownerLevel);
                if (ring == null) _ownerLevel = OwnerRings.Levels;
            }
            if (ring == null)
            {
                Mod.Log.Warn("Citymaker: no valid lot shape for the owner lot; the build is selectable per part (bulldozing a part removes it all).");
                return false;
            }
            var ring3 = new float3[ring.Length];
            for (int i = 0; i < ring.Length; i++) ring3[i] = new float3(ring[i].x, 0f, ring[i].y);
            var w = new WorkItem { Kind = WorkKind.Area, Layer = -1, Index = -1, Category = BuildCategories.AreaLot };
            int areas = _emittedAreas;
            _ownerDefinition = EmitAreaDefinition(_ownerPrefab, ring3, OwnerKey, w);
            _emittedAreas = areas;   // the owner lot is not a planned area (verification)
            _itemByKey.Remove(OwnerKey);   // never part of the census or exclusion
            return _ownerDefinition != Entity.Null;
        }

        /// <summary>Owner temporary (matched by prefab + centroid), or Null.</summary>
        private Entity FindOwnerTemp()
        {
            using var temps = _tempQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < temps.Length; i++)
            {
                var e = temps[i];
                if (!EntityManager.HasComponent<global::Game.Areas.Area>(e) || !IsOwnCandidate(e, out _)) continue;
                if (TryMatch(e, out long key, out _) && key == OwnerKey) return e;
            }
            return Entity.Null;
        }

        /// <summary>Error type the game reports for <paramref name="e"/> (from its notification icon), "" if none.</summary>
        private string ErrorTypeOf(Entity e)
        {
            if (_iconQuery.IsEmptyIgnoreFilter) return "";
            using var icons = _iconQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < icons.Length; i++)
            {
                if (!EntityManager.HasComponent<Owner>(icons[i]) || EntityManager.GetComponentData<Owner>(icons[i]).m_Owner != e) continue;
                var prefab = EntityManager.GetComponentData<PrefabRef>(icons[i]).m_Prefab;
                if (EntityManager.HasComponent<ToolErrorData>(prefab)) return EntityManager.GetComponentData<ToolErrorData>(prefab).m_Error.ToString();
            }
            return "Unknown";
        }

        private static bool IsShapeError(string type) =>
            type == "InvalidShape" || type == "SmallArea" || type == "LongDistance" || type == "Unknown";

        private void CancelOwnerTemp(Entity temp)
        {
            if (_ownerDefinition != Entity.Null && EntityManager.Exists(_ownerDefinition) && !EntityManager.HasComponent<Deleted>(_ownerDefinition))
                EntityManager.AddComponent<Deleted>(_ownerDefinition);
            _ownerDefinition = Entity.Null;
            if (temp != Entity.Null && EntityManager.Exists(temp) && EntityManager.HasComponent<Temp>(temp))
            {
                var t = EntityManager.GetComponentData<Temp>(temp);
                t.m_Flags |= TempFlags.Cancel;   // Apply deletes cancelled temporaries instead of building them
                EntityManager.SetComponentData(temp, t);
            }
        }

        /// <summary>Step.OwnerLot: emit → materialise → own validation → adopt, or retry simpler, or go without.</summary>
        private void DoOwnerLot(int frame)
        {
            if (frame == _stepFrame)
            {
                _ownerSince = frame;
                if (!EmitOwnerArea()) { Go(Step.Apply); }
                return;
            }
            var temp = FindOwnerTemp();
            if (temp == Entity.Null)
            {
                if (frame - _ownerSince < OwnerMaterializeFrames) return;
                Mod.Log.Warn($"Citymaker: the owner lot (level {_ownerLevel}) did not materialise.");
                NextOwnerLevel(frame, Entity.Null);
                return;
            }
            if (frame - _ownerSince < OwnerValidateFrames + 2) return;
            if (EntityManager.HasComponent<Error>(temp))
            {
                string type = ErrorTypeOf(temp);
                if (IsShapeError(type))
                {
                    Mod.Log.Warn($"Citymaker: the game rejected the owner lot shape (level {_ownerLevel}: {type}); trying a simpler ring.");
                    NextOwnerLevel(frame, temp);
                    return;
                }
                // Overlap with another lot (a neighbouring build, a planned lot): harmless for an invisible
                // selection lot. Apply builds temporaries regardless of errors (the tool gate is ours).
                Mod.Log.Info($"Citymaker: owner lot overlaps existing lots ({type}); kept.");
            }
            AdoptOwnerTemp(temp);
            Go(Step.Apply);
        }

        private void NextOwnerLevel(int frame, Entity temp)
        {
            CancelOwnerTemp(temp);
            _ownerLevel++;
            _ownerSince = frame;
            if (_ownerLevel >= OwnerRings.Levels || !EmitOwnerArea())
            {
                Mod.Log.Warn("Citymaker: building without owner lot; the build is selectable per part and bulldozing a part removes it all.");
                Go(Step.Apply);
            }
        }

        /// <summary>Tags the materialised owner lot (called from TagTemporaries).</summary>
        private void AdoptOwnerTemp(Entity e)
        {
            _ownerTemp = e;
            _registry.EnsureOwnerBuffers(e);
            Tag(e, MemberKind.Auxiliary, MemberRole.OwnerArea);
            if (_record != Entity.Null && EntityManager.Exists(_record))
            {
                var g = EntityManager.GetComponentData<CitymakerGroup>(_record);
                g.Owner = e;   // Apply converts the Temp in place, so the entity stays valid
                EntityManager.SetComponentData(_record, g);
            }
        }

        // ───────────────────────── clearing (US6) ─────────────────────────

        private bool OnBuildingLot(float2 p)
        {
            var prot = _request?.Plan?.Protected;
            if (prot == null) return false;
            foreach (var q in prot) if (Poly.Contains(q, p)) return true;
            return false;
        }

        private void EmitClearDefinitions()
        {
            _cleared = 0;
            if (LooseBuild) return;   // road grids keep the trees; the game clears what a road overlaps
            if (!ClearExistingObjects || _clearQuery == null || _clearQuery.IsEmptyIgnoreFilter) return;
            var outline = BuildOutline();
            if (outline == null) return;
            Poly.Bounds(outline, out var min, out var max);
            using var entities = _clearQuery.ToEntityArray(Allocator.Temp);
            using var transforms = _clearQuery.ToComponentDataArray<Transform>(Allocator.Temp);
            for (int i = 0; i < entities.Length && _cleared < MaxClearedObjects; i++)
            {
                var t = transforms[i];
                var p = t.m_Position.xz;
                if (p.x < min.x || p.y < min.y || p.x > max.x || p.y > max.y) continue;
                if (!Poly.Contains(outline, p)) continue;
                if (OnBuildingLot(p)) continue;   // (s15) a building's garden stays as it is
                var e = entities[i];
                if (EntityManager.HasComponent<Elevation>(e)) continue;   // stacked/elevated props belong to something
                var def = NewDefinition();
                EntityManager.AddComponentData(def, new CreationDefinition { m_Original = e, m_Flags = CreationFlags.Delete });
                EntityManager.AddComponent<Updated>(def);
                EntityManager.AddComponentData(def, new ObjectDefinition
                {
                    m_Position = t.m_Position,
                    m_Rotation = t.m_Rotation,
                    m_LocalPosition = t.m_Position,
                    m_LocalRotation = t.m_Rotation,
                    m_ParentMesh = -1,
                    m_Probability = 100,
                    m_PrefabSubIndex = -1,
                });
                _cleared++;
            }
            if (_cleared > 0) Mod.Log.Info($"Citymaker clears {_cleared} existing trees/props inside the outline.");
        }
    }
}
