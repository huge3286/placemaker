// s57: blocker lots never extend past their own build, and a neighbouring block's fill only sees the build's real outline.

using System.Collections.Generic;
using System.Linq;
using Citymaker.Core;
using Citymaker.Core.Build;
using Citymaker.Core.Geometry;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    public class ZoneBlockerClipTests
    {
        private static float2[] Rect(float x0, float z0, float x1, float z1) =>
            new[] { new float2(x0, z0), new float2(x1, z0), new float2(x1, z1), new float2(x0, z1) };

        [Fact]
        public void Blockers_stay_inside_the_footprint()
        {
            var foot = Rect(0, -40, 80, -4);
            var grid = new ZoneGrid { Position = new float2(40, -24), Direction = new float2(0, 1), Size = new int2(10, 6) };
            var r = ZoneGuard.Compute(new[] { foot }, new float2[0][], new string[0], new List<float2[]>(), new[] { grid });
            Assert.NotEmpty(r.Blockers);
            foreach (var b in r.Blockers)
                foreach (var p in b)
                    Assert.True(p.x >= -0.01f && p.x <= 80.01f && p.y >= -40.01f && p.y <= -3.99f, "blocker vertex outside the footprint: " + p);
        }

        [Fact]
        public void Fill_is_unaffected_by_a_blocker_poking_past_the_outline_when_only_the_outline_counts()
        {
            var block = Rect(0, 0, 100, 100);
            var outline = Rect(60, 0, 100, 100);                 // the neighbour build's saved outline
            var poking = Rect(58, 0, 100, 100);                  // a helper lot 2 m past it
            var good = FillWalls.ClipToSeed(block, new List<float2[]> { outline }, new float2(10, 50), out _);
            Assert.InRange(Poly.Area(good), 5999f, 6001f);
            var bad = FillWalls.ClipToSeed(block, new List<float2[]> { poking }, new float2(10, 50), out _);
            Assert.True(Poly.Area(bad) < Poly.Area(good));        // what the helper lot used to do
        }
    }
}
