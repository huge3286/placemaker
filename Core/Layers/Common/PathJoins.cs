// Path joins (g5 integration). CS2 connects two courses only at bit-identical nodes: a spur that
// ends in the middle of another planned path is built as a separate path lying on top of it, with
// no junction (pedestrians cannot turn, the meshes z-fight). This helper splits the planned path at
// the closest point to the spur end and moves the spur end exactly onto the new node.
//
// The composer runs JoinAll over every pedestrian path of a composed plan, so a feature's "path to
// it" joins the parent's path network without the feature knowing about it. Layers can also call
// SplitAt themselves (e.g. to get a junction node before routing a spur to it).
//
// Index stability: a split keeps the first half in the original PlannedNet (same list index, so
// PlannedObject.AttachedNet and plan-work keys stay valid) and APPENDS the second half.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Citymaker.Core.Layers.Common
{
    public static class PathJoins
    {
        /// <summary>Ends closer than this to another path are joined to it.</summary>
        public const float DefaultTolerance = 0.6f;
        /// <summary>No split piece shorter than this (m): the end snaps to the existing node instead.</summary>
        public const float MinPiece = 1.5f;

        public static bool IsPath(PlannedNet n) => n != null && (n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide);

        /// <summary>Splits <paramref name="n"/> at curve parameter t: n becomes [0,t], the returned net is [t,1].</summary>
        public static PlannedNet Split(PlannedNet n, float t)
        {
            t = math.clamp(t, 0f, 1f);
            float3 p0 = n.A, p1 = n.B, p2 = n.C, p3 = n.D;
            float3 a = math.lerp(p0, p1, t), b = math.lerp(p1, p2, t), c = math.lerp(p2, p3, t);
            float3 d = math.lerp(a, b, t), e = math.lerp(b, c, t);
            float3 m = math.lerp(d, e, t);
            var second = new PlannedNet
            {
                A = m, B = e, C = c, D = p3, Role = n.Role, Asset = n.Asset, Category = n.Category, Control = n.Control,
                StartAttach = -1, EndAttach = n.EndAttach, EndAttachT = n.EndAttachT,
                Upgrades = n.Upgrades != null ? (string[])n.Upgrades.Clone() : Array.Empty<string>(),
                FollowTerrain = n.FollowTerrain,
            };
            n.B = a; n.C = d; n.D = m;
            n.EndAttach = -1; n.EndAttachT = 0f;
            return second;
        }

        /// <summary>Closest curve parameter and XZ distance from p to the net (sampled + refined).</summary>
        public static float Closest(PlannedNet n, float2 p, out float distance)
        {
            const int samples = 24;
            float bestT = 0f, best = float.MaxValue;
            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples;
                float d = math.distancesq(Composer.Bezier(n.A, n.B, n.C, n.D, t).xz, p);
                if (d < best) { best = d; bestT = t; }
            }
            float lo = math.max(0f, bestT - 1f / samples), hi = math.min(1f, bestT + 1f / samples);
            for (int it = 0; it < 24; it++)
            {
                float m1 = lo + (hi - lo) / 3f, m2 = hi - (hi - lo) / 3f;
                float d1 = math.distancesq(Composer.Bezier(n.A, n.B, n.C, n.D, m1).xz, p);
                float d2 = math.distancesq(Composer.Bezier(n.A, n.B, n.C, n.D, m2).xz, p);
                if (d1 < d2) hi = m2; else lo = m1;
            }
            bestT = (lo + hi) * 0.5f;
            distance = math.distance(Composer.Bezier(n.A, n.B, n.C, n.D, bestT).xz, p);
            return bestT;
        }

        /// <summary>
        /// Makes a node at the point of the planned paths closest to p (within <paramref name="tolerance"/>):
        /// snaps to an existing end when one is near, else splits the path there. Returns false when no
        /// path is close enough. New pieces are appended to <paramref name="nets"/>.
        /// </summary>
        public static bool SplitAt(List<PlannedNet> nets, float2 p, float tolerance, out float3 junction, Func<PlannedNet, bool> filter = null)
        {
            junction = default;
            if (nets == null) return false;
            filter = filter ?? IsPath;
            int bestNet = -1; float bestD = tolerance, bestT = 0f;
            for (int i = 0; i < nets.Count; i++)
            {
                var n = nets[i];
                if (!filter(n) || !NearBox(n, p, tolerance)) continue;
                float t = Closest(n, p, out float d);
                if (d <= bestD) { bestD = d; bestNet = i; bestT = t; }
            }
            if (bestNet < 0) return false;
            junction = NodeAt(nets[bestNet], bestT, nets);
            return true;
        }

        /// <summary>
        /// Joins every path end that meets another path within <paramref name="tolerance"/> (but is not
        /// already on one of its nodes) by splitting that path and snapping the end onto the new node.
        /// Ends attached to existing game nets are left alone. Returns the number of joins.
        /// </summary>
        public static int JoinAll(List<PlannedNet> nets, float tolerance = DefaultTolerance, Func<PlannedNet, bool> filter = null)
        {
            if (nets == null || nets.Count < 2) return 0;
            var refs = new List<(List<PlannedNet> list, int index)>(nets.Count);
            for (int i = 0; i < nets.Count; i++) refs.Add((nets, i));
            return Join(refs, tolerance, filter ?? IsPath);
        }

        /// <summary>JoinAll across every layer of a composed plan (a feature's spur joins its parent's path).</summary>
        public static int JoinAll(ComposedPlan plan, float tolerance = DefaultTolerance, Func<PlannedNet, bool> filter = null)
        {
            if (plan?.Layers == null) return 0;
            var refs = new List<(List<PlannedNet> list, int index)>();
            foreach (var l in plan.Layers)
                if (l?.Nets != null)
                    for (int i = 0; i < l.Nets.Count; i++) refs.Add((l.Nets, i));
            return Join(refs, tolerance, filter ?? IsPath);
        }

        private static int Join(List<(List<PlannedNet> list, int index)> refs, float tolerance, Func<PlannedNet, bool> filter)
        {
            var paths = new List<(List<PlannedNet> list, PlannedNet net)>();
            foreach (var (list, i) in refs) if (filter(list[i])) paths.Add((list, list[i]));
            if (paths.Count < 2) return 0;
            int joins = 0;
            // Ends are visited in plan order; pieces created by a split are appended to `paths` too,
            // so a later end can join them.
            for (int si = 0; si < paths.Count; si++)
            {
                for (int end = 0; end < 2; end++)
                {
                    var spur = paths[si].net;
                    if ((end == 0 ? spur.StartAttach : spur.EndAttach) >= 0) continue;
                    var e = end == 0 ? spur.A : spur.D;
                    // Already on a node of another path? Then CS2 joins it (identical position).
                    int hostIdx = -1; float hostD = tolerance, hostT = 0f;
                    bool onNode = false;
                    for (int hi = 0; hi < paths.Count && !onNode; hi++)
                    {
                        if (hi == si) continue;
                        var h = paths[hi].net;
                        if (!NearBox(h, e.xz, tolerance)) continue;
                        if (Same(h.A, e) || Same(h.D, e)) { onNode = true; break; }
                        float t = Closest(h, e.xz, out float d);
                        if (d < hostD) { hostD = d; hostIdx = hi; hostT = t; }
                    }
                    if (onNode || hostIdx < 0) continue;
                    var host = paths[hostIdx];
                    int before = host.list.Count;
                    var node = NodeAt(host.net, hostT, host.list);
                    if (host.list.Count > before) paths.Add((host.list, host.list[host.list.Count - 1]));
                    MoveEnd(spur, end, node);
                    joins++;
                }
            }
            return joins;
        }

        /// <summary>The node at parameter t: an existing end when the piece would be shorter than MinPiece, else a split.</summary>
        private static float3 NodeAt(PlannedNet n, float t, List<PlannedNet> owner)
        {
            float len = (float)Pricing.CurveLength(n);
            if (t * len < MinPiece) return n.A;
            if ((1f - t) * len < MinPiece) return n.D;
            owner.Add(Split(n, t));
            return n.D;
        }

        private static void MoveEnd(PlannedNet n, int end, float3 to)
        {
            if (end == 0) { var d = to - n.A; n.A = to; n.B += d; }
            else { var d = to - n.D; n.D = to; n.C += d; }
        }

        private static bool Same(float3 a, float3 b) => a.x == b.x && a.z == b.z;

        private static bool NearBox(PlannedNet n, float2 p, float r)
        {
            float2 min = math.min(math.min(n.A.xz, n.B.xz), math.min(n.C.xz, n.D.xz)) - r;
            float2 max = math.max(math.max(n.A.xz, n.B.xz), math.max(n.C.xz, n.D.xz)) + r;
            return p.x >= min.x && p.y >= min.y && p.x <= max.x && p.y <= max.y;
        }
    }
}
