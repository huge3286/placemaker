using System;
using System.Collections.Generic;
using Citymaker.Core.Build;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    public class BuildTerraformRasterTests
    {
        private static float2[] Square(float x0, float y0, float x1, float y1) =>
            new[] { new float2(x0, y0), new float2(x1, y0), new float2(x1, y1), new float2(x0, y1) };

        private static int Index(TerraformRaster r, float2 p)
        {
            int x = (int)math.floor((p.x - r.Origin.x) / r.Cell), y = (int)math.floor((p.y - r.Origin.y) / r.Cell);
            return y * r.Width + x;
        }

        [Fact]
        public void InsideIsTargetOutsideBankBlendsBackToGround()
        {
            var items = new List<(float2[], float, int)> { (Square(0, 0, 40, 40), 10f, 0) };
            var r = TerraformRaster.Build(items, 1f, 4f);
            Assert.NotNull(r);
            int c = Index(r, new float2(20, 20));
            Assert.Equal(10f, r.Desired(c, 3f), 3);
            // 2 m outside: partly blended; 4.5 m outside: untouched.
            int near = Index(r, new float2(42, 20)), far = Index(r, new float2(44.5f, 20));
            float h = r.Desired(near, 0f);
            Assert.True(h > 0.5f && h < 10f, $"bank height {h}");
            Assert.Equal(0f, r.Desired(far, 0f), 3);
        }

        [Fact]
        public void LaterLayerWinsWhereItOverlaps()
        {
            var items = new List<(float2[], float, int)>
            {
                (Square(10, 10, 20, 20), 7f, 1),      // child, listed first but a later layer
                (Square(0, 0, 40, 40), 2f, 0),
            };
            var r = TerraformRaster.Build(items, 1f, 0f);
            Assert.Equal(7f, r.Desired(Index(r, new float2(15, 15)), 0f), 3);
            Assert.Equal(2f, r.Desired(Index(r, new float2(30, 30)), 0f), 3);
        }

        [Fact]
        public void DeltasAndMasksReconstructTheChange()
        {
            var items = new List<(float2[], float, int)> { (Square(0, 0, 20, 20), 5f, 0) };
            var r = TerraformRaster.Build(items, 1f, 3f);
            Func<float2, float> ground = p => 0.1f * p.x;   // sloped: some texels raise, none lower here
            var d = r.Deltas(ground, 0.3f, out var stats);
            Assert.True(stats.Inside > 0 && stats.InsideOff > 0);
            Assert.False(stats.Done(0.3f, 0.02f));
            foreach (var tile in r.Tiles(1024))
            {
                float scale = TerraformRaster.Masks(d, r.Width, r.Height, tile, false, false, 0.01f, out var raise, out var lower, out bool anyRaise, out _);
                Assert.True(anyRaise);
                for (int y = 0; y < r.Height; y++)
                    for (int x = 0; x < r.Width; x++)
                    {
                        int k = (y - tile.Y) * tile.Size + (x - tile.X);
                        float rebuilt = (raise[k] - lower[k]) * scale;
                        Assert.Equal(d[y * r.Width + x], rebuilt, 3);
                    }
            }
            // After applying the change exactly, the ground is on target.
            Func<float2, float> after = p =>
            {
                int i = Index(r, p);
                return i >= 0 && i < r.Count ? r.Desired(i, ground(p)) : ground(p);
            };
            r.Deltas(after, 0.3f, out var done);
            Assert.True(done.Done(0.3f, 0.02f));
        }

        [Fact]
        public void MasksMirrorForOrientation()
        {
            var items = new List<(float2[], float, int)> { (Square(0, 0, 4, 4), 1f, 0) };
            var r = TerraformRaster.Build(items, 1f, 0f);
            var d = new float[r.Count];
            d[0] = 1f;   // only the lower-left texel changes
            var tile = r.Tiles(1024)[0];
            TerraformRaster.Masks(d, r.Width, r.Height, tile, true, true, 0f, out var raise, out _, out _, out _);
            int n = tile.Size;
            Assert.Equal(1f, raise[(n - 1) * n + (n - 1)]);
            Assert.Equal(0f, raise[0]);
        }

        [Fact]
        public void LargeRastersTileIntoSquares()
        {
            var items = new List<(float2[], float, int)> { (Square(0, 0, 300, 100), 1f, 0) };
            var r = TerraformRaster.Build(items, 1f, 0f);
            var tiles = r.Tiles(128);
            Assert.True(tiles.Count >= 3);
            var covered = new bool[r.Count];
            foreach (var t in tiles)
                for (int y = t.Y; y < math.min(r.Height, t.Y + t.Size); y++)
                    for (int x = t.X; x < math.min(r.Width, t.X + t.Size); x++) covered[y * r.Width + x] = true;
            Assert.All(covered, Assert.True);
            r.TileWorld(tiles[0], out var center, out float size);
            Assert.Equal(128f, size, 3);
            Assert.Equal(r.Origin.x + 64f, center.x, 3);
        }

        [Fact]
        public void ShiftStrengthMatchesDecompiledFormula()
        {
            // change = clamp(dt, .05, 1) · strength · 2000 · mask (metres at mask 1)
            float s = TerraformRaster.ShiftStrength(10f, 0.016f, 2000f, out float scale);
            Assert.Equal(1f, scale);
            Assert.Equal(10f, 0.05f * s * 2000f, 3);
            // Tiny changes keep the strength above the game's 0.002 bend and scale the mask instead.
            s = TerraformRaster.ShiftStrength(0.05f, 0.016f, 2000f, out scale);
            Assert.True(s >= 0.002f && scale < 1f);
            Assert.Equal(0.05f, 0.05f * s * 2000f * scale, 4);
        }

        [Fact]
        public void TrySampleReturnsTerraformGroundOnlyWhereTouched()
        {
            var items = new List<(float2[], float, int)> { (Square(0, 0, 20, 20), 6f, 0) };
            var r = TerraformRaster.Build(items, 1f, 2f);
            Assert.True(r.TrySample(new float2(10, 10), _ => 0f, out float h));
            Assert.Equal(6f, h, 2);
            Assert.False(r.TrySample(new float2(40, 40), _ => 0f, out _));
        }

        [Fact]
        public void CoverageIsSoftAtTheEdgeAndFullInside()
        {
            var ring = Square(0, 0, 20, 20);
            var cov = TerraformRaster.Coverage(ring, new float2(-5, -5), 1f, 30, 30, 3f);
            Assert.Equal(1f, cov[15 * 30 + 15], 3);                 // (10.5, 10.5)
            Assert.Equal(0f, cov[15 * 30 + 0], 3);                  // (-4.5, 10.5)
            float edge = cov[15 * 30 + 5];                          // (0.5, 10.5): just inside the edge
            Assert.True(edge > 0.4f && edge < 1f, $"edge {edge}");
        }
    }
}
