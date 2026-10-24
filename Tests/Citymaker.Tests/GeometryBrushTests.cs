// BrushMask (paint selection) — adapted from Grid Road Generator's BrushMaskTests (owner: geometry).

using System.Collections.Generic;
using System.Linq;
using Citymaker.Core.Geometry;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    public class GeometryBrushTests
    {
        private static float Area(List<float3> ring) => Poly.Area(Poly.To2D(ring));

        private static void Path(BrushMask mask, float radius, bool erase, params float[] xz)
        {
            for (int i = 2; i + 1 < xz.Length; i += 2)
            {
                if (i == 2) mask.Stamp(new float2(xz[0], xz[1]), radius, erase);
                mask.Stroke(new float2(xz[i - 2], xz[i - 1]), new float2(xz[i], xz[i + 1]), radius, erase);
            }
        }

        private static BrushMask Sample()
        {
            var mask = new BrushMask();
            Path(mask, 70f, false, 120, 380, 200, 300, 300, 270, 400, 300, 470, 220);
            Path(mask, 45f, false, 300, 270, 310, 150, 260, 90);
            Path(mask, 30f, true, 330, 330, 360, 345);
            return mask;
        }

        [Fact]
        public void Outline_IsSimpleSmoothAndMatchesThePaintedArea()
        {
            BrushMask mask = Sample();
            List<float3> ring = mask.Outline();
            Assert.True(ring.Count >= 8);
            var flat = Poly.To2D(ring);
            G.AssertSimpleCcw(flat);
            Assert.InRange(Area(ring), mask.Area * 0.9f, mask.Area * 1.05f);
            for (int i = 0; i < ring.Count; i++)
            {
                float2 a = ring[i].xz, b = ring[(i + 1) % ring.Count].xz, c = ring[(i + 2) % ring.Count].xz;
                float turn = math.degrees(math.acos(math.clamp(math.dot(math.normalize(b - a), math.normalize(c - b)), -1f, 1f)));
                Assert.True(turn < 60f, $"turn of {turn:F0}° at {b}");
            }
            Assert.True(SurfaceAcceptance.IsAccepted(flat));
        }

        [Fact]
        public void Outline_KeepsTheLargestIslandAndFillsHoles()
        {
            var mask = new BrushMask();
            mask.Stamp(new float2(0f, 0f), 150f, false);
            mask.Stamp(new float2(0f, 0f), 40f, true);        // hole
            mask.Stamp(new float2(600f, 0f), 60f, false);     // separate small island
            List<float3> ring = mask.Outline();
            G.AssertSimpleCcw(Poly.To2D(ring));
            Assert.InRange(Area(ring), math.PI * 150f * 150f * 0.93f, math.PI * 150f * 150f * 1.03f);
            Assert.All(ring, p => Assert.True(p.x < 200f));
        }

        [Fact]
        public void Outlines_KeepIslandsAndHoles()
        {
            var mask = new BrushMask();
            mask.Stamp(new float2(0f, 0f), 150f, false);
            mask.Stamp(new float2(0f, 0f), 40f, true);
            mask.Stamp(new float2(80f, 80f), 12f, true);       // second, small hole
            mask.Stamp(new float2(600f, 0f), 60f, false);
            var regions = mask.Outlines();
            Assert.Equal(2, regions.Count);
            Assert.Equal(2, regions[0].Holes.Count);
            Assert.Empty(regions[1].Holes);
            foreach (var r in regions)
            {
                G.AssertSimpleCcw(r.Outer);
                foreach (var h in r.Holes) { Assert.True(Poly.IsSimple(h)); Assert.True(Poly.SignedArea(h) < 0); Assert.True(Poly.Contains(r.Outer, h[0])); }
            }
            float expected = math.PI * (150f * 150f - 40f * 40f - 12f * 12f + 60f * 60f);
            Assert.InRange(G.TotalArea(regions), expected * 0.9f, expected * 1.08f);
            // Options: islands without holes; tiny islands dropped; tiny holes filled.
            var noHoles = mask.Outlines(keepIslands: true, keepHoles: false);
            Assert.All(noHoles, r => Assert.Empty(r.Holes));
            var bigOnly = mask.Outlines(minIslandArea: 20000f);
            Assert.Single(bigOnly);
            var bigHolesOnly = mask.Outlines(minHoleArea: 2000f);
            Assert.Single(bigHolesOnly[0].Holes);
            // Islands and holes are accepted by CS2 once the holes are cut.
            foreach (var r in regions) Assert.NotEmpty(SurfaceAcceptance.MakeAcceptable2D(r, out _));
        }

        [Fact]
        public void Outline_HandlesCornerPinches()
        {
            var mask = new BrushMask();
            // Two squares touching at one corner, plus a bridge further away (pinch inside one blob).
            for (int i = 0; i < 20; i++)
                for (int j = 0; j < 20; j++)
                {
                    mask.Stamp(new float2(i * 4f + 2f, j * 4f + 2f), 1f, false);
                    mask.Stamp(new float2(80f + i * 4f + 2f, 80f + j * 4f + 2f), 1f, false);
                }
            Path(mask, 12f, false, 70, 20, 140, 20, 140, 90);
            List<float3> ring = mask.Outline();
            Assert.NotEmpty(ring);
            G.AssertSimpleCcw(Poly.To2D(ring));
            foreach (var r in mask.Outlines()) G.AssertSimpleCcw(r.Outer);
        }

        [Fact]
        public void Outlines_CheckerboardOfPinchesStaysSimple()
        {
            var mask = new BrushMask();
            for (int i = 0; i < 12; i++)
                for (int j = 0; j < 12; j++)
                    if ((i + j) % 2 == 0) mask.Stamp(new float2(i * 4f + 2f, j * 4f + 2f), 1f, false);
            var regions = mask.Outlines();
            Assert.NotEmpty(regions);
            foreach (var r in regions)
            {
                G.AssertSimpleCcw(r.Outer);
                foreach (var h in r.Holes) Assert.True(Poly.IsSimple(h));
            }
        }

        [Fact]
        public void RawBoundary_MatchesTheEdgeOfThePaintAfterErasing()
        {
            BrushMask mask = Sample();
            float cell = mask.CellSize;
            float expected = 0f;
            for (int i = -20; i < 200; i++)
                for (int j = -20; j < 200; j++)
                {
                    if (!mask.IsPainted(i, j)) continue;
                    foreach (int2 d in new[] { new int2(1, 0), new int2(-1, 0), new int2(0, 1), new int2(0, -1) })
                        if (!mask.IsPainted(i + d.x, j + d.y)) expected += cell;
                }
            float actual = mask.RawBoundary().Sum(s => math.distance(s.a, s.b));
            Assert.Equal(expected, actual, 1);
            Assert.All(mask.RawBoundary(), s => Assert.True(math.distance(s.a, s.b) <= 12 * cell + 0.01f));
        }

        [Fact]
        public void PaintingABigArea_StaysFast()
        {
            new BrushMask().Stroke(float2.zero, new float2(100f, 0f), 200f, false);
            new BrushMask().RawBoundary();
            double best = G.BestMs(() =>
            {
                var mask = new BrushMask();
                float2 last = new float2(0f, 0f);
                mask.Stamp(last, 200f, false);
                for (float x = 30f; x <= 1600f; x += 30f)
                {
                    float2 p = new float2(x, 300f * math.sin(x / 400f));
                    mask.Stroke(last, p, 200f, false);
                    last = p;
                    mask.RawBoundary();
                }
            }, 1, 2);
            double perFrame = best / 53;
            Assert.True(perFrame < 6.0, $"{perFrame:F2} ms per painting frame");
        }

        [Fact]
        public void Outline_BigArea_IsFastEnough()
        {
            var mask = new BrushMask();
            Path(mask, 200f, false, 0, 0, 800, 300, 1600, 0);
            double ms = G.BestMs(() => mask.Outlines(), 1, 3);
            Assert.True(ms < TimingBudget.Ms(250), $"{ms:F0} ms for {mask.PaintedCellCount} cells");
        }

        [Fact]
        public void FillRuns_CoverExactlyThePaintedCells()
        {
            BrushMask mask = Sample();
            float cell = mask.CellSize;
            var runs = mask.FillRuns();
            float covered = runs.Sum(r => math.distance(r.a, r.b)) * cell;
            Assert.Equal(mask.Area, covered, 1);
            Assert.All(runs, r => Assert.True(math.distance(r.a, r.b) <= 16 * cell + 0.01f));
            foreach (var r in runs)
            {
                float2 mid = (r.a + r.b) * 0.5f;
                Assert.True(mask.IsPaintedAt(mid));
            }
        }

        [Fact]
        public void SquareStamps_AndEraser()
        {
            var mask = new BrushMask();
            mask.Stamp(new float2(100f, 100f), 50f, false, square: true);
            Assert.InRange(mask.Area, 96f * 96f, 104f * 104f);
            Assert.True(mask.IsPaintedAt(new float2(146f, 146f)));
            Assert.False(mask.IsPaintedAt(new float2(156f, 100f)));
            G.AssertSimpleCcw(Poly.To2D(mask.Outline()));
            mask.Stamp(new float2(100f, 100f), 20f, true, square: true);
            Assert.InRange(mask.Area, 96f * 96f - 44f * 44f, 104f * 104f - 36f * 36f);
            var withHole = mask.Outlines();
            Assert.Single(withHole);
            Assert.Single(withHole[0].Holes);
        }

        [Fact]
        public void RotatedSquareStamp_PaintsARotatedSquare()
        {
            var mask = new BrushMask();
            mask.Stamp(new float2(0f, 0f), 50f, false, square: true, angleDegrees: 45f);
            Assert.InRange(mask.Area, 100f * 100f * 0.93f, 100f * 100f * 1.07f);
            Assert.True(mask.IsPaintedAt(new float2(66f, 1f)));
            Assert.False(mask.IsPaintedAt(new float2(46f, 46f)));
        }

        [Fact]
        public void PaintPolygon_FillsCellsInside()
        {
            var mask = new BrushMask(2f);
            mask.PaintPolygon(G.L(40, 20));
            Assert.InRange(mask.Area, 1200f * 0.95f, 1200f * 1.05f);
            mask.PaintPolygon(G.Square(10), erase: true);
            Assert.False(mask.IsPaintedAt(new float2(5, 5)));
        }

        [Fact]
        public void SaveRestore_RoundTrips()
        {
            var mask = Sample();
            var snap = mask.Save();
            float area = mask.Area;
            int v = mask.Version;
            mask.Stamp(new float2(1000, 1000), 50, false);
            Assert.NotEqual(area, mask.Area);
            mask.Restore(snap);
            Assert.Equal(area, mask.Area);
            Assert.True(mask.Version > v);
            Assert.Equal(mask.RawBoundary().Count, BrushMask.FromSnapshot(snap).RawBoundary().Count);
            mask.Clear();
            Assert.True(mask.IsEmpty);
            Assert.Empty(mask.Outlines());
        }

        [Fact]
        public void EmptyOrTinyPaint_GivesNoOutline()
        {
            Assert.Empty(new BrushMask().Outline());
            var mask = new BrushMask();
            mask.Stamp(new float2(10f, 10f), 5f, false);
            Assert.Empty(mask.Outline());
            mask.Stamp(new float2(float.NaN, 0), 5f, false); // ignored
            Assert.Equal(5 * 16f, mask.Area);
        }
    }
}
