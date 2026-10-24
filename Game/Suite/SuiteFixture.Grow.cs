// Game runner: land made to order laid out in a running session (a queue session gets a scenario with "land"):
// the plot is added east of the last one, its ground written into the live heightmap only where the grown fixture
// reaches beyond what is already laid out (SuiteLayout.PatchOutside), so the grid, earlier plots and standing builds
// keep their ground.

using Citymaker.Core.Suite;
using Unity.Mathematics;

namespace Citymaker.Game.Suite
{
    internal sealed partial class SuiteFixture
    {
        /// <summary>The map's heightmap as read at launch, before the fixture replaced it (null until then).</summary>
        private ushort[] _mapHeights;

        /// <summary>True when the plot (already added) lies inside the playable map with room for its ease.</summary>
        public bool PlotFits(SuitePlot plot)
        {
            var lo = Layout.MapOffset + Layout.Blend;
            var hi = Layout.MapOffset + Layout.MapExtent - Layout.Blend;
            return plot.OuterMin.x >= lo.x && plot.OuterMin.y >= lo.y && plot.OuterMax.x <= hi.x && plot.OuterMax.y <= hi.y;
        }

        /// <summary>Takes a plot added by <see cref="AddPlot"/> back out (it did not fit).</summary>
        public void RemovePlot(SuitePlot plot, SuiteBlock block)
        {
            Layout.Plots.Remove(plot);
            if (block != null) { Blocks.Remove(block); _used.Remove(block.Id); }
        }

        /// <summary>
        /// Writes the ground of land added after launch: every texel outside <paramref name="keepMin"/>..<paramref name="keepMax"/>
        /// (the fixture before the plot) that the grown fixture now reaches. Returns null or the problem.
        /// </summary>
        public string GrowHeightmap(float2 keepMin, float2 keepMax, out int changed)
        {
            changed = 0;
            var current = ReadHeightmap(out var source);
            if (current == null) return "heightmap unavailable";
            changed = Layout.PatchOutside(current, _mapHeights, keepMin, keepMax);
            if (changed > 0) Replace(current);
            Note($"grow heightmap (read from {source}, map ground {(_mapHeights != null ? "from launch" : "unknown: current heights used")}): {changed} texels");
            return null;
        }
    }
}
