using Citymaker.Core.Suite;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests.Suite
{
    public class SuiteLayoutGrowTests
    {
        [Fact]
        public void LandLaidOutLater_MatchesALaunchWithIt_AndNeverTouchesWhatIsLaidOut()
        {
            int n = SuiteLayout.HeightmapSize;
            var map = new ushort[n * n];
            for (int i = 0; i < map.Length; i++) map[i] = (ushort)(9000 + (i % n) / 7 + (i / n) / 5);   // a tilted map

            var launch = new SuiteLayout();
            launch.AddPlot(new SuitePlot { Size = new float2(140, 140), Ground = "hills" });
            var live = launch.Heightmap(map);
            float2 cell = launch.MapExtent / n;
            int2 build = (int2)((new float2(launch.LineX(2) + 70f, launch.LineZ(1) + 60f) - launch.MapOffset) / cell);
            live[build.y * n + build.x] ^= 0x55;   // a standing build's own change to the ground, inside the grid
            var before = (ushort[])live.Clone();
            float2 keepMin = launch.FixtureMin, keepMax = launch.FixtureMax;

            launch.AddPlot(new SuitePlot { Size = new float2(400, 900), Ground = "mountains" });   // taller than the grid: the band grows too
            int changed = launch.PatchOutside(live, map, keepMin, keepMax);
            Assert.True(changed > 1000);

            var fresh = launch.Heightmap(map);   // as if both plots were there at launch
            int differ = 0, touchedInside = 0;
            for (int py = 0; py < n; py++)
                for (int px = 0; px < n; px++)
                {
                    int i = py * n + px;
                    float x = launch.MapOffset.x + (px + 0.5f) * cell.x, z = launch.MapOffset.y + (py + 0.5f) * cell.y;
                    bool inside = x >= keepMin.x && x <= keepMax.x && z >= keepMin.y && z <= keepMax.y;
                    if (inside) { if (live[i] != before[i]) touchedInside++; }
                    else if (live[i] != fresh[i]) differ++;
                }
            Assert.Equal(0, touchedInside);
            Assert.Equal(0, differ);
        }
    }
}
