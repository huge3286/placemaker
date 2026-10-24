// s58: every type that parks (parking, rest area, transit plaza, campground, sports park, park car park, logistics yard ...) gets its
// stall markings from the one planner, so they all carry the scale that keeps neighbouring markings apart (s53).

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Citymaker.Core;
using Citymaker.Core.Tool;
using Xunit;

namespace Citymaker.Tests
{
    public class StallMarkingsAllTypesTests
    {
        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        public static IEnumerable<object[]> Gens()
        {
            foreach (var g in GeneratorRegistry.All)
                if (!GeneratorRegistry.IsHidden(g) && !GeneratorRegistry.IsLoose(g.Id)) yield return new object[] { g.Id };
        }

        [Theory]
        [MemberData(nameof(Gens))]
        public void RegularStallMarkingsAreScaledToTheirStall(string gen)
        {
            var ring = TestKit.Rect(140, 130);
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get(gen), 5);
            uint q = 0;
            Editor.AddDefaultFeatures(r, () => ++q);
            var plan = Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, ZoningCutoutTests.Streets(ring), CancellationToken.None);
            var bays = plan.Layers.Where(l => l != null).SelectMany(l => l.Objects).Where(o => o.Role == ObjectRole.ParkingBay).ToList();
            foreach (var b in bays)
                Assert.InRange(b.Scale, 0.85f, 1f);
            // A regular marking (3.2 x 6.2 m box) on the standard 3.0 x 5.9 m stall is shrunk; an unshrunk one is the accessible hatch (5.0 m box).
            if (bays.Count > 20) Assert.Contains(bays, b => b.Scale < 0.99f);
        }
    }
}
