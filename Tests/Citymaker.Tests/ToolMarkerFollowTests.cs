using System.Collections.Generic;
using Citymaker.Core;
using Citymaker.Core.Geometry;
using Citymaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    public class ToolMarkerFollowTests
    {
        private static Recipe WithMarker(float3[] ring, int seg, float t, string kind = "entrance")
        {
            var r = new Recipe { Root = new RecipeStep { Layer = "fake" } };
            r.Root.Markers.Add(new EdgeMarker { Kind = kind, RingSegment = seg, RingT = t, Position = math.lerp(ring[seg], ring[(seg + 1) % ring.Length], t) });
            return r;
        }

        private static NearbyNet Road(float2 a, float2 b)
        {
            var bez = CubicBezier.Line(new float3(a.x, 0, a.y), new float3(b.x, 0, b.y));
            return new NearbyNet { A = bez.A, B = bez.B, C = bez.C, D = bez.D, Width = 10 };
        }

        [Fact]
        public void DraggingAPointKeepsTheMarkerOnItsEdge()
        {
            var ring = ToolTestData.Square(100);
            var r = WithMarker(ring, 0, 0.5f);           // middle of the bottom edge (0,0)→(100,0)
            var moved = (float3[])ring.Clone();
            moved[1] = new float3(140, 0, -20);          // drag the corner
            Assert.True(MarkerFollow.Reproject(r, ring, moved, new[] { "0" }));
            var m = r.Root.Markers[0];
            Assert.Equal(0, m.RingSegment);
            Assert.Equal(0.5f, m.RingT, 4);
            Assert.Equal(new float3(70, 0, -10), m.Position);
        }

        [Fact]
        public void InsertingAPointProjectsToTheClosestPoint()
        {
            var ring = ToolTestData.Square(100);
            var r = WithMarker(ring, 2, 0.25f);          // on the top edge (100,100)→(0,100) at x = 75
            var inserted = new[] { ring[0], ring[1], ring[2], new float3(50, 0, 100), ring[3] };
            Assert.True(MarkerFollow.Reproject(r, ring, inserted, new[] { "0" }));
            var m = r.Root.Markers[0];
            Assert.Equal(2, m.RingSegment);
            Assert.Equal(new float3(75, 0, 100), m.Position);
            Assert.Equal(0.5f, m.RingT, 4);
        }

        [Fact]
        public void MarkersFollowAShrunkOutline()
        {
            var ring = ToolTestData.Square(100);
            var r = WithMarker(ring, 1, 0.5f);           // right edge at (100,50)
            var smaller = new[] { new float3(0, 0, 0), new float3(80, 0, 0), new float3(80, 0, 40), new float3(60, 0, 100), new float3(0, 0, 100) };
            MarkerFollow.Reproject(r, ring, smaller, new[] { "0" });
            var m = r.Root.Markers[0];
            Assert.True(Composer.ClosestSegment(smaller, m.Position.xz, out int seg, out float t, out float d));
            Assert.True(d < 1e-3f);
            Assert.Equal(seg, m.RingSegment);
            Assert.Equal(t, m.RingT, 3);
        }

        [Fact]
        public void AutomaticMarkersFarFromRoadsAreDroppedEditedOnesKept()
        {
            var ring = ToolTestData.Square(100);
            var nets = new List<NearbyNet> { Road(new float2(-50, -5), new float2(150, -5)) };   // along the bottom edge
            var auto = WithMarker(ring, 2, 0.5f);       // top edge: 105 m from the road
            auto.Root.Markers.Add(new EdgeMarker { Kind = "entrance", RingSegment = 0, RingT = 0.5f, Position = new float3(50, 0, 0) });
            var other = new[] { ring[0], ring[1], ring[2], new float3(50, 0, 110), ring[3] };
            Assert.True(MarkerFollow.Reproject(auto, ring, other, new string[0], () => nets));
            Assert.Single(auto.Root.Markers);
            Assert.Equal(new float3(50, 0, 0), auto.Root.Markers[0].Position);

            var edited = WithMarker(ring, 2, 0.5f);
            MarkerFollow.Reproject(edited, ring, other, new[] { "0" }, () => nets);
            Assert.Single(edited.Root.Markers);
        }
    }
}
