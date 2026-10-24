// Citymaker — "Pre-zone all new roads" after a roads build (s34, GPL-3.0).
//
// Playtest (image 62): the Roads type's edge-zoning cutouts left a scattered pattern of zoned cells. The roads layer
// now plans one Marquee quad per predicted zone block of every road it adds (Core RoadPreZone, fact "roads.preZone").
// After the main Apply the game has given the new roads their real blocks (and split them where walkways and side
// streets join): this step reads the live blocks, keeps those whose owner edge lies on a planned road, and zones each
// whole (cells under nets / buildings stay blocked; Marquee never overwrites the player's zoning; s52: on hillside terraces only
// the rows that lie level on the terrace, read from the plan's built ground). Without a live
// match the planned quads are used. Other layers' zoning quads of the plan are kept as planned.

using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Tools;
using Citymaker.Core;
using Citymaker.Core.Build;
using Citymaker.Core.Layers.Roads;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Citymaker.Game.Build
{
    public sealed partial class BuildService
    {
        /// <summary>A roads layer of this plan pre-zones its new roads.</summary>
        private bool PreZonesRoads
        {
            get
            {
                var plan = _request?.Plan;
                if (plan?.Layers == null) return false;
                foreach (var l in plan.Layers)
                    if (l != null && l.Facts != null && l.Facts.TryGetValue(RoadPreZone.Fact, out double v) && v > 0 && l.Zoning.Count > 0) return true;
                return false;
            }
        }

        private void DoRoadPreZone(int frame)
        {
            if (frame - _stepFrame < ZoneRefreshFrames) return;
            try
            {
                var plan = _request.Plan;
                var nets = new List<PlannedNet>();
                var others = new List<ZoneGuard.ZoningQuad>();
                string zoneId = null;
                RoadPreZone.DepthRule rule = null;   // (s52) hillside terraces zone only the level rows
                float2 min = new float2(float.MaxValue), max = new float2(float.MinValue);
                foreach (var l in plan.Layers)
                {
                    if (l == null) continue;
                    bool roads = l.Facts != null && l.Facts.TryGetValue(RoadPreZone.Fact, out double v) && v > 0;
                    if (!roads)
                    {
                        foreach (var z in l.Zoning)
                            if (z != null) others.Add(new ZoneGuard.ZoningQuad { Corners = new[] { z.A.xz, z.B.xz, z.C.xz, z.D.xz }, ZoneId = z.ZoneId });
                        continue;
                    }
                    foreach (var z in l.Zoning) if (z != null && !string.IsNullOrEmpty(z.ZoneId)) { zoneId = zoneId ?? z.ZoneId; break; }
                    rule = rule ?? RoadPreZone.RuleOf(l);
                    foreach (var n in l.Nets)
                    {
                        if (n == null) continue;
                        nets.Add(n);
                        foreach (var p in new[] { n.A, n.B, n.C, n.D }) { min = math.min(min, p.xz); max = math.max(max, p.xz); }
                    }
                }
                if (zoneId == null || nets.Count == 0) { ContinueAfterApply(); return; }
                var blocks = new List<RoadPreZone.LiveBlock>();
                CaptureOwnedBlocks(min - 60f, max + 60f, blocks);
                var quads = RoadPreZone.FromLive(nets, blocks, zoneId, 1.5f, rule);
                if (quads.Count > 0)
                {
                    quads.AddRange(others);
                    _refreshedZoning = quads;
                    Mod.Log.Info($"Citymaker roads pre-zoning: {blocks.Count} live zone block(s) around the build, {quads.Count - others.Count} of the new roads zoned whole ({zoneId}).");
                }
                else Mod.Log.Info($"Citymaker roads pre-zoning: no live block of the new roads found among {blocks.Count}; zoning the planned blocks.");
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Citymaker roads pre-zoning refresh failed (zoning the planned blocks): " + e.Message);
                _refreshedZoning = null;
            }
            ContinueAfterApply();
        }

        /// <summary>Live zone blocks in the box with the curve of the road edge that owns each.</summary>
        private void CaptureOwnedBlocks(float2 min, float2 max, List<RoadPreZone.LiveBlock> into)
        {
            var search = World.GetOrCreateSystemManaged<global::Game.Zones.SearchSystem>();
            var tree = search.GetSearchTree(true, out var deps);
            deps.Complete();
            var results = new NativeList<Entity>(64, Allocator.Temp);
            try
            {
                var it = new ZoneBlockIterator { Bounds = new Bounds2(min, max), Results = results };
                tree.Iterate(ref it);
                var seen = new HashSet<Entity>();
                for (int i = 0; i < results.Length; i++)
                {
                    var e = results[i];
                    if (!seen.Add(e) || !EntityManager.HasComponent<global::Game.Zones.Block>(e)) continue;
                    if (EntityManager.HasComponent<Temp>(e) || EntityManager.HasComponent<Deleted>(e) || !EntityManager.HasComponent<Owner>(e)) continue;
                    var owner = EntityManager.GetComponentData<Owner>(e).m_Owner;
                    if (owner == Entity.Null || !EntityManager.Exists(owner) || !EntityManager.HasComponent<Curve>(owner)) continue;
                    var b = EntityManager.GetComponentData<global::Game.Zones.Block>(e);
                    var c = EntityManager.GetComponentData<Curve>(owner).m_Bezier;
                    into.Add(new RoadPreZone.LiveBlock
                    {
                        Grid = new ZoneGrid { Position = b.m_Position.xz, Direction = b.m_Direction, Size = b.m_Size },
                        OwnerA = c.a, OwnerB = c.b, OwnerC = c.c, OwnerD = c.d,
                    });
                }
            }
            finally { results.Dispose(); }
        }
    }
}
