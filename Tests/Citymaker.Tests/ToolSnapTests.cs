using System.Collections.Generic;
using Citymaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    public class ToolSnapTests
    {
        [Fact]
        public void NoCandidatesKeepsRawPoint()
        {
            var s = new SnapResolver();
            s.Begin(new float3(1, 2, 3));
            var r = s.Result();
            Assert.Equal(SnapKind.None, r.Kind);
            Assert.Equal(new float3(1, 2, 3), r.Position);
        }

        [Fact]
        public void HigherLevelWinsOverCloserLowerLevel()
        {
            var s = new SnapResolver();
            s.Begin(new float3(0, 0, 0));
            s.OfferLine(SnapResolver.LevelZoneGrid, SnapKind.ZoneGrid, new float3(0, 0, 0.5f), new float2(1, 0), true);
            s.OfferLine(SnapResolver.LevelArea, SnapKind.AreaEdge, new float3(0, 0, 5f), new float2(1, 0));
            Assert.Equal(SnapKind.AreaEdge, s.Result().Kind);
            Assert.Equal(5f, s.Result().Position.z, 3);
        }

        [Fact]
        public void CrossingOfTwoLinesBeatsEitherLine()
        {
            var s = new SnapResolver();
            s.Begin(new float3(2, 0, 3));
            s.OfferLine(SnapResolver.LevelNet, SnapKind.RoadEdge, new float3(0, 0, 0), new float2(1, 0));   // z = 0
            s.OfferLine(SnapResolver.LevelNet, SnapKind.RoadEdge, new float3(0, 0, 0), new float2(0, 1));   // x = 0
            var r = s.Result();
            Assert.Equal(SnapKind.Crossing, r.Kind);
            Assert.Equal(0f, r.Position.x, 3);
            Assert.Equal(0f, r.Position.z, 3);
        }

        [Fact]
        public void FarAwayLinesAreIgnored()
        {
            var s = new SnapResolver();
            s.Begin(new float3(0, 0, 0));
            s.OfferLine(SnapResolver.LevelArea, SnapKind.AreaEdge, new float3(0, 0, 20f), new float2(1, 0));
            Assert.Equal(SnapKind.None, s.Result().Kind);
        }

        [Fact]
        public void RightAngleFromLastEdge()
        {
            var pts = new List<float3> { new float3(0, 0, 0), new float3(50, 0, 0) };
            var axes = new List<float2> { float2.zero, float2.zero };
            var s = new SnapResolver();
            s.Begin(new float3(52, 0, 30));         // near the perpendicular through (50,0)
            s.OfferDirections(pts, axes, -1, false);
            var r = s.Result();
            Assert.Equal(SnapKind.Direction, r.Kind);
            Assert.Equal(50f, r.Position.x, 3);
            Assert.Equal(30f, r.Position.z, 3);
            Assert.True(r.HasGuide);
        }

        [Fact]
        public void GuideThroughEarlierCorner()
        {
            var pts = new List<float3> { new float3(0, 0, 0), new float3(50, 0, 0), new float3(50, 0, 40) };
            var s = new SnapResolver();
            s.Begin(new float3(1.5f, 0, 39));
            s.OfferGuides(pts, null, -1);
            var r = s.Result();
            Assert.NotEqual(SnapKind.None, r.Kind);
            Assert.Equal(0f, r.Position.x, 3);
        }

        [Fact]
        public void SegmentClampsToEnds()
        {
            var s = new SnapResolver();
            s.Begin(new float3(-3, 0, 1));
            s.OfferSegment(SnapResolver.LevelObject, SnapKind.ObjectSide, new float3(0, 0, 0), new float3(10, 0, 0));
            var r = s.Result();
            Assert.Equal(SnapKind.ObjectSide, r.Kind);
            Assert.Equal(0f, r.Position.x, 3);
        }
    }
}
