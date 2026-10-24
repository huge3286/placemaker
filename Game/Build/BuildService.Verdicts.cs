// Citymaker — s59: one validation pass, then build what was planned (GPL-3.0).
//
// Replaces the s53 anarchy classifier and the exclude-and-retry loop (MaxAutoExcludeAttempts = 6, since the first game
// layer commit). Why the loop could never work, and what the game really does (decompiled Game.dll 1.6.2f1):
//   * GenerateObjectsSystem gives EVERY object created from a CreationDefinition TempFlags.Essential. Game.Objects.
//     ValidationHelpers.ValidateObject then checks each of our top-level temporaries against every other temporary
//     (objectList / edgeList) and every existing object/net (quad trees). Pieces only skip each other when they share a
//     top-level owner (the Owner chain) — ours have none at Temp time, so stall marking vs stall marking, bush vs bush,
//     marking vs our own lot lane are all ErrorSeverity.Error (Essential → no "override", no "warning").
//   * Apply never reads Error. ToolApplySystem turns Warning into Deleted and Override into Overridden; ApplyObjects/
//     Nets/Areas drop temporaries with TempFlags.Cancel. Everything else is applied as it is.
// So with "Ignore collisions in my builds" the build simply applies; right before Apply it removes the verdicts that would
// delete or hide our own pieces (Warning/Override on our temporaries) and — in every mode — the Warning that would
// demolish an existing building. Pieces left out are cancelled in place (TempFlags.Cancel, the game's own mechanism), once.

using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Citymaker.Core;
using Citymaker.Core.Build;
using Citymaker.Game.Selection;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Citymaker.Game.Build
{
    public sealed partial class BuildService
    {
        /// <summary>Temporaries the build left out in place (re-asserted right before Apply).</summary>
        private readonly List<Entity> _cancelledTemps = new List<Entity>();
        /// <summary>Plan items built although the game flagged them (anarchy), for the audit and the parking line.</summary>
        private int _keptDespite;
        private EntityQuery _verdictQuery;

        private void InitVerdicts()
        {
            _verdictQuery = GetEntityQuery(new EntityQueryDesc
            {
                Any = new[] { ComponentType.ReadOnly<Warning>(), ComponentType.ReadOnly<Override>(), ComponentType.ReadOnly<Error>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
        }

        private void ResetVerdicts()
        {
            _cancelledTemps.Clear();
            _keptDespite = 0;
        }

        /// <summary>Top-level temporary of a flagged sub-element (sub-objects / lanes go with their owner).</summary>
        private Entity TopTemporary(Entity e)
        {
            var top = e;
            for (int depth = 0; depth < 6 && EntityManager.HasComponent<Owner>(top); depth++)
            {
                var o = EntityManager.GetComponentData<Owner>(top).m_Owner;
                if (o == Entity.Null || !EntityManager.Exists(o) || !EntityManager.HasComponent<Temp>(o)) break;
                top = o;
            }
            return top;
        }

        /// <summary>
        /// Every temporary the game marked with an Error, as plan pieces (kind, functional, touching an existing building,
        /// error type, name, position). <paramref name="entities"/> is index-aligned with the result.
        /// </summary>
        private List<FlaggedPiece> GatherFlagged(List<Entity> entities)
        {
            var list = new List<FlaggedPiece>();
            entities.Clear();
            if (_errorQuery.IsEmptyIgnoreFilter) return list;
            var byOwner = ErrorTypeByOwner();
            var assets = SafeAssetInfo();
            List<float2[]> buildings = null;
            HashSet<long> touching = null;
            using var errors = _errorQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < errors.Length; i++)
            {
                var e = errors[i];
                var top = TopTemporary(e);
                var p = new FlaggedPiece { Kind = KindOf(top), ErrorType = "OverlapExisting" };
                if (byOwner.TryGetValue(e, out var t) || byOwner.TryGetValue(top, out t)) p.ErrorType = t;
                if (TryGetPosition(top, out var at) || TryGetPosition(e, out at)) p.At = at;
                try { if (EntityManager.HasComponent<PrefabRef>(top)) p.Name = _catalog?.NameOf(EntityManager.GetComponentData<PrefabRef>(top).m_Prefab); } catch (Exception) { }
                if (EntityManager.HasComponent<Temp>(top) && EntityManager.HasComponent<PrefabRef>(top) && IsOwnCandidate(top, out _)
                    && TryMatch(top, out long key, out _) && key != OwnerKey && _itemByKey.TryGetValue(key, out var w))
                {
                    p.Key = key;
                    p.Ours = true;
                    p.Kind = KindOf(w);
                    var obj = ItemObject(_request.Plan, w);
                    p.Functional = obj != null && PlanWork.IsFunctional(obj.Role);
                    if (touching == null)
                    {
                        buildings = ExistingBuildingPolys();
                        touching = new HashSet<long>();
                        if (buildings.Count > 0)
                            foreach (var hit in SiteSafety.ItemsTouching(_request.Plan, _work, buildings, assets, 0.3f)) touching.Add(PlanWork.Key(hit));
                    }
                    p.TouchesBuilding = touching.Contains(PlanWork.Key(w));
                }
                list.Add(p);
                entities.Add(top);
            }
            return list;
        }

        private VerdictKind KindOf(WorkItem w)
        {
            switch (w.Kind)
            {
                case WorkKind.Net: return VerdictKind.Net;
                case WorkKind.Surface: return VerdictKind.Surface;
                case WorkKind.Area: return VerdictKind.Area;
                case WorkKind.Object:
                    var o = ItemObject(_request?.Plan, w);
                    return o != null && o.Role == ObjectRole.Decal ? VerdictKind.Decal : VerdictKind.Object;
            }
            return VerdictKind.Other;
        }

        private VerdictKind KindOf(Entity e)
        {
            if (EntityManager.HasComponent<Edge>(e) || EntityManager.HasComponent<global::Game.Net.Node>(e)) return VerdictKind.Net;
            if (EntityManager.HasComponent<global::Game.Areas.Area>(e)) return VerdictKind.Area;
            if (EntityManager.HasComponent<global::Game.Objects.Object>(e)) return VerdictKind.Object;
            return VerdictKind.Other;
        }

        /// <summary>
        /// The single validation pass (Step.Validate, after the settle frames). False = the build stops (status set).
        /// </summary>
        private bool ApplyVerdicts()
        {
            var entities = new List<Entity>();
            var flagged = GatherFlagged(entities);
            if (flagged.Count == 0) return true;
            var d = BuildVerdicts.Decide(flagged, IgnoreOwnCollisions, AutoExcludeCollisions || RecoverRejectedPieces);
            string histogram = BuildVerdicts.Histogram(flagged);
            if (d.Fail)
            {
                var b = d.Blocker.Value;
                string type = d.FailErrorType ?? "OverlapExisting";
                Mod.Log.Warn($"Citymaker build stopped by the game ({type}): {BuildVerdicts.Describe(b)}"
                    + (b.TouchesBuilding ? " touches an existing building" : "") + $". All flagged: {histogram}.");
                _status.ErrorPositions = new List<float3> { b.At };
                _errorTypeName = type;
                _errorKey = BuildErrorKeys.For(type);
                _errorArgs = new object[] { 1, type };
                return false;
            }
            // Leave out exactly the decided pieces, in place (no re-emission, no retry).
            var dropKeys = new Dictionary<long, string>();
            foreach (var kv in d.Drop) dropKeys[kv.Key.Key] = kv.Value;
            int cancelled = 0;
            for (int i = 0; i < flagged.Count; i++)
                if (flagged[i].Ours && dropKeys.ContainsKey(flagged[i].Key) && CancelTemporary(entities[i])) cancelled++;
            foreach (var kv in d.Drop)
            {
                if (_itemByKey.TryGetValue(kv.Key.Key, out var w)) AuditItem(w, kv.Value);
                string what = kv.Key.Kind == VerdictKind.Net ? "Net" : kv.Key.Kind == VerdictKind.Surface ? "Surface" : kv.Key.Kind == VerdictKind.Area ? "Area" : "Object";
                NoteSkip("Citymaker.Build.Skipped." + what, kv.Key.At);
            }
            var keptKeys = new HashSet<long>();
            foreach (var k in d.KeptDespite)
            {
                if (!keptKeys.Add(k.Key)) continue;
                if (_itemByKey.TryGetValue(k.Key, out var w)) AuditItem(w, AuditReason.Anarchy);
            }
            _keptDespite = keptKeys.Count;
            foreach (var n in d.NetsByBuildings)
                Mod.Log.Info($"Citymaker: {BuildVerdicts.Describe(n)} touches an existing building or its lot; built anyway (anarchy) — existing buildings are never removed.");
            Mod.Log.Info($"Citymaker validation: {flagged.Count} flagged temporaries ({histogram}); "
                + (IgnoreOwnCollisions ? "anarchy: building everything" : "vanilla rules")
                + $" — kept {_keptDespite} piece(s) despite errors, left out {d.Drop.Count} ({cancelled} temporaries cancelled in place).");
            return true;
        }

        /// <summary>
        /// Leaves one temporary (and its sub-elements) out of the Apply the way the game's own ValidationSystem.Cancel does:
        /// TempFlags.Cancel | Hidden. ApplyObjects/Nets/Areas then delete it instead of creating it.
        /// </summary>
        private bool CancelTemporary(Entity e, int depth = 0)
        {
            if (e == Entity.Null || !EntityManager.Exists(e) || !EntityManager.HasComponent<Temp>(e) || depth > 6) return false;
            var temp = EntityManager.GetComponentData<Temp>(e);
            bool changed = (temp.m_Flags & TempFlags.Cancel) == 0;
            if (changed)
            {
                temp.m_Flags |= TempFlags.Cancel | TempFlags.Hidden;
                EntityManager.SetComponentData(e, temp);
                if (!EntityManager.HasComponent<BatchesUpdated>(e)) EntityManager.AddComponent<BatchesUpdated>(e);
                if (depth == 0) _cancelledTemps.Add(e);
            }
            var children = new List<Entity>();
            if (EntityManager.HasBuffer<global::Game.Objects.SubObject>(e))
                foreach (var s in EntityManager.GetBuffer<global::Game.Objects.SubObject>(e, true)) children.Add(s.m_SubObject);
            if (EntityManager.HasBuffer<global::Game.Net.SubLane>(e))
                foreach (var s in EntityManager.GetBuffer<global::Game.Net.SubLane>(e, true)) children.Add(s.m_SubLane);
            if (EntityManager.HasBuffer<global::Game.Areas.SubArea>(e))
                foreach (var s in EntityManager.GetBuffer<global::Game.Areas.SubArea>(e, true)) children.Add(s.m_Area);
            foreach (var c in children) CancelTemporary(c, depth + 1);
            return changed;
        }

        /// <summary>
        /// Right before Apply (same frame, ToolUpdate; ToolApplySystem runs in ApplyTool right after):
        ///   * every mode: no existing building (or its sub-objects / an existing road) carries a Warning — Apply would delete it;
        ///   * anarchy: none of our temporaries carries Warning (→ Deleted) or Override (→ Overridden, hidden); Error goes too;
        ///   * pieces left out in place stay cancelled.
        /// </summary>
        private void NeutralizeVerdicts()
        {
            int keptBuildings = 0, freedOwn = 0;
            if (!_verdictQuery.IsEmptyIgnoreFilter)
            {
                using var all = _verdictQuery.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < all.Length; i++)
                {
                    var e = all[i];
                    bool hasWarning = EntityManager.HasComponent<Warning>(e);
                    if (EntityManager.HasComponent<Temp>(e))
                    {
                        if (!IgnoreOwnCollisions) continue;
                        var temp = EntityManager.GetComponentData<Temp>(e);
                        if (temp.m_Original != Entity.Null || (temp.m_Flags & (TempFlags.Cancel | TempFlags.Delete)) != 0) continue;
                        bool any = false;
                        if (hasWarning) { EntityManager.RemoveComponent<Warning>(e); any = true; }
                        if (EntityManager.HasComponent<Override>(e)) { EntityManager.RemoveComponent<Override>(e); any = true; }
                        if (EntityManager.HasComponent<Error>(e)) EntityManager.RemoveComponent<Error>(e);
                        if (any) freedOwn++;
                        continue;
                    }
                    if (!hasWarning) continue;
                    if (IsBuildingOrPartOfOne(e) || EntityManager.HasComponent<Edge>(e) || EntityManager.HasComponent<global::Game.Net.Node>(e))
                    {
                        EntityManager.RemoveComponent<Warning>(e);
                        keptBuildings++;
                    }
                }
            }
            foreach (var c in _cancelledTemps)
            {
                if (!EntityManager.Exists(c) || !EntityManager.HasComponent<Temp>(c)) continue;
                var t = EntityManager.GetComponentData<Temp>(c);
                if ((t.m_Flags & TempFlags.Cancel) == 0) { t.m_Flags |= TempFlags.Cancel | TempFlags.Hidden; EntityManager.SetComponentData(c, t); }
            }
            if (keptBuildings > 0)
            {
                _buildingsProtected += keptBuildings;
                Mod.Log.Info($"Citymaker: {keptBuildings} existing building(s)/road(s) the game would have removed are kept standing.");
            }
            if (freedOwn > 0) Mod.Log.Info($"Citymaker anarchy: {freedOwn} of our pieces were marked by the game to be removed or hidden; all are built.");
        }

        private bool IsBuildingOrPartOfOne(Entity e)
        {
            var cur = e;
            for (int depth = 0; depth < 6 && cur != Entity.Null && EntityManager.Exists(cur); depth++)
            {
                if (BuildingFootprints.IsBuilding(EntityManager, cur)) return true;
                if (!EntityManager.HasComponent<Owner>(cur)) return false;
                cur = EntityManager.GetComponentData<Owner>(cur).m_Owner;
            }
            return false;
        }

        /// <summary>Ghost under anarchy: the build places everything, so the preview shows no red "refused" tint either.</summary>
        private void ClearGhostVerdicts()
        {
            if (!IgnoreOwnCollisions || _verdictQuery.IsEmptyIgnoreFilter) return;
            using var all = _verdictQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < all.Length; i++)
            {
                var e = all[i];
                if (!EntityManager.HasComponent<Temp>(e)) continue;
                if (EntityManager.HasComponent<Error>(e)) EntityManager.RemoveComponent<Error>(e);
                if (EntityManager.HasComponent<Warning>(e)) EntityManager.RemoveComponent<Warning>(e);
                if (!EntityManager.HasComponent<BatchesUpdated>(e)) EntityManager.AddComponent<BatchesUpdated>(e);
            }
        }
    }
}
