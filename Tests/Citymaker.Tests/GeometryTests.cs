// Poly basics: measures, orientation, Clean, IsSimple, booleans, normalisation (owner: geometry).

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Citymaker.Core.Geometry;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    public class GeometryTests
    {
        // ───────────── measures & orientation ─────────────

        [Fact]
        public void SignedArea_IsPositiveForCcw_AndExactAtWorldCoordinates()
        {
            Assert.Equal(100f, Poly.SignedArea(G.Square(10)), 3);
            Assert.Equal(-100f, Poly.SignedArea(Poly.Reversed(G.Square(10))), 3);
            // 2 m² square 6 km from the origin: float shoelace would lose it, double relative to p0 does not.
            var far = G.Square(1.41421356f, new float2(-6123.4f, 5871.9f));
            Assert.Equal(2f, Poly.Area(far), 2);
        }

        [Fact]
        public void EnsureCcw_ReversesOnlyClockwiseRings()
        {
            var ccw = G.Square(5);
            Assert.Same(ccw, Poly.EnsureCcw(ccw));
            var cw = Poly.Reversed(ccw);
            Assert.True(Poly.SignedArea(Poly.EnsureCcw(cw)) > 0);
            Assert.True(Poly.SignedArea(Poly.EnsureCw(ccw)) < 0);
            var c3 = cw.Select(p => new float3(p.x, 1, p.y)).ToArray();
            Assert.True(Poly.SignedArea(Poly.To2D(Poly.EnsureCcw(c3))) > 0);
        }

        [Fact]
        public void ContainsAndWinding_AgreeOnLShape()
        {
            var l = G.L();
            Assert.True(Poly.Contains(l, new float2(25, 25)));
            Assert.False(Poly.Contains(l, new float2(75, 75)));
            Assert.Equal(1, Poly.WindingNumber(l, new float2(25, 75)));
            Assert.Equal(-1, Poly.WindingNumber(Poly.Reversed(l), new float2(25, 75)));
            Assert.Equal(0, Poly.WindingNumber(l, new float2(75, 75)));
        }

        [Fact]
        public void ConvexAndReflexHelpers()
        {
            Assert.True(Poly.IsConvex(G.Square(3)));
            Assert.False(Poly.IsConvex(G.L()));
            var l = G.L();
            Assert.True(Poly.IsReflex(l, 3));
            Assert.False(Poly.IsReflex(l, 0));
        }

        [Fact]
        public void ClosestPointAndDistance()
        {
            var sq = G.Square(10);
            var q = Poly.ClosestPointOnBoundary(sq, new float2(5, -3), out int seg, out float t);
            Assert.Equal(0, seg);
            Assert.Equal(0.5f, t, 3);
            Assert.Equal(new float2(5, 0), q);
            Assert.Equal(3f, Poly.DistanceToBoundary(sq, new float2(5, -3)), 4);
        }

        // ───────────── IsSimple ─────────────

        [Fact]
        public void IsSimple_DetectsCrossingTouchingAndDegenerate()
        {
            Assert.True(Poly.IsSimple(G.Square(10)));
            Assert.True(Poly.IsSimple(G.L()));
            Assert.False(Poly.IsSimple(new[] { new float2(0, 0), new float2(10, 10), new float2(10, 0), new float2(0, 10) })); // bow tie
            // Vertex touching a non-adjacent edge.
            Assert.False(Poly.IsSimple(new[] { new float2(0, 0), new float2(10, 0), new float2(10, 10), new float2(5, 0.0000001f), new float2(0, 10) }));
            // Fold-back spike and duplicate point.
            Assert.False(Poly.IsSimple(new[] { new float2(0, 0), new float2(10, 0), new float2(20, 0), new float2(10, 0.0f), new float2(10, 10), new float2(0, 10) }));
            Assert.False(Poly.IsSimple(new[] { new float2(0, 0), new float2(10, 0), new float2(10, 0), new float2(10, 10) }));
            Assert.False(Poly.IsSimple(new[] { new float2(0, 0), new float2(1, 1), new float2(2, 2) }));
            Assert.False(Poly.IsSimple(new[] { new float2(0, 0), new float2(1, 1) }));
        }

        [Fact]
        public void IsSimple_LargeRing_IsFast()
        {
            var blob = G.Blob(500, 4000);
            Assert.True(Poly.IsSimple(blob));
            double ms = G.BestMs(() => Poly.IsSimple(blob));
            Assert.True(ms < TimingBudget.Ms(20), $"{ms:F1} ms for 4000 vertices");
            // One crossing hidden in a big ring is still found.
            var bad = (float2[])blob.Clone();
            var t = bad[1000]; bad[1000] = bad[1001]; bad[1001] = t;
            Assert.False(Poly.IsSimple(bad));
        }

        // ───────────── Clean ─────────────

        [Fact]
        public void Clean_RemovesNearDuplicatesCollinearAndSpikes()
        {
            var ring = new[]
            {
                new float2(0, 0), new float2(0.01f, 0), new float2(5, 0), new float2(10, 0.0001f), // near dup + collinear
                new float2(10, 10), new float2(10, 10.002f),                                         // near dup
                new float2(5, 10), new float2(5.001f, 30), new float2(5.002f, 10),                   // needle spike up
                new float2(0, 10),
            };
            var c = Poly.Clean(ring);
            G.AssertSimpleCcw(c);
            Assert.Equal(4, c.Length);
            Assert.Equal(100f, Poly.Area(c), 1);
        }

        [Fact]
        public void Clean_ReturnsCcwAndDropsClosingDuplicate()
        {
            var cw = Poly.Reversed(G.Square(10)).Concat(new[] { new float2(0, 10) }).ToArray();
            var c = Poly.Clean(cw);
            Assert.Equal(4, c.Length);
            Assert.True(Poly.SignedArea(c) > 0);
        }

        [Fact]
        public void Clean_DegenerateInputGivesEmpty()
        {
            Assert.Empty(Poly.Clean(new float2[0]));
            Assert.Empty(Poly.Clean(new[] { new float2(0, 0), new float2(1, 0) }));
            Assert.Empty(Poly.Clean(new[] { new float2(0, 0), new float2(1, 0), new float2(2, 0) }));
            Assert.Empty(Poly.Clean(new[] { new float2(0, 0), new float2(0.01f, 0), new float2(0, 0.01f) }));
            Assert.Empty(Poly.Clean(new[] { new float2(float.NaN, 0), new float2(1, 0), new float2(0, 1) }));
        }

        [Fact]
        public void Clean_ResolvesSelfIntersection_KeepsLargestPiece()
        {
            // Figure eight: big lobe 20×20, small lobe 10×10.
            var eight = new[] { new float2(0, 0), new float2(20, 20), new float2(20, 0), new float2(0, 20) };
            var c = Poly.Clean(eight);
            G.AssertSimpleCcw(c);
            Assert.Equal(100f, Poly.Area(c), 1); // bow tie halves are 100 each

            var lop = new[] { new float2(0, 0), new float2(40, 0), new float2(40, 40), new float2(0, 40), new float2(0, 10), new float2(-10, 10), new float2(-10, 20), new float2(5, 20), new float2(5, 25), new float2(0, 25) };
            var all = Poly.CleanAll(lop);
            Assert.True(all.Count >= 1);
            foreach (var r in all) G.AssertSimpleCcw(r.Outer);
        }

        [Fact]
        public void Clean_SelfTouchingRing_SplitsIntoSimplePieces()
        {
            // Two squares sharing one corner, written as a single ring passing that corner twice.
            var ring = new[] { new float2(0, 0), new float2(10, 0), new float2(10, 10), new float2(20, 10), new float2(20, 20), new float2(10, 20), new float2(10, 10), new float2(0, 10) };
            Assert.False(Poly.IsSimple(ring));
            var all = Poly.CleanAll(ring);
            Assert.Equal(2, all.Count);
            foreach (var r in all) { G.AssertSimpleCcw(r.Outer); Assert.Equal(100f, r.Area, 1); }
            G.AssertSimpleCcw(Poly.Clean(ring));
        }

        [Fact]
        public void Clean_HugeAndTinyAreasSurvive()
        {
            var huge = G.Rect(-2500, -2500, 2500, 2500);
            Assert.InRange(Poly.Area(Poly.Clean(huge)), 25e6f - 10f, 25e6f + 10f);
            var tiny = G.Square(10, new float2(-4321.5f, 3456.25f));
            Assert.Equal(100f, Poly.Area(Poly.Clean(tiny)), 1);
        }

        [Fact]
        public void Clean_DenseCollinearRunCollapses()
        {
            var pts = new List<float2>();
            for (int i = 0; i <= 1000; i++) pts.Add(new float2(i * 0.1f, 0));
            pts.Add(new float2(100, 50));
            pts.Add(new float2(0, 50));
            var c = Poly.Clean(pts);
            Assert.Equal(4, c.Length);
            Assert.Equal(5000f, Poly.Area(c), 0);
        }

        [Fact]
        public void Clean_500Vertices_Under50ms()
        {
            var blob = G.Blob(200, 500);
            double ms = G.BestMs(() => Poly.Clean(blob));
            Assert.True(ms < TimingBudget.Ms(50), $"{ms:F1} ms");
            var c = Poly.Clean(blob);
            G.AssertSimpleCcw(c);
        }

        [Fact]
        public void RemoveCollinear_KeepsRealCorners()
        {
            var ring = new[] { new float2(0, 0), new float2(5, 0), new float2(10, 0), new float2(10, 10), new float2(0, 10) };
            Assert.Equal(4, Poly.RemoveCollinear(ring).Length);
        }

        // ───────────── booleans ─────────────

        [Fact]
        public void Boolean_OverlappingSquares()
        {
            var a = G.Square(10);
            var b = G.Square(10, new float2(5, 5));
            Assert.Equal(175f, G.TotalArea(Poly.Union(a, b)), 2);
            Assert.Equal(25f, G.TotalArea(Poly.Intersection(a, b)), 2);
            Assert.Equal(75f, G.TotalArea(Poly.Difference(a, b)), 2);
            Assert.Equal(150f, G.TotalArea(Poly.Xor(a, b)), 2);
            foreach (var r in Poly.Union(a, b)) G.AssertSimpleCcw(r.Outer);
            Assert.Single(Poly.Union(a, b));
            Assert.Equal(2, Poly.Xor(a, b).Count);
        }

        [Fact]
        public void Boolean_DifferenceInside_MakesHole()
        {
            var r = Poly.Difference(G.Square(10), G.Square(4, new float2(3, 3)));
            Assert.Single(r);
            Assert.Single(r[0].Holes);
            Assert.True(Poly.SignedArea(r[0].Holes[0]) < 0);
            Assert.Equal(84f, r[0].Area, 2);
            Assert.True(r[0].Contains(new float2(1, 1)));
            Assert.False(r[0].Contains(new float2(5, 5)));
        }

        [Fact]
        public void Boolean_SharedEdges_MergeExactly()
        {
            // Adjacent unit cells (shared edges cancel) → one ring of 4 corners.
            var cells = new List<IReadOnlyList<float2>>();
            for (int x = 0; x < 5; x++) for (int y = 0; y < 3; y++) cells.Add(G.Square(1, new float2(x, y)));
            var u = Poly.UnionAll(cells);
            Assert.Single(u);
            Assert.Equal(4, u[0].Outer.Length);
            Assert.Equal(15f, u[0].Area, 3);
        }

        [Fact]
        public void Boolean_DisjointAndContained()
        {
            Assert.Empty(Poly.Intersection(G.Square(1), G.Square(1, new float2(5, 5))));
            Assert.Equal(2, Poly.Union(G.Square(1), G.Square(1, new float2(5, 5))).Count);
            var inner = Poly.Intersection(G.Square(10), G.Square(2, new float2(3, 3)));
            Assert.Single(inner);
            Assert.Equal(4f, inner[0].Area, 3);
        }

        [Fact]
        public void Boolean_TouchingAtPoint_GivesTwoSimpleRings()
        {
            var u = Poly.Union(G.Square(10), G.Square(10, new float2(10, 10)));
            Assert.Equal(2, u.Count);
            foreach (var r in u) G.AssertSimpleCcw(r.Outer);
        }

        [Fact]
        public void Boolean_CollinearOverlapAndNearCoincidentEdges()
        {
            var a = G.Rect(0, 0, 10, 10);
            var b = G.Rect(10.00001f, 2, 20, 8); // gap of 10 µm → snapped together
            var u = Poly.Union(a, b);
            Assert.Single(u);
            G.AssertSimpleCcw(u[0].Outer);
            Assert.Equal(160f, u[0].Area, 1);
        }

        [Fact]
        public void Boolean_RegionsWithHoles()
        {
            var ring = Poly.Difference(G.Square(20), G.Square(10, new float2(5, 5)))[0];
            var cut = Poly.Boolean(new[] { ring }, new[] { new Region(G.Rect(-1, 8, 21, 12)) }, BoolOp.Difference);
            Assert.Equal(2, cut.Count);
            Assert.Equal(300f - 2 * 4 * 5 * 2 / 2f, G.TotalArea(cut), 1);
            foreach (var r in cut) { Assert.Empty(r.Holes); G.AssertSimpleCcw(r.Outer); }
        }

        [Fact]
        public void Boolean_WorldCoordinates_FiveKilometres()
        {
            var o = new float2(-5000, 4000);
            var a = G.Square(300, o);
            var b = G.Square(300, o + new float2(150, 150));
            Assert.Equal(22500f, G.TotalArea(Poly.Intersection(a, b)), 0);
        }

        [Fact]
        public void Normalize_EvenOddVsNonZero()
        {
            // Ring that winds twice around the centre square.
            var twice = new[]
            {
                new float2(0, 0), new float2(30, 0), new float2(30, 30), new float2(0, 30), new float2(0, 10),
                new float2(10, 10), new float2(20, 10), new float2(20, 20), new float2(10, 20), new float2(10, 10), new float2(0, 10)
            };
            _ = twice;
            var star = G.Star(10, 4, 5);
            // A pentagram drawn as one self-crossing path (vertices 0,2,4,1,3 of a pentagon).
            var penta = G.Circle(10, 5);
            var pentagram = new[] { penta[0], penta[2], penta[4], penta[1], penta[3] };
            var nz = Poly.Normalize(pentagram, FillRule.NonZero);
            var eo = Poly.Normalize(pentagram, FillRule.EvenOdd);
            Assert.Single(nz);
            Assert.Equal(5, eo.Count);
            Assert.True(G.TotalArea(nz) > G.TotalArea(eo));
            Assert.True(Poly.IsSimple(Poly.Clean(star)));
        }
    }
}
