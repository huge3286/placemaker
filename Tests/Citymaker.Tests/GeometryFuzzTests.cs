// Randomised invariants for the planar arrangement (booleans, normalisation, offset, split) (owner: geometry).

using System.Collections.Generic;
using System.Linq;
using Citymaker.Core.Geometry;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    public class GeometryFuzzTests
    {
        private static float2[] RandomStarShaped(G.Rng rng, int n, float rMin, float rMax, float2 c)
        {
            var p = new float2[n];
            for (int i = 0; i < n; i++)
            {
                float a = 2 * math.PI * i / n, r = rng.Range(rMin, rMax);
                p[i] = c + r * new float2(math.cos(a), math.sin(a));
            }
            return p;
        }

        private static void AssertRegionsValid(List<Region> regions, string what)
        {
            foreach (var r in regions)
            {
                Assert.True(Poly.IsSimple(r.Outer), $"{what}: outer not simple {G.Dump(r.Outer)}");
                Assert.True(Poly.SignedArea(r.Outer) > 0, $"{what}: outer not CCW");
                foreach (var h in r.Holes)
                {
                    Assert.True(Poly.IsSimple(h), $"{what}: hole not simple");
                    Assert.True(Poly.SignedArea(h) < 0, $"{what}: hole not CW");
                }
            }
        }

        [Fact]
        public void Booleans_SatisfyAreaIdentities()
        {
            var rng = new G.Rng(11);
            for (int k = 0; k < 80; k++)
            {
                float2 world = k % 2 == 0 ? float2.zero : new float2(-4800, 3900);
                var a = RandomStarShaped(rng, 5 + (int)(rng.Next() * 25), 10, 60, world);
                var b = RandomStarShaped(rng, 5 + (int)(rng.Next() * 25), 10, 60, world + new float2(rng.Range(-40, 40), rng.Range(-40, 40)));
                var u = Poly.Union(a, b);
                var i = Poly.Intersection(a, b);
                var d = Poly.Difference(a, b);
                var x = Poly.Xor(a, b);
                AssertRegionsValid(u, "union"); AssertRegionsValid(i, "intersection");
                AssertRegionsValid(d, "difference"); AssertRegionsValid(x, "xor");
                float A = Poly.Area(a), B = Poly.Area(b), U = G.TotalArea(u), I = G.TotalArea(i), D = G.TotalArea(d), X = G.TotalArea(x);
                float tol = 0.01f + 2e-5f * (A + B);
                Assert.True(math.abs(U - (A + B - I)) < tol, $"case {k}: U={U} A+B-I={A + B - I}");
                Assert.True(math.abs(D - (A - I)) < tol, $"case {k}: D={D} A-I={A - I}");
                Assert.True(math.abs(X - (U - I)) < tol, $"case {k}: X={X} U-I={U - I}");
            }
        }

        [Fact]
        public void Normalize_RandomSelfIntersectingRings_GivesSimpleRegions()
        {
            var rng = new G.Rng(5);
            for (int k = 0; k < 80; k++)
            {
                int n = 4 + (int)(rng.Next() * 20);
                var pts = new float2[n];
                for (int i = 0; i < n; i++) pts[i] = new float2(rng.Range(0, 100), rng.Range(0, 100));
                var nz = Poly.Normalize(pts, FillRule.NonZero);
                var eo = Poly.Normalize(pts, FillRule.EvenOdd);
                AssertRegionsValid(nz, $"nonzero {k}");
                AssertRegionsValid(eo, $"evenodd {k}");
                Assert.True(G.TotalArea(nz) + 1e-2f >= G.TotalArea(eo));
                var c = Poly.Clean(pts);
                if (c.Length > 0) G.AssertSimpleCcw(c, $"clean {k}");
            }
        }

        [Fact]
        public void Booleans_GridAlignedDegenerateOverlaps()
        {
            // Many coincident edges and vertices: unions of overlapping axis-aligned rectangles.
            var rng = new G.Rng(9);
            for (int k = 0; k < 40; k++)
            {
                var rects = new List<IReadOnlyList<float2>>();
                for (int r = 0; r < 6; r++)
                {
                    int x0 = (int)(rng.Next() * 10), y0 = (int)(rng.Next() * 10);
                    int w = 1 + (int)(rng.Next() * 6), h = 1 + (int)(rng.Next() * 6);
                    rects.Add(G.Rect(x0, y0, x0 + w, y0 + h));
                }
                var u = Poly.UnionAll(rects);
                AssertRegionsValid(u, $"grid union {k}");
                // Area by cell counting.
                int cells = 0;
                for (int x = 0; x < 20; x++) for (int y = 0; y < 20; y++)
                    if (rects.Any(r => Poly.Contains(r, new float2(x + 0.5f, y + 0.5f)))) cells++;
                Assert.Equal(cells, G.TotalArea(u), 2);
            }
        }

        [Fact]
        public void Offset_ThenAcceptance_OnRandomShapes()
        {
            var rng = new G.Rng(21);
            for (int k = 0; k < 30; k++)
            {
                var a = RandomStarShaped(rng, 6 + (int)(rng.Next() * 40), 5, 80, new float2(-2500, 1200));
                foreach (float d in new[] { 0.5f, 3f, -2f })
                    foreach (var r in Poly.Offset(a, d))
                    {
                        G.AssertSimpleCcw(r, $"offset {k} {d}");
                        foreach (var piece in SurfaceAcceptance.MakeAcceptable2D(r))
                            Assert.True(SurfaceAcceptance.IsAccepted(piece));
                    }
            }
        }

        [Fact]
        public void Split_RandomLines_PreserveArea()
        {
            var rng = new G.Rng(33);
            for (int k = 0; k < 40; k++)
            {
                var a = RandomStarShaped(rng, 6 + (int)(rng.Next() * 30), 10, 60, float2.zero);
                var line = new[] { new float2(rng.Range(-80, 80), -90), new float2(rng.Range(-30, 30), rng.Range(-30, 30)), new float2(rng.Range(-80, 80), 90) };
                var parts = Poly.Split(a, line);
                Assert.NotEmpty(parts);
                foreach (var p in parts) G.AssertSimpleCcw(p, $"split {k}");
                Assert.Equal(Poly.Area(Poly.Clean(a, 1e-3f)), G.TotalArea(parts), 0);
            }
        }
    }
}
