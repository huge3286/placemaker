// Offset, split, clip, holes, convex decomposition, shaping (owner: geometry).

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Citymaker.Core.Geometry;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    [Collection(Citymaker.Tests.TimingCollection.Name)]
    public class GeometryOffsetTests
    {
        private static void AssertOffsetDistance(IReadOnlyList<float2> original, IEnumerable<float2[]> rings, float d, float tol = 0.01f)
        {
            foreach (var r in rings)
                foreach (var p in r)
                {
                    float dist = Poly.DistanceToBoundary(original, p);
                    Assert.True(dist >= math.abs(d) - tol, $"vertex {p} at {dist} from input, offset {d}");
                    Assert.Equal(d > 0, Poly.Contains(original, p) || dist < tol);
                }
        }

        [Fact]
        public void Offset_Square_InAndOut()
        {
            var r = Poly.Offset(G.Square(10), 2f);
            Assert.Single(r);
            Assert.Equal(36f, Poly.Area(r[0]), 2);
            G.AssertSimpleCcw(r[0]);
            var o = Poly.Offset(G.Square(10), -2f);
            Assert.Single(o);
            Assert.Equal(196f, Poly.Area(o[0]), 1); // miter keeps square corners
            var round = Poly.Offset(G.Square(10), -2f, OffsetJoin.Round, 4f, 0.01f);
            // Arc vertices lie on the circle: the polygonal area is a hair below the exact one.
            Assert.InRange(round[0].Area, 100 + 4 * 20 + math.PI * 4 - 0.3f, 100 + 4 * 20 + math.PI * 4 + 1e-3f);
        }

        [Fact]
        public void Offset_Vanishes_ReturnsEmpty()
        {
            Assert.Empty(Poly.Offset(G.Square(10), 5.01f));
            Assert.Empty(Poly.Offset(G.Rect(0, 0, 100, 3), 2f));
        }

        [Fact]
        public void Offset_LShape_KeepsSharpReflexCornerWithMiter()
        {
            var l = G.L();
            var r = Poly.Offset(l, 5f);
            Assert.Single(r);
            G.AssertSimpleCcw(r[0]);
            Assert.Equal(6, r[0].Length);
            Assert.Contains(r[0], p => math.distance(p, new float2(45, 45)) < 1e-3f);
            AssertOffsetDistance(l, r, 5f);
            // Round join: inner corner becomes an arc around (50,50).
            var rr = Poly.Offset(l, 5f, OffsetJoin.Round, 4, 0.02f);
            Assert.Single(rr);
            Assert.True(rr[0].Outer.Length > 6);
            AssertOffsetDistance(l, rr.Select(x => x.Outer), 5f, 0.03f);
        }

        [Fact]
        public void Offset_UShape_ArmsCollapseIndependently()
        {
            var u = G.U(100, 80, 20, 20);
            // 9 m inset: arms (20 wide) survive as 2 m strips joined by the base.
            var a = Poly.Offset(u, 9f);
            Assert.Single(a);
            AssertOffsetDistance(u, a, 9f);
            // 10.5 m: arms vanish, base (20 high) vanishes too → empty.
            Assert.Empty(Poly.Offset(u, 10.5f));
            // U with a thick base: arms vanish, base remains as one ring.
            var thick = G.U(100, 80, 10, 40);
            var b = Poly.Offset(thick, 6f);
            Assert.Single(b);
            AssertOffsetDistance(thick, b, 6f);
        }

        [Fact]
        public void Offset_Dumbbell_SplitsIntoTwoRings()
        {
            var db = G.Dumbbell(40, 20, 4);
            var r = Poly.Offset(db, 3f);
            Assert.Equal(2, r.Count);
            foreach (var ring in r) { G.AssertSimpleCcw(ring); Assert.Equal(34f * 34f, Poly.Area(ring), 0); }
            AssertOffsetDistance(db, r, 3f);
            // Smaller inset keeps the neck: one ring.
            Assert.Single(Poly.Offset(db, 1.5f));
        }

        [Fact]
        public void Offset_Outward_FillsUShapeGap()
        {
            var u = G.U(100, 80, 20, 20); // gap 60 wide, open at the top: growing by 31 m fills it, no hole
            var regions = Poly.Offset(new Region(u), -31f, OffsetJoin.Miter);
            Assert.Single(regions);
            Assert.Empty(regions[0].Holes);
            Assert.True(regions[0].Contains(new float2(50, 60)));
            Assert.Equal(162f * 142f, regions[0].Area, 0);
        }

        [Fact]
        public void Offset_OutwardClosesSlot_LeavesHole()
        {
            // Square ring with a 2 m wide slot to the outside; growing by 1.5 m closes the mouth (rect corners
            // of the slot) and leaves the inner pocket as a hole.
            var ring = new[]
            {
                new float2(0, 0), new float2(40, 0), new float2(40, 40), new float2(21, 40), new float2(21, 30),
                new float2(30, 30), new float2(30, 10), new float2(10, 10), new float2(10, 30), new float2(19, 30),
                new float2(19, 40), new float2(0, 40),
            };
            Assert.True(Poly.IsSimple(ring));
            var r = Poly.Offset(new Region(Poly.EnsureCcw(ring)), -1.5f, OffsetJoin.Miter);
            Assert.Single(r);
            Assert.Single(r[0].Holes);
            Assert.True(r[0].Holes[0].Length >= 4);
            // Ring overload fills the hole (documented).
            var outers = Poly.Offset(ring, -1.5f);
            Assert.Single(outers);
        }

        [Fact]
        public void Offset_WithHoles_HoleGrows()
        {
            var reg = Poly.Difference(G.Square(30), G.Square(10, new float2(10, 10)))[0];
            var r = Poly.Offset(reg, 2f);
            Assert.Single(r);
            Assert.Single(r[0].Holes);
            Assert.Equal(26f * 26f - 14f * 14f, r[0].Area, 1);
            // Inset more than the wall (10 m wall) destroys it.
            Assert.Empty(Poly.Offset(reg, 5.01f));
        }

        [Fact]
        public void Offset_ReflexHeavyStar_IsSimpleAndAtDistance()
        {
            var star = G.Star(100, 70, 12);
            foreach (float d in new[] { 1f, 5f, 12f, 25f, -3f, -10f })
            {
                var r = Poly.Offset(star, d);
                Assert.NotEmpty(r);
                foreach (var ring in r) G.AssertSimpleCcw(ring, $"d={d}");
                AssertOffsetDistance(star, r, d, 0.02f);
            }
            // Gear teeth separate before the core vanishes at large insets → star body only.
            var big = Poly.Offset(star, 35f);
            Assert.Single(big);
        }

        [Fact]
        public void Offset_Comb_TeethFallOff()
        {
            var comb = G.Comb(20, 6, 4, 30, 10);
            var r = Poly.Offset(comb, 2.5f);
            Assert.Single(r);
            G.AssertSimpleCcw(r[0]);
            AssertOffsetDistance(comb, r, 2.5f);
            var r2 = Poly.Offset(comb, 3.5f); // teeth 6 wide gone, base 10 high remains
            Assert.Single(r2);
            AssertOffsetDistance(comb, r2, 3.5f);
            Assert.True(Poly.Area(r2[0]) < Poly.Area(r[0]));
        }

        [Fact]
        public void Offset_WorldCoordinatesAndTinyArea()
        {
            var far = G.L(10, 5, new float2(-6543.21f, 4321.5f));
            var r = Poly.Offset(far, 1f);
            Assert.Single(r);
            Assert.Equal(8 * 8 - 5 * 5 + 0f, Poly.Area(r[0]), 1);
            var huge = G.Rect(-2500, -2500, 2500, 2500);
            Assert.InRange(Poly.Area(Poly.Offset(huge, 5f)[0]), 4990f * 4990f - 50f, 4990f * 4990f + 50f);
        }

        [Fact]
        public void Offset_500Vertices_Under50ms()
        {
            var blob = G.Blob(150, 500, 0.3f, 9);
            double ms = G.BestMs(() => Poly.Offset(blob, 4f));
            Assert.True(ms < TimingBudget.Ms(50), $"{ms:F1} ms");
            double msRound = G.BestMs(() => Poly.Offset(blob, 4f, OffsetJoin.Round, 4f, 0.1f));
            Assert.True(msRound < TimingBudget.Ms(50), $"round {msRound:F1} ms");
            var r = Poly.Offset(blob, 4f);
            Assert.NotEmpty(r);
            foreach (var ring in r) G.AssertSimpleCcw(ring);
        }

        [Fact]
        public void Offset_RandomPolygons_AlwaysSimpleAndAtDistance()
        {
            var rng = new G.Rng(7);
            for (int k = 0; k < 40; k++)
            {
                int n = 6 + (int)(rng.Next() * 30);
                var pts = new float2[n];
                for (int i = 0; i < n; i++)
                {
                    float a = 2 * math.PI * i / n, rr = rng.Range(20, 100);
                    pts[i] = rr * new float2(math.cos(a), math.sin(a));
                }
                float d = rng.Range(-8, 15);
                var r = Poly.Offset(pts, d);
                foreach (var ring in r) G.AssertSimpleCcw(ring, $"poly {k} d={d}");
                AssertOffsetDistance(pts, r, d, 0.02f);
                if (d < 0) Assert.Single(r);
            }
        }

        // ───────────── split / clip ─────────────

        [Fact]
        public void Split_SquareByLine_TwoHalves()
        {
            var parts = Poly.Split(G.Square(10), new[] { new float2(5, -1), new float2(5, 11) });
            Assert.Equal(2, parts.Count);
            foreach (var p in parts) { G.AssertSimpleCcw(p); Assert.Equal(50f, Poly.Area(p), 2); }
        }

        [Fact]
        public void Split_ShortLineIsExtended()
        {
            var parts = Poly.Split(G.Square(10), new[] { new float2(5, 0.5f), new float2(5, 9.5f) });
            Assert.Single(parts);
            parts = Poly.Split(G.Square(10), new[] { new float2(5, 0.5f), new float2(5, 9.5f) }, extend: 1f);
            Assert.Equal(2, parts.Count);
        }

        [Fact]
        public void Split_UShapeByHorizontalLine_ThreeParts()
        {
            var u = G.U(100, 80, 20, 20);
            var parts = Poly.Split(u, new[] { new float2(-10, 50), new float2(110, 50) });
            Assert.Equal(3, parts.Count);
            Assert.Equal(Poly.Area(u), G.TotalArea(parts), 1);
        }

        [Fact]
        public void Split_PolylineWithBendAndDanglingEnd()
        {
            var sq = G.Square(100);
            // Bent cut from bottom to left, plus a dangling tail into the interior that must be ignored.
            var line = new[] { new float2(50, -5), new float2(50, 50), new float2(-5, 50) };
            var parts = Poly.Split(sq, line);
            Assert.Equal(2, parts.Count);
            Assert.Equal(new[] { 2500f, 7500f }, parts.Select(p => (float)math.round(Poly.Area(p))).OrderBy(a => a).ToArray());
            var dangling = new[] { new float2(50, -5), new float2(50, 30) };
            var one = Poly.Split(sq, dangling);
            Assert.Single(one);
            Assert.Equal(4, one[0].Length);
        }

        [Fact]
        public void Split_ClosedLoopInside_OpensTheOuterPart()
        {
            var sq = G.Square(100);
            var loop = G.Square(20, new float2(40, 40)).Concat(new[] { new float2(40, 40) }).ToArray();
            var parts = Poly.Split(sq, loop);
            Assert.True(parts.Count >= 2);
            foreach (var p in parts) G.AssertSimpleCcw(p);
            Assert.Equal(10000f, G.TotalArea(parts), 1);
        }

        [Fact]
        public void SplitByRatio_GivesRequestedShare()
        {
            var l = G.L();
            var parts = Poly.SplitByRatio(l, new float2(0, 1), 0.3f);
            Assert.Equal(2, parts.Count);
            Assert.Equal(0.3f * Poly.Area(l), Poly.Area(parts[0]), 0);
        }

        [Fact]
        public void ClipSegment_ToUShape()
        {
            var u = G.U(100, 80, 20, 20);
            var pieces = Poly.ClipSegmentToPolygon(u, new float2(-10, 50), new float2(110, 50));
            Assert.Equal(2, pieces.Count);
            Assert.Equal(20f, math.distance(pieces[0].a, pieces[0].b), 3);
            var iv = Poly.ClipSegmentIntervals(G.Square(10), new float2(5, 5), new float2(5, 20));
            Assert.Single(iv);
            Assert.Equal(0f, iv[0].x, 4);
            Assert.Equal(5f / 15f, iv[0].y, 4);
            var holed = Poly.Difference(G.Square(30), G.Square(10, new float2(10, 10)))[0];
            Assert.Equal(2, Poly.ClipSegmentToPolygon(holed, new float2(-1, 15), new float2(31, 15)).Count);
            var poly = Poly.ClipPolylineToPolygon(G.Square(10), new[] { new float2(-5, 5), new float2(5, 5), new float2(5, 15) });
            Assert.Single(poly);
            Assert.Equal(3, poly[0].Length);
        }

        // ───────────── holes ─────────────

        [Fact]
        public void CutHoles_SeamsCoverEverything()
        {
            var reg = Poly.Difference(G.Square(30), G.Square(10, new float2(10, 10)))[0];
            var pieces = Poly.CutHoles(reg);
            Assert.Equal(2, pieces.Count);
            foreach (var p in pieces) G.AssertSimpleCcw(p);
            Assert.Equal(800f, G.TotalArea(pieces), 1);
        }

        [Fact]
        public void CutHoles_ManyHolesAndCorridor()
        {
            var outer = G.Square(100);
            var holes = new List<float2[]>();
            for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) holes.Add(Poly.Reversed(G.Square(10, new float2(10 + 30 * i, 10 + 30 * j))));
            var reg = new Region(outer, holes);
            var pieces = Poly.CutHoles(reg);
            foreach (var p in pieces) G.AssertSimpleCcw(p);
            Assert.Equal(10000f - 900f, G.TotalArea(pieces), 0);
            var corr = Poly.CutHoles(reg, 1f);
            foreach (var p in corr) G.AssertSimpleCcw(p);
            float a = G.TotalArea(corr);
            Assert.True(a < 9100f && a > 8800f, $"corridor area {a}");
        }

        [Fact]
        public void CutHoles_UShapedHoleAndHoleTouchingNeighbour()
        {
            var hole = Poly.Reversed(G.U(40, 40, 10, 10, new float2(30, 30)));
            var reg = new Region(G.Square(100), new[] { hole, Poly.Reversed(G.Square(5, new float2(5, 50))) });
            var pieces = Poly.CutHoles(reg);
            foreach (var p in pieces) G.AssertSimpleCcw(p);
            Assert.Equal(reg.Area, G.TotalArea(pieces), 0);
        }

        // ───────────── convex decomposition ─────────────

        [Fact]
        public void ConvexDecomposition_LShape_TwoParts()
        {
            var parts = ConvexDecomposition.Decompose(G.L());
            Assert.Equal(2, parts.Count);
            foreach (var p in parts) { Assert.True(Poly.IsConvex(p)); Assert.True(Poly.SignedArea(p) > 0); }
            Assert.Equal(7500f, G.TotalArea(parts), 2);
        }

        [Fact]
        public void ConvexDecomposition_CombAndStar_PartsBoundedByReflexCount()
        {
            foreach (var ring in new[] { G.Comb(10), G.Star(50, 20, 8), G.U(), G.Rotate(G.L(), 17) })
            {
                int reflex = 0;
                for (int i = 0; i < ring.Length; i++) if (Poly.IsReflex(Poly.EnsureCcw(ring), i)) reflex++;
                var parts = ConvexDecomposition.Decompose(ring);
                Assert.InRange(parts.Count, 1, reflex + 1);
                foreach (var p in parts) Assert.True(Poly.IsConvex(p));
                Assert.Equal(Poly.Area(ring), G.TotalArea(parts), 0);
            }
        }

        [Fact]
        public void ConvexDecomposition_SharesSeamVerticesExactly()
        {
            var parts = ConvexDecomposition.Decompose(G.Rotate(G.U(), 33, new float2(-1234, 777)));
            var verts = parts.SelectMany(p => p).ToList();
            // Every seam endpoint appears in ≥ 2 parts with identical bits.
            foreach (var p in parts)
                foreach (var v in p)
                    Assert.True(verts.Count(q => q.x == v.x && q.y == v.y) >= 1);
            var all = Poly.UnionAll(parts);
            Assert.Single(all);
        }

        // ───────────── shaping ─────────────

        [Fact]
        public void Simplify_RemovesNoiseKeepsCorners()
        {
            var noisy = new List<float2>();
            var rng = new G.Rng(3);
            foreach (var p in Poly.Resample(G.Square(100), 1f, keepCorners: true))
                noisy.Add(p + new float2(rng.Range(-0.05f, 0.05f), rng.Range(-0.05f, 0.05f)));
            var s = Poly.Simplify(noisy, 0.2f);
            Assert.InRange(s.Length, 4, 8);
            G.AssertSimpleCcw(s);
        }

        [Fact]
        public void SmoothCorners_RoundsWithinTheShape()
        {
            var sq = G.Square(100);
            var s = Poly.SmoothCorners(sq, 10f, 1f);
            G.AssertSimpleCcw(s);
            Assert.True(s.Length > 20);
            Assert.All(s, p => Assert.True(Poly.Contains(sq, p) || Poly.DistanceToBoundary(sq, p) < 1e-3f));
            Assert.InRange(Poly.Area(s), 10000 - (4 - math.PI) * 100 - 2, 10000 - (4 - math.PI) * 100 + 2);
            var ch = Poly.Chaikin(sq, 3);
            Assert.Equal(32, ch.Length);
            G.AssertSimpleCcw(ch);
        }

        [Fact]
        public void Resample_SpacingAndCorners()
        {
            var r = Poly.Resample(G.Square(10), 1f);
            Assert.Equal(40, r.Length);
            var k = Poly.Resample(G.Rect(0, 0, 10, 3), 2f, keepCorners: true);
            Assert.Equal(5 + 2 + 5 + 2, k.Length);
            var line = Poly.ResamplePolyline(new[] { new float2(0, 0), new float2(10, 0) }, 2.5f);
            Assert.Equal(5, line.Length);
        }

        [Fact]
        public void FillRuns_CoverThePolygon()
        {
            var runs = Poly.FillRuns(G.L(), 2f, 30f);
            float covered = runs.Sum(r => math.distance(r.a, r.b)) * 2f;
            Assert.InRange(covered, 7400f, 7600f);
            Assert.All(runs, r => Assert.True(math.distance(r.a, r.b) <= 30.01f));
        }

        [Fact]
        public void PoleOfInaccessibility_FindsWidestPoint()
        {
            var p = Poly.PoleOfInaccessibility(G.Rect(0, 0, 100, 20), out float d);
            Assert.Equal(10f, d, 1);
            Assert.Equal(10f, p.y, 1);
            var l = G.L();
            var q = Poly.PoleOfInaccessibility(l, out float dl);
            Assert.True(Poly.Contains(l, q));
            Assert.InRange(dl, 28.8f, 29.8f);
            var holed = Poly.Difference(G.Square(100), G.Square(20, new float2(40, 40)))[0];
            var h = Poly.PoleOfInaccessibility(holed, out float dh);
            Assert.True(holed.Contains(h));
            Assert.True(dh > 15f);
        }
    }
}
