using System.Collections.Generic;
using System.Linq;
using Citymaker.Core.Suite;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests.Suite
{
    public class SuiteLayoutTests
    {
        [Fact]
        public void Roads_ShareExactJunctionPoints()
        {
            var l = new SuiteLayout();
            var roads = l.Roads();
            Assert.Equal((l.NX + 1) * l.NZ + (l.NZ + 1) * l.NX, roads.Count);
            // every junction is hit by the same float2 from each segment that ends there (nodes merge on exact equality)
            var ends = new Dictionary<float2, int>();
            foreach (var r in roads) { ends[r.A] = ends.TryGetValue(r.A, out int a) ? a + 1 : 1; ends[r.B] = ends.TryGetValue(r.B, out int b) ? b + 1 : 1; }
            Assert.Equal((l.NX + 1) * (l.NZ + 1), ends.Count);
            Assert.True(ends[new float2(l.LineX(4), l.LineZ(3))] == 4);
        }

        [Fact]
        public void Blocks_HaveEveryKind_AndMixedStreets()
        {
            var l = new SuiteLayout();
            var blocks = l.Blocks();
            Assert.Equal(l.NX * l.NZ, blocks.Count);
            foreach (SuiteBlockKind k in System.Enum.GetValues(typeof(SuiteBlockKind)))
                if (k != SuiteBlockKind.Plot) Assert.Contains(blocks, b => b.Kind == k);   // plots are not grid blocks
            Assert.Contains(blocks, b => b.West == SuiteLayout.PedestrianStreet || b.East == SuiteLayout.PedestrianStreet);
            Assert.Contains(blocks, b => b.South == SuiteLayout.MediumRoad || b.West == SuiteLayout.MediumRoad);
        }

        [Fact]
        public void Heightmap_FlatSlopeAndRough()
        {
            var l = new SuiteLayout();
            Assert.Equal(l.Base, l.Height(l.LineX(5) + 40, l.LineZ(2) + 50), 3);
            // slope: 6 % north of LineZ(5), level roads at the start line
            Assert.Equal(SuiteLayout.BaseHeight + 0.06f * 70f, l.Height(100f, l.LineZ(5) + 70f), 3);
            Assert.Equal(SuiteLayout.BaseHeight, l.Height(100f, l.LineZ(5)), 3);
            // rough: bumps inside, level on the road lines
            Assert.Equal(SuiteLayout.BaseHeight, l.Height(l.LineX(1), l.LineZ(4) + 60f), 3);
            float max = 0f;
            for (float x = l.LineX(0); x < l.LineX(3); x += 3f)
                for (float z = l.LineZ(4); z < l.LineZ(5); z += 3f) max = math.max(max, math.abs(l.Height(x, z) - SuiteLayout.BaseHeight));
            Assert.InRange(max, 2.5f, SuiteLayout.RoughAmplitude + 0.01f);
        }

        [Fact]
        public void Heightmap_TexelsMatchHeightFunction()
        {
            var l = new SuiteLayout();
            var data = l.Heightmap();
            int n = SuiteLayout.HeightmapSize;
            foreach (var (px, py) in new[] { (2000, 2000), (1900, 2200), (1880, 2040), (2100, 2100) })
            {
                var w = l.TexelWorld(px, py);
                float h = data[py * n + px] / 65535f * SuiteLayout.HeightScale;
                Assert.InRange(math.abs(l.Height(w.x, w.y) - h), 0f, 0.07f);
            }
            Assert.Equal(l.Encode(SuiteLayout.BaseHeight), data[0]);
            Assert.True(SuiteLayout.BaseHeight > 511.7f + 25f);
        }

        [Fact]
        public void Heightmap_EasesIntoTheMapsTerrain()
        {
            // a map with its own scale/offset and a ramp along x; the fixture sits 20 m below the map's ground there
            var l = new SuiteLayout { HeightScaleM = 2048f, HeightOffset = 100f, Base = 400f };
            int n = SuiteLayout.HeightmapSize;
            var existing = new ushort[n * n];
            for (int py = 0; py < n; py++)
                for (int px = 0; px < n; px++)
                    existing[py * n + px] = l.Encode(420f + 0.002f * l.TexelWorld(px, py).x);
            var data = l.Heightmap(existing);
            float At(float x, float z)
            {
                int px = (int)((x - l.MapOffset.x) / (l.MapExtent.x / n)), py = (int)((z - l.MapOffset.y) / (l.MapExtent.y / n));
                return l.Decode(data[py * n + px]);
            }
            // inside: the layout (flat block, slope), to the encoding's resolution
            Assert.InRange(math.abs(At(l.LineX(5) + 40, l.LineZ(2) + 50) - l.Height(l.LineX(5) + 40, l.LineZ(2) + 50)), 0f, 0.1f);
            Assert.InRange(math.abs(At(100f, l.LineZ(5) + 70f) - l.Height(100f, l.LineZ(5) + 70f)), 0f, 0.3f);
            // far away: the map's own ground, untouched
            Assert.Equal(existing[10 * n + 10], data[10 * n + 10]);
            float farX = l.FixtureMax.x + l.Blend + 50f;
            Assert.InRange(math.abs(At(farX, l.LineZ(2)) - (420f + 0.002f * farX)), 0f, 0.1f);
            // the ease climbs steadily from the fixture to the map's ground
            float prev = At(l.FixtureMax.x + 1f, l.LineZ(2));
            for (float d = 20f; d < l.Blend; d += 20f)
            {
                float h = At(l.FixtureMax.x + d, l.LineZ(2));
                Assert.True(h >= prev - 0.05f, $"ease dips at {d} m: {h} after {prev}");
                prev = h;
            }
            Assert.Equal(400f, SuiteLayout.Median(new List<float> { 380f, 400f, 450f }));
        }

        [Fact]
        public void FrameStats_P95()
        {
            var ms = Enumerable.Range(1, 100).Select(i => (float)i).ToList();
            var (max, p95, n) = SuiteStats.FrameStats(ms);
            Assert.Equal(100f, max);
            Assert.Equal(95f, p95);
            Assert.Equal(100, n);
        }
    }
}
