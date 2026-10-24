// s55: the pit guard of the terraform target (Core/Build/TerraformPits) and the live hollow finder.

using System;
using System.Collections.Generic;
using System.Linq;
using Citymaker.Core.Build;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    public class TerraformPitTests
    {
        private static float2[] Sq(float x0, float y0, float x1, float y1) => new[] { new float2(x0, y0), new float2(x1, y0), new float2(x1, y1), new float2(x0, y1) };

        private static TerraformRaster Raster(float pitSize, float pitDepth)
        {
            // 60 x 60 m lot at +0.0 with a square pit in the middle (later items win).
            var items = new List<(float2[] ring, float target, int layer)> { (Sq(0, 0, 60, 60), 0f, 0) };
            if (pitSize > 0) items.Add((Sq(30 - pitSize / 2, 30 - pitSize / 2, 30 + pitSize / 2, 30 + pitSize / 2), -pitDepth, 0));
            return TerraformRaster.Build(items, 1.75f, 4f, 1 << 22, null, _ => 0f);
        }

        [Fact]
        public void ABowlInALotIsFilledToItsRim()
        {
            var r = Raster(8f, 1.8f);
            Func<float2, float> g = _ => 0f;
            Assert.True(r.TrySample(new float2(30, 30), g, out float before) && before < -1.5f);
            int n = TerraformPits.Fill(r, g, 0.35f, out float deepest, out var at);
            Assert.True(n > 0);
            Assert.True(deepest > 1.5f);
            Assert.True(math.distance(at, new float2(30, 30)) < 6f);
            Assert.True(r.TrySample(new float2(30, 30), g, out float after) && after > -0.3f, $"pit centre {after:0.00} m");
            Assert.Empty(TerraformPits.Worst(r, p => r.TrySample(p, g, out float h) ? h : g(p), 0.35f));
        }

        [Fact]
        public void FlatGroundASlopeAndAWideBasinAreLeftAlone()
        {
            Func<float2, float> g = _ => 0f;
            var flat = Raster(0f, 0f);
            Assert.Equal(0, TerraformPits.Fill(flat, g, 0.35f, out _, out _));

            // a 6 % slope: a tilted plane has no pit
            var tilted = TerraformRaster.Build(new List<(float2[] ring, float target, int layer)> { (Sq(0, 0, 60, 60), 0f, 0) }, 1.75f, 4f, 1 << 22, null, _ => 0f);
            Func<float2, float> slope = p => 0.06f * p.y;
            for (int i = 0; i < tilted.Count; i++) tilted.Target[i] = 0.06f * tilted.Center(i % tilted.Width, i / tilted.Width).y;
            Assert.Equal(0, TerraformPits.Fill(tilted, slope, 0.35f, out _, out _));

            // a 40 m wide, 1 m deep basin is a landform, not a pit
            var basin = Raster(40f, 1f);
            Assert.True(basin.TrySample(new float2(30, 30), g, out float c) && c < -0.9f);
            TerraformPits.Fill(basin, g, 0.35f, out _, out _);
            Assert.True(basin.TrySample(new float2(30, 30), g, out float c2) && c2 < -0.9f, $"basin centre {c2:0.00} m");
        }

        [Fact]
        public void TheLiveFinderReportsTheBowlOnce()
        {
            var r = Raster(0f, 0f);
            Func<float2, float> live = p => -1.6f * math.exp(-math.lengthsq(p - new float2(20, 40)) / (2f * 3f * 3f));
            var pits = TerraformPits.Worst(r, live, 0.4f);
            Assert.Single(pits);
            Assert.True(math.distance(pits[0].At, new float2(20, 40)) < 3f);
            Assert.True(pits[0].Depth > 1.3f);
        }
    }
}
