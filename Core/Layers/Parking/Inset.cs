// Ported from ParkingLotTool (KruemelGames, GPL-3.0) — Geometry/Zellen/Planung.cs
// (Layoutplanung.Innenrand, RingUnsauber, ErodierterRing). Translated, made non-throwing.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Citymaker.Core.Layers.Parking
{
    /// <summary>
    /// Inward offset of a CCW ring that keeps edge i of the result parallel to edge i of the input
    /// (edges that vanish keep their slot with zero length). The perimeter row and the ring road
    /// both rely on that correspondence.
    /// </summary>
    internal static class Inset
    {
        /// <summary>Returns the inset ring (same vertex count, may contain zero-length edges) or null.</summary>
        public static double2[] Of(IReadOnlyList<double2> outer, double distance)
        {
            int n = outer.Count;
            if (n < 3) return null;
            var lines = new (double2 P, double2 D)[n];
            for (int i = 0; i < n; i++)
            {
                var a = outer[i];
                var d = outer[(i + 1) % n] - a;
                double len = PGeo.Len(d);
                if (len == 0) return null;
                var leftNormal = new double2(-d.y / len, d.x / len);
                lines[i] = (a + leftNormal * distance, d);
            }

            var inner = new double2[n];
            for (int i = 0; i < n; i++)
            {
                var prev = lines[PGeo.Mod(i - 1, n)];
                var cur = lines[i];
                double den = PGeo.Cross(prev.D, cur.D);
                if (den == 0)
                {
                    // A straight-through vertex (both edges parallel and same direction) is no
                    // error: the offset edge simply continues. Measured on PLT's "Referenz 08s"
                    // shape (point 6 at (68,83)), which used to abort the whole build. A fold-back
                    // (parallel, opposite direction) has no defined inset.
                    if (PGeo.Dot(prev.D, cur.D) <= 0) return null;
                    inner[i] = cur.P;
                    continue;
                }
                double t = PGeo.Cross(cur.P - prev.P, cur.D) / den;
                inner[i] = prev.P + prev.D * t;
            }

            // An edge that runs backwards after offsetting has length zero; its neighbours then
            // intersect directly. PLT report AJ54 (2026-09-27): a 24.1 m edge after a 57.6° corner,
            // offset 13.9 m, ran 0.59 m backwards and produced a 5.2° spike that flipped a triangle
            // after CS2's 0.1 m inset and laid 14.6 m² of grass onto the aisle. Repeats until no
            // edge runs backwards (one removal can trigger the next).
            var gone = new bool[n];
            for (int round = 0; round < n; round++)
            {
                bool any = false;
                for (int i = 0; i < n; i++)
                {
                    if (gone[i]) continue;
                    var end = inner[(i + 1) % n];
                    if (PGeo.Dot(end - inner[i], lines[i].D) >= 0) continue;
                    gone[i] = true; any = true;
                }
                if (!any) break;
                int alive = 0;
                foreach (var g in gone) if (!g) alive++;
                if (alive < 3) return null;
                for (int i = 0; i < n; i++)
                {
                    if (gone[i]) continue;
                    int prevI = PGeo.Mod(i - 1, n);
                    while (gone[prevI]) prevI = PGeo.Mod(prevI - 1, n);
                    var prev = lines[prevI];
                    var cur = lines[i];
                    double den = PGeo.Cross(prev.D, cur.D);
                    var cut = den == 0 ? cur.P : prev.P + prev.D * (PGeo.Cross(cur.P - prev.P, cur.D) / den);
                    for (int k = PGeo.Mod(prevI + 1, n); ; k = PGeo.Mod(k + 1, n))
                    {
                        inner[k] = cut;
                        if (k == i) break;
                    }
                }
            }

            // Only when the ring is demonstrably unclean — otherwise every shape stays bit-identical.
            if (IsUnclean(outer, inner, distance))
            {
                var eroded = Eroded(outer, lines, distance);
                if (eroded != null) inner = eroded;
                else return null;
            }

            if (PGeo.SignedArea(inner) <= 0) return null;
            foreach (var p in inner)
                if (!PGeo.Contains(outer, p) && PGeo.DistToBoundary(outer, p) > 1e-6) return null;
            return inner;
        }

        /// <summary>
        /// An arm narrower than twice the offset collapses: pieces of the inset then lie closer to
        /// the outline than the offset, or outside it (PLT issue #10, a U whose arm is 10.5 m wide).
        /// </summary>
        private static bool IsUnclean(IReadOnlyList<double2> outer, double2[] inner, double distance)
        {
            int n = inner.Length;
            for (int i = 0; i < n; i++)
            {
                var a = inner[i]; var b = inner[(i + 1) % n];
                if (PGeo.Len(b - a) < 1e-9) continue;
                for (int s = 0; s <= 4; s++)
                {
                    var p = a + (b - a) * (s * 0.25);
                    bool inside = PGeo.Contains(outer, p) || PGeo.DistToBoundary(outer, p) < 1e-9;
                    if (!inside || PGeo.DistToBoundary(outer, p) < distance - 0.01) return true;
                }
            }
            return false;
        }

        private sealed class Piece { public int Edge; public double2 Start, End; }

        /// <summary>
        /// Boundary of the eroded outline: all points at least <paramref name="distance"/> from every
        /// outline edge. Built from pieces of the offset lines (distance to a segment is convex along
        /// a line, so the too-close part is one interval). Keeps the largest CCW loop; edge i keeps
        /// its slot (zero length when it has no piece). Returns null when that is impossible.
        /// </summary>
        private static double2[] Eroded(IReadOnlyList<double2> outer, (double2 P, double2 D)[] lines, double distance)
        {
            int n = outer.Count;
            const double eps = 1e-6;
            var raw = new double2[n];
            for (int i = 0; i < n; i++)
            {
                var v = lines[PGeo.Mod(i - 1, n)]; var w = lines[i];
                double den = PGeo.Cross(v.D, w.D);
                raw[i] = Math.Abs(den) < 1e-12 ? w.P : v.P + v.D * (PGeo.Cross(w.P - v.P, w.D) / den);
            }

            var pieces = new List<Piece>();
            for (int i = 0; i < n; i++)
            {
                var basePt = lines[i].P;
                double len = PGeo.Len(lines[i].D);
                if (len < 1e-12) continue;
                var u = lines[i].D / len;
                double t0 = PGeo.Dot(raw[i] - basePt, u);
                double t1 = PGeo.Dot(raw[(i + 1) % n] - basePt, u);
                if (t1 <= t0 + eps) continue;
                var free = new List<double2> { new double2(t0, t1) };
                for (int j = 0; j < n && free.Count > 0; j++)
                {
                    if (j == i) continue;
                    var a = outer[j]; var b = outer[(j + 1) % n];
                    Func<double, double> dist = t => PGeo.DistPointSeg(basePt + u * t, a, b);
                    double lo = t0, hi = t1;
                    for (int k = 0; k < 80; k++)
                    {
                        double m1 = lo + (hi - lo) / 3, m2 = hi - (hi - lo) / 3;
                        if (dist(m1) < dist(m2)) hi = m2; else lo = m1;
                    }
                    double tm = (lo + hi) / 2;
                    if (dist(tm) >= distance - eps) continue;
                    Func<double, double, double> edgeOf = (inT, outT) =>
                    {
                        if (dist(outT) < distance - eps) return outT;
                        for (int k = 0; k < 80; k++)
                        {
                            double m = (inT + outT) / 2;
                            if (dist(m) < distance - eps) inT = m; else outT = m;
                        }
                        return (inT + outT) / 2;
                    };
                    double fromBad = edgeOf(tm, t0), toBad = edgeOf(tm, t1);
                    var next = new List<double2>();
                    foreach (var f in free)
                    {
                        if (toBad <= f.x || fromBad >= f.y) { next.Add(f); continue; }
                        if (fromBad > f.x + eps) next.Add(new double2(f.x, fromBad));
                        if (toBad < f.y - eps) next.Add(new double2(toBad, f.y));
                    }
                    free = next;
                }
                foreach (var f in free)
                    if (f.y - f.x > 1e-4)
                        pieces.Add(new Piece { Edge = i, Start = basePt + u * f.x, End = basePt + u * f.y });
            }
            if (pieces.Count < 3) return null;

            var used = new bool[pieces.Count];
            List<Piece> best = null;
            double bestArea = 0;
            for (int start = 0; start < pieces.Count; start++)
            {
                if (used[start]) continue;
                var loop = new List<Piece>();
                int cur = start;
                bool closed = false;
                while (cur >= 0 && !used[cur])
                {
                    used[cur] = true;
                    loop.Add(pieces[cur]);
                    var end = pieces[cur].End;
                    int nxt = -1; double nd = 1e-3;
                    for (int k = 0; k < pieces.Count; k++)
                    {
                        double d = PGeo.Len(pieces[k].Start - end);
                        if (d < nd) { nd = d; nxt = k; }
                    }
                    if (nxt == start) { closed = true; break; }
                    cur = nxt;
                }
                if (!closed || loop.Count < 3) continue;
                var pts = new List<double2>();
                foreach (var p in loop) pts.Add(p.Start);
                double area = PGeo.SignedArea(pts);
                if (area > bestArea) { bestArea = area; best = loop; }
            }
            if (best == null) return null;

            var byEdge = new Piece[n];
            foreach (var p in best)
            {
                if (byEdge[p.Edge] != null) return null;
                byEdge[p.Edge] = p;
            }
            // pieces must follow outline order
            int first = Array.FindIndex(byEdge, p => p != null);
            int offset = best.FindIndex(p => p.Edge == first);
            int seq = 0;
            for (int e = 0; e < n; e++)
            {
                if (byEdge[e] == null) continue;
                if (best[(offset + seq) % best.Count].Edge != e) return null;
                seq++;
            }

            var result = new double2[n];
            for (int i = 0; i < n; i++)
            {
                int k = i;
                while (byEdge[k] == null) k = (k + 1) % n;
                result[i] = byEdge[k].Start;
            }
            return result;
        }
    }
}
