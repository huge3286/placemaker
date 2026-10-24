using System.Collections.Generic;
using Citymaker.Core;
using Citymaker.Core.Geometry;
using Citymaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    public class ToolAreaClipTests
    {
        private static AreaShape Rect(float x0, float z0, float x1, float z1) => new AreaShape
        {
            Ring = new[] { new float3(x0, 0, z0), new float3(x1, 0, z0), new float3(x1, 0, z1), new float3(x0, 0, z1) },
            Source = AreaSource.Drawn,
        };

        private static NearbyNet Straight(float2 a, float2 b, float width, NetCategory cat = NetCategory.Road)
        {
            var bez = CubicBezier.Line(new float3(a.x, 0, a.y), new float3(b.x, 0, b.y));
            return new NearbyNet { ExternalId = 1, A = bez.A, B = bez.B, C = bez.C, D = bez.D, Width = width, Category = cat };
        }

        private static float Area(AreaShape s) => Poly.Area(s.Ring2D());

        [Fact]
        public void RectangleOverlappingStraightRoadIsTrimmedToTheRoadEdge()
        {
            var shape = Rect(0, 0, 100, 60);
            var road = Straight(new float2(-50, 0), new float2(150, 0), 12);
            var r = AreaClip.Apply(shape, new[] { road });
            Assert.True(r.Clipped);
            float edge = 6f + AreaClip.EdgeMargin;
            Assert.Equal(100f * (60f - edge), Area(r.Shape), 1);
            foreach (var p in r.Shape.Ring) Assert.True(p.z >= edge - 0.01f, "vertex on the road: " + p);
            Assert.Equal(0, r.DroppedPieces);
            Assert.Equal(AreaClip.IssueTrimmed, r.Issue.Key);
            Assert.Equal(IssueSeverity.Info, r.Issue.Severity);
            Assert.NotEmpty(r.Removed);
            // The road now runs along the new edge: entrances are suggested there.
            Assert.Contains(r.Shape.Attachments, a => a.Kind == AttachmentKind.RoadAlongEdge);
            Assert.DoesNotContain(r.Shape.Attachments, a => a.Kind == AttachmentKind.RoadCrossing);
        }

        [Fact]
        public void CurvedRoadIsKeptOutOfTheArea()
        {
            var shape = Rect(0, 0, 100, 100);
            // Arc sweeping through the lower-left corner.
            var road = new NearbyNet
            {
                ExternalId = 2,
                A = new float3(-20, 0, 60), B = new float3(10, 0, 30), C = new float3(30, 0, 10), D = new float3(60, 0, -20),
                Width = 10, Category = NetCategory.Road,
            };
            var r = AreaClip.Apply(shape, new[] { road });
            Assert.True(r.Clipped);
            Assert.True(Area(r.Shape) < 10000f - 100f);
            var bez = new CubicBezier(road.A, road.B, road.C, road.D);
            // Every vertex of the clipped outline is at least the half width away from the centre line.
            foreach (var p in r.Shape.Ring)
            {
                float t = bez.ClosestParameterXZ(p.xz);
                Assert.True(math.distance(bez.Position(t).xz, p.xz) >= 5f - 0.05f, "vertex inside the road corridor: " + p);
            }
            Assert.True(Poly.IsSimple(r.Shape.Ring2D()));
            Assert.True(Poly.IsCcw(r.Shape.Ring2D()));
        }

        [Fact]
        public void RoadThroughTheMiddleKeepsTheLargestPieceAndReportsIt()
        {
            var shape = Rect(0, 0, 100, 60);
            var road = Straight(new float2(40, -50), new float2(40, 110), 10);
            var r = AreaClip.Apply(shape, new[] { road });
            Assert.True(r.Clipped);
            float hw = 5f + AreaClip.EdgeMargin;
            Assert.Equal((100f - 40f - hw) * 60f, Area(r.Shape), 1);
            foreach (var p in r.Shape.Ring) Assert.True(p.x >= 40f + hw - 0.01f);
            Assert.Equal(1, r.DroppedPieces);
            Assert.Equal((40f - hw) * 60f, r.DroppedArea, 1);
            Assert.Equal(AreaClip.IssueSplit, r.Issue.Key);
        }

        [Fact]
        public void CurbSnappedEdgeIsNotTouched()
        {
            var shape = Rect(0, 0, 100, 60);
            var road = Straight(new float2(-50, -6), new float2(150, -6), 12);   // outer edge exactly at z = 0
            var r = AreaClip.Apply(shape, new[] { road });
            Assert.False(r.Clipped);
            Assert.Same(shape, r.Shape);
        }

        [Fact]
        public void PathwaysAndFarRoadsAreIgnored()
        {
            var shape = Rect(0, 0, 100, 60);
            var path = Straight(new float2(-50, 30), new float2(150, 30), 4, NetCategory.Pathway);
            var far = Straight(new float2(-50, 500), new float2(150, 500), 12);
            var r = AreaClip.Apply(shape, new[] { path, far });
            Assert.False(r.Clipped);
            Assert.Null(r.Issue);
        }

        [Fact]
        public void AreaEntirelyOnARoadIsLeftAlone()
        {
            var shape = Rect(0, -2, 20, 2);
            var road = Straight(new float2(-50, 0), new float2(150, 0), 20);
            var r = AreaClip.Apply(shape, new[] { road });
            Assert.False(r.Clipped);
        }
    }
}
