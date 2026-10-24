// Citymaker — last line of defence for existing buildings (s15).
//
// The plan already keeps off building lots (Core/Build/SiteSafety, from SiteCapture's obstacles). But the
// capture is a snapshot: a building may have grown since, or sit outside the captured margin. The game
// tells us exactly which existing building our temporaries would remove: Net/Areas/Objects
// ValidationHelpers give an overlapped spawnable building (Overridable | DeleteOverridden) a
// Game.Tools.Warning, and Apply (ToolApplySystem) deletes every entity carrying a Warning. (s59) So before Apply:
// the building's Warning is removed (NeutralizeVerdicts, every mode — nothing is ever demolished) and our objects,
// decals and areas touching it are left out in place (cancelled temporaries, no retry). A NET touching it is built
// anyway under anarchy (removing one edge would break the network; the building stays); with vanilla rules the
// build stops and names the net.

using System.Collections.Generic;
using Game.Common;
using Game.Tools;
using Citymaker.Core;
using Citymaker.Core.Build;
using Citymaker.Core.Geometry;
using Citymaker.Game.Selection;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Citymaker.Game.Build
{
    public sealed partial class BuildService
    {
        private EntityQuery _warnedBuildingQuery;
        private int _buildingsProtected;

        private void InitProtect()
        {
            _warnedBuildingQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Warning>(), ComponentType.ReadOnly<global::Game.Objects.Transform>() },
                Any = new[] { ComponentType.ReadOnly<global::Game.Buildings.Building>(), ComponentType.ReadOnly<global::Game.Buildings.Extension>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
        }

        private void ResetProtect()
        {
            _buildingsProtected = 0;
        }

        /// <summary>
        /// Called once in Validate. 0 = go on (pieces touching a building at risk were cancelled in place, the building's
        /// Warning goes in NeutralizeVerdicts); -1 = vanilla rules and one of our nets would remove a building (stop).
        /// </summary>
        private int ProtectBuildings()
        {
            if (_warnedBuildingQuery == null || _warnedBuildingQuery.IsEmptyIgnoreFilter || _request?.Plan == null || _work == null) return 0;
            using var warned = _warnedBuildingQuery.ToEntityArray(Allocator.Temp);
            var polys = new List<float2[]>();
            float3? at = null;
            for (int i = 0; i < warned.Length; i++)
            {
                var e = warned[i];
                if (!BuildingFootprints.IsBuilding(EntityManager, e)) continue;
                BuildingFootprints.Of(EntityManager, e, out var lot, out var footprint);
                if (lot != null) polys.Add(lot);
                if (footprint != null) polys.Add(footprint);
                if (!at.HasValue) at = EntityManager.GetComponentData<global::Game.Objects.Transform>(e).m_Position;
            }
            if (polys.Count == 0) return 0;
            Dictionary<string, AssetInfo> assets = null;
            try { assets = Services.Assets?.InfoMap(); } catch (System.Exception) { }
            var hits = SiteSafety.ItemsTouching(_request.Plan, _work, ObstacleCut.Grow(polys, 1f), assets, 1f);
            if (hits.Count == 0)
            {
                Mod.Log.Info($"Citymaker: {warned.Length} existing building(s) were marked for removal; none of our pieces touches them — they are kept standing.");
                return 0;
            }
            var hitKeys = new HashSet<long>();
            WorkItem? net = null;
            foreach (var w in hits)
            {
                if (w.Kind == WorkKind.Net) { if (!net.HasValue) net = w; continue; }
                if (w.Kind == WorkKind.Zoning) continue;
                hitKeys.Add(PlanWork.Key(w));
            }
            if (net.HasValue && !IgnoreOwnCollisions)
            {
                var n = net.Value;
                Mod.Log.Warn($"Citymaker: net '{n.Asset.Id}' at ({n.Anchor.x:F0}, {n.Anchor.y:F0}) would remove {warned.Length} existing building(s); the build stops (vanilla rules).");
                NoteSkip("Citymaker.Build.WouldDemolish", new float3(n.Anchor.x, Height(n.Anchor), n.Anchor.y), IssueSeverity.Error, warned.Length);
                _status.ErrorPositions = new List<float3> { new float3(n.Anchor.x, Height(n.Anchor), n.Anchor.y) };
                return -1;
            }
            int cancelled = 0;
            var audited = new HashSet<long>();
            using (var temps = _tempQuery.ToEntityArray(Allocator.Temp))
                for (int i = 0; i < temps.Length; i++)
                {
                    var e = temps[i];
                    if (!IsOwnCandidate(e, out _) || !TryMatch(e, out long key, out _) || !_itemByKey.TryGetValue(key, out var w)) continue;
                    if (!hitKeys.Contains(PlanWork.Key(w))) continue;
                    if (CancelTemporary(e)) cancelled++;
                    if (audited.Add(key)) AuditItem(w, AuditReason.SiteSafety);
                }
            _buildingsProtected += warned.Length;
            Mod.Log.Info($"Citymaker: {warned.Length} existing building(s) would have been removed by this build; they are kept, {cancelled} of our piece(s) touching them left out"
                + (net.HasValue ? $"; net '{net.Value.Asset.Id}' at ({net.Value.Anchor.x:F0}, {net.Value.Anchor.y:F0}) is built anyway (anarchy)." : "."));
            return 0;
        }
    }
}
