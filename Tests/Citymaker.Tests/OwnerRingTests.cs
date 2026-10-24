using System.Collections.Generic;
using Citymaker.Core.Build;
using Citymaker.Core.Geometry;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    public class OwnerRingTests
    {
        private static float2[] Jagged()
        {
            // A 100 × 60 outline with 1 m teeth along one side and a 0.5 m spike (fails lot rules as is).
            var r = new List<float2> { new float2(0, 0), new float2(100, 0), new float2(100, 60) };
            for (float x = 99f; x > 1f; x -= 1f) r.Add(new float2(x, (x % 2f) < 1f ? 60f : 60.8f));
            r.Add(new float2(0.5f, 60f));
            r.Add(new float2(0, 60));
            return r.ToArray();
        }

        [Fact]
        public void EveryLevelYieldsAValidLotRing()
        {
            var outline = Jagged();
            for (int level = 0; level < OwnerRings.Levels; level++)
            {
                var ring = OwnerRings.Candidate(outline, level);
                Assert.NotNull(ring);
                Assert.True(AreaRings.IsValid(ring, AreaRings.LotMinNodeDistance), $"level {level}");
                Assert.True(Poly.IsSimple(ring));
                Assert.True(Poly.Area(ring) > 0.8f * 100f * 60f, $"level {level} keeps most of the area");
            }
            Assert.Null(OwnerRings.Candidate(outline, OwnerRings.Levels));
        }

        [Fact]
        public void BoundingRectIsMinimalForARotatedRectangle()
        {
            var u = math.normalize(new float2(3, 1));
            var v = new float2(-u.y, u.x);
            var hull = new[] { 0 * u, 40 * u, 40 * u + 20 * v, 20 * v };
            var rect = OwnerRings.BoundingRect(hull, 0f);
            Assert.Equal(800f, Poly.Area(rect), 0);
        }

        [Fact]
        public void TinyOrBrokenOutlinesGiveNull()
        {
            Assert.Null(OwnerRings.Candidate(new[] { new float2(0, 0), new float2(1, 0), new float2(0, 1) }, 0));
            Assert.Null(OwnerRings.Candidate(null, 0));
        }
    }
}
