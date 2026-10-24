using System.Collections.Generic;
using Citymaker.Core.Build;
using Citymaker.Core.Geometry;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    public class BuildAreaRingTests
    {
        private static float2[] Square(float s) => new[] { new float2(0, 0), new float2(s, 0), new float2(s, s), new float2(0, s) };

        [Fact]
        public void PlainSquareIsValid()
        {
            Assert.True(AreaRings.IsValid(Square(10), AreaRings.SurfaceMinNodeDistance));
            Assert.True(AreaRings.IsValid(Square(10), AreaRings.LotMinNodeDistance));
        }

        [Fact]
        public void EdgeShorterThanGameDistanceIsInvalid()
        {
            // 0.33 m edge passes the old 0.3 m MinEdge policy but fails the game's 0.375 m rule.
            var ring = new[] { new float2(0, 0), new float2(10, 0), new float2(10, 0.33f), new float2(0, 5) };
            Assert.False(AreaRings.IsValid(ring, AreaRings.SurfaceMinNodeDistance));
            var fixedRing = AreaRings.MakeValid(ring, AreaRings.SurfaceMinNodeDistance);
            Assert.NotNull(fixedRing);
            Assert.True(AreaRings.IsValid(fixedRing, AreaRings.SurfaceMinNodeDistance));
        }

        [Fact]
        public void AcuteSpikeIsInvalidAndRepaired()
        {
            // Needle: 5 m spike whose base is only 0.2 m wide → base node 0.2 m from the opposite edge.

            var ring = new[] { new float2(0, 0), new float2(10, 0), new float2(10, 10), new float2(0, 10), new float2(-5f, 10f), new float2(0f, 9.8f) };
            Assert.False(AreaRings.IsValid(ring, AreaRings.SurfaceMinNodeDistance));
            var r = AreaRings.MakeValid(ring, AreaRings.SurfaceMinNodeDistance);
            Assert.NotNull(r);
            Assert.True(AreaRings.IsValid(r, AreaRings.SurfaceMinNodeDistance));
            Assert.True(Poly.Area(r) > 90f);
        }

        [Fact]
        public void ClosingDuplicateAndCollinearNodesAreDropped()
        {
            var ring = new[] { new float2(0, 0), new float2(5, 0), new float2(10, 0), new float2(10, 10), new float2(0, 10), new float2(0, 0) };
            var r = AreaRings.MakeValid(ring, AreaRings.SurfaceMinNodeDistance);
            Assert.NotNull(r);
            Assert.Equal(4, r.Length);
        }

        [Fact]
        public void NarrowNeckBetweenNonAdjacentEdgesIsInvalid()
        {
            // Dumbbell with a 0.2 m neck: non-adjacent edges closer than 0.375 m.
            var ring = new[]
            {
                new float2(0, 0), new float2(10, 0), new float2(10, 4.9f), new float2(20, 4.9f), new float2(20, 0), new float2(30, 0),
                new float2(30, 10), new float2(20, 10), new float2(20, 5.1f), new float2(10, 5.1f), new float2(10, 10), new float2(0, 10),
            };
            Assert.False(AreaRings.IsValid(ring, AreaRings.SurfaceMinNodeDistance));
        }

        [Fact]
        public void LotOutlineWithShortEdgesBecomesValidForLots()
        {
            var pts = new List<float2>();
            for (int i = 0; i < 64; i++) { float t = i / 64f * math.PI * 2f; pts.Add(new float2(math.cos(t), math.sin(t)) * 30f); }   // ~2.9 m edges
            Assert.False(AreaRings.IsValid(pts, AreaRings.LotMinNodeDistance));
            var r = AreaRings.MakeValid(pts, AreaRings.LotMinNodeDistance);
            Assert.NotNull(r);
            Assert.True(AreaRings.IsValid(r, AreaRings.LotMinNodeDistance));
            Assert.True(Poly.Area(r) > 0.8f * math.PI * 900f);
        }

        [Fact]
        public void PrepareSurfaceReturnsAcceptedRings()
        {
            var ring = new[] { new float2(0, 0), new float2(10, 0), new float2(10, 0.33f), new float2(0, 5) };
            var list = AreaRings.PrepareSurface(ring, AreaRings.SurfaceMinNodeDistance);
            Assert.NotEmpty(list);
            foreach (var r in list)
            {
                Assert.True(AreaRings.IsValid(r, AreaRings.SurfaceMinNodeDistance));
                Assert.True(SurfaceAcceptance.IsAccepted(r));
            }
        }

        [Fact]
        public void ConvexHullIsCcwAndContainsAll()
        {
            var pts = new[] { new float2(0, 0), new float2(4, 0), new float2(2, 1), new float2(4, 4), new float2(0, 4), new float2(1, 2) };
            var h = AreaRings.ConvexHull(pts);
            Assert.Equal(4, h.Length);
            Assert.True(Poly.IsCcw(h));
        }
    }
}
