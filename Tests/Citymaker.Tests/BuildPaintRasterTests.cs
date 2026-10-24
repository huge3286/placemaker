// Terrain texture paint fields and stamps (Core/Build/PaintRaster, s13): coverage, feather and gradient math,
// noise containment, determinism, layer order and the stamp budget.

using System.Collections.Generic;
using System.Linq;
using Citymaker.Core;
using Citymaker.Core.Build;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    public class BuildPaintRasterTests
    {
        private static float3[] Rect(float x0, float y0, float x1, float y1) =>
            new[] { new float3(x0, 0, y0), new float3(x1, 0, y0), new float3(x1, 0, y1), new float3(x0, 0, y1) };

        private static PlannedPaint Paint(float3[] ring, int layer = TerrainMaterials.Sand, float strength = 1f, float feather = 4f, float noise = 0f) =>
            new PlannedPaint { Ring = ring, Layer = layer, Strength = strength, Feather = feather, Noise = noise, Seed = 7 };

        private static readonly PaintRasterOptions Fine = new PaintRasterOptions { Cell = 1f, Align = 2f };

        [Fact]
        public void FeatherRampsFromZeroAtTheRingToFullInside()
        {
            var p = Paint(Rect(0, 0, 60, 40), strength: 0.8f, feather: 6f);
            Assert.Equal(0f, PaintField.StrengthAt(p, new float2(-0.5f, 20)), 3);   // outside: never painted
            Assert.Equal(0.8f, PaintField.StrengthAt(p, new float2(30, 20)), 3);    // deep inside: the paint's strength
            float prev = -1f;
            for (float x = 0.1f; x <= 7f; x += 0.5f)
            {
                float v = PaintField.StrengthAt(p, new float2(x, 20));
                Assert.True(v >= prev - 1e-5f, $"ramp not monotonic at {x}");
                prev = v;
            }
            Assert.InRange(PaintField.StrengthAt(p, new float2(3f, 20)), 0.3f, 0.5f);   // half way = half (smoothstep)
            Assert.Equal(0.8f, PaintField.StrengthAt(p, new float2(6.5f, 20)), 3);
        }

        [Fact]
        public void LegacyEdgeIsCentredOnTheRing()
        {
            var p = new PlannedPaint { Ring = Rect(0, 0, 20, 20), Layer = 1, Strength = 1f };
            Assert.Equal(0.5f, PaintField.StrengthAt(p, new float2(0, 10)), 2);
            Assert.True(PaintField.StrengthAt(p, new float2(-1f, 10)) > 0f);
            Assert.Equal(0f, PaintField.StrengthAt(p, new float2(-1.6f, 10)), 3);
        }

        [Fact]
        public void GradientIsFullNearTheLineAndFadesOverTheBlendWidth()
        {
            var p = Paint(Rect(0, -5, 100, 60), feather: 1f);
            p.GradientFrom = new[] { new float2(-10, 0), new float2(110, 0) };
            p.GradientFull = 20f; p.GradientFade = 15f; p.GradientMin = 0.1f;
            Assert.Equal(1f, PaintField.StrengthAt(p, new float2(50, 2)), 3);     // at the water: full
            Assert.Equal(1f, PaintField.StrengthAt(p, new float2(50, 19.9f)), 3);
            float mid = PaintField.StrengthAt(p, new float2(50, 27.5f));
            Assert.InRange(mid, 0.5f, 0.6f);                                          // half way: lerp(1, 0.1, 0.5)
            Assert.Equal(0.1f, PaintField.StrengthAt(p, new float2(50, 40)), 3);    // beyond the blend: the minimum
            float prev = 2f;
            for (float y = 18f; y < 38f; y += 1f)
            {
                float v = PaintField.StrengthAt(p, new float2(50, y));
                Assert.True(v <= prev + 1e-5f, $"gradient rises at {y}");
                prev = v;
            }
        }

        [Fact]
        public void RasterMatchesTheReferenceFieldAndCoversThePolygon()
        {
            var ring = new[] { new float3(3, 0, 2), new float3(70, 0, 8), new float3(64, 0, 55), new float3(20, 0, 48), new float3(30, 0, 25) };   // concave
            var p = Paint(ring, feather: 5f, noise: 1.5f);
            p.GradientFrom = new[] { new float2(0, 0), new float2(80, 10) };
            p.GradientFull = 15f; p.GradientFade = 20f;
            var stamps = PaintRaster.Build(new[] { p }, Fine);
            Assert.NotEmpty(stamps);
            foreach (var s in stamps)
                for (int y = 0; y < s.N; y += 3)
                    for (int x = 0; x < s.N; x += 3)
                    {
                        var c = s.Min + (new float2(x, y) + 0.5f) * s.Cell;
                        Assert.Equal(PaintField.StrengthAt(p, c), s.Mask[y * s.N + x], 2);
                    }
            // Every point the field paints is inside some stamp.
            for (float y = 0; y < 60; y += 2.3f)
                for (float x = 0; x < 75; x += 2.3f)
                {
                    var q = new float2(x, y);
                    if (PaintField.StrengthAt(p, q) < 0.05f) continue;
                    Assert.True(PaintRaster.Sample(stamps, p.Layer, q) > 0f, $"{q} not covered");
                }
        }

        [Fact]
        public void NoiseNeverPaintsOutsideTheRing()
        {
            var ring = Rect(0, 0, 40, 30);
            var p = Paint(ring, feather: 3f, noise: 3f);
            var stamps = PaintRaster.Build(new[] { p }, Fine);
            foreach (var s in stamps)
                for (int y = 0; y < s.N; y++)
                    for (int x = 0; x < s.N; x++)
                    {
                        if (s.Mask[y * s.N + x] <= 0f) continue;
                        var c = s.Min + (new float2(x, y) + 0.5f) * s.Cell;
                        Assert.InRange(c.x, 0f, 40f);
                        Assert.InRange(c.y, 0f, 30f);
                    }
            // ... but the noise does vary the edge: the strengths along a line 5 m inside differ.
            var along = Enumerable.Range(0, 40).Select(i => PaintField.StrengthAt(p, new float2(i + 0.5f, 5f))).ToList();
            Assert.True(along.Max() - along.Min() > 0.15f, "edge noise has no effect (5 m inside)");
        }

        [Fact]
        public void StampsAreDeterministicAlignedAndNonOverlapping()
        {
            var paints = new List<PlannedPaint> { Paint(Rect(5, 5, 300, 90), noise: 2f), Paint(Rect(100, 60, 160, 200), layer: TerrainMaterials.Dirt) };
            var o = new PaintRasterOptions { Cell = 1.75f, Align = 3.5f, TileCells = 64 };
            var a = PaintRaster.Build(paints, o);
            var b = PaintRaster.Build(paints, o);
            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.Equal(a[i].Layer, b[i].Layer);
                Assert.Equal(a[i].Min, b[i].Min);
                Assert.Equal(a[i].Mask, b[i].Mask);
            }
            foreach (var s in a)
            {
                Assert.Equal(0f, math.abs(math.fmod(s.Min.x, 3.5f)), 3);
                Assert.Equal(0f, math.abs(math.fmod(s.Min.y, 3.5f)), 3);
                Assert.True(s.Peak > 0f && s.Painted > 0);
            }
            foreach (var g in a.GroupBy(s => s.Layer))
            {
                var list = g.ToList();
                for (int i = 0; i < list.Count; i++)
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        var p = list[i]; var q = list[j];
                        bool overlap = p.Min.x < q.Min.x + q.Size - 0.01f && q.Min.x < p.Min.x + p.Size - 0.01f
                            && p.Min.y < q.Min.y + q.Size - 0.01f && q.Min.y < p.Min.y + p.Size - 0.01f;
                        Assert.False(overlap, "stamps of one layer overlap (would dither twice)");
                    }
            }
        }

        [Fact]
        public void SameLayerMergesByMaxAndLayersKeepPlanOrderWithEraseFirst()
        {
            var weak = Paint(Rect(0, 0, 40, 40), strength: 0.3f, feather: 1f);
            var strong = Paint(Rect(20, 0, 60, 40), strength: 0.9f, feather: 1f);
            var dirt = Paint(Rect(0, 50, 20, 70), layer: TerrainMaterials.Dirt, feather: 1f);
            var erase = Paint(Rect(0, 80, 20, 100), layer: TerrainMaterials.Erase, feather: 1f);
            var stamps = PaintRaster.Build(new[] { weak, dirt, strong, erase }, Fine);
            Assert.Equal(new[] { TerrainMaterials.Erase, TerrainMaterials.Sand, TerrainMaterials.Dirt }, stamps.Select(s => s.Layer).Distinct().ToArray());
            Assert.Equal(0.3f, PaintRaster.Sample(stamps, TerrainMaterials.Sand, new float2(10.5f, 20.5f)), 2);
            Assert.Equal(0.9f, PaintRaster.Sample(stamps, TerrainMaterials.Sand, new float2(30.5f, 20.5f)), 2);   // overlap: max, not sum
        }

        [Fact]
        public void HugeAreasStayWithinTheStampBudget()
        {
            var big = Paint(Rect(0, 0, 9000, 6000), feather: 10f);
            var o = new PaintRasterOptions { Cell = 1.75f, Align = 3.5f, TileCells = 512, MaxStamps = 24 };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var stamps = PaintRaster.Build(new[] { big }, o);
            Assert.InRange(stamps.Count, 1, 24);
            Assert.True(stamps[0].Cell > 1.75f, "the cell should have grown");
            Assert.True(sw.ElapsedMilliseconds < TimingBudget.Ms(4000), sw.ElapsedMilliseconds + " ms");
            // A typical beach (300 × 80 m) is one stamp at full resolution.
            var beach = PaintRaster.Build(new[] { Paint(Rect(0, 0, 300, 80)) }, o);
            Assert.Single(beach);
            Assert.Equal(1.75f, beach[0].Cell, 3);
        }

        [Fact]
        public void PreviewRectsShowLevelsPerLayer()
        {
            var p = Paint(Rect(0, 0, 80, 40), feather: 1f);
            p.GradientFrom = new[] { new float2(0, 0), new float2(80, 0) };
            p.GradientFull = 10f; p.GradientFade = 25f;
            var rects = PaintRaster.PreviewRects(PaintRaster.Build(new[] { p }, Fine), 4, 4f, 500);
            Assert.NotEmpty(rects);
            Assert.All(rects, r => Assert.InRange(r.level, 1, 4));
            Assert.Contains(rects, r => r.level == 4);
            Assert.Contains(rects, r => r.level <= 2);
            Assert.True(rects.Count <= 500);
        }

        [Fact]
        public void InvalidPaintsAreSkipped()
        {
            var stamps = PaintRaster.Build(new[] { null, new PlannedPaint(), Paint(Rect(0, 0, 10, 10), strength: 0f),
                new PlannedPaint { Ring = new[] { new float3(float.NaN, 0, 0), new float3(1, 0, 0), new float3(1, 0, 1) } } }, Fine);
            Assert.Empty(stamps);
            Assert.Empty(PaintRaster.Build(null));
        }
    }
}
