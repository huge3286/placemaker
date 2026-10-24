using System.Collections.Generic;
using Citymaker.Core;
using Citymaker.Core.Geometry;
using Citymaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    public class ToolAttachmentTests
    {
        private static NearbyNet Straight(long id, float3 a, float3 d, float width = 8) => new NearbyNet
        {
            ExternalId = id, A = a, B = math.lerp(a, d, 1f / 3), C = math.lerp(a, d, 2f / 3), D = d, Width = width,
        };

        [Fact]
        public void CrossingRoadIsFound()
        {
            var ring = ToolTestData.Square(100);
            var nets = new List<NearbyNet> { Straight(7, new float3(50, 0, -50), new float3(50, 0, 50)) };
            var atts = AttachmentFinder.Find(ring, nets);
            var c = atts.Find(a => a.Kind == AttachmentKind.RoadCrossing);
            Assert.Equal(7, c.ExternalId);
            Assert.Equal(0, c.RingSegment);
            Assert.Equal(0.5f, c.RingT, 3);
            Assert.Equal(0.5f, c.ExternalT, 2);
            Assert.Equal(50f, c.Position.x, 2);
        }

        [Fact]
        public void RoadAlongEdgeIsFound()
        {
            var ring = ToolTestData.Square(100);
            // Road 5 m outside the right edge (x = 105), running its full length.
            var nets = new List<NearbyNet> { Straight(9, new float3(105, 0, -20), new float3(105, 0, 120)) };
            var atts = AttachmentFinder.Find(ring, nets);
            Assert.Contains(atts, a => a.Kind == AttachmentKind.RoadAlongEdge && a.ExternalId == 9 && a.RingSegment == 1);
            Assert.DoesNotContain(atts, a => a.Kind == AttachmentKind.RoadCrossing);
        }

        [Fact]
        public void FarRoadIsIgnored()
        {
            var ring = ToolTestData.Square(100);
            var nets = new List<NearbyNet> { Straight(9, new float3(150, 0, -20), new float3(150, 0, 120)) };
            Assert.Empty(AttachmentFinder.Find(ring, nets));
        }
    }
}
