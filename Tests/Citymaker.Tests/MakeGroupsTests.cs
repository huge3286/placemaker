// s42: the Design picker's categories come from [AreaGenerator(Group = …)] (RecipeInfo.group). Every type the
// player can pick must declare one of the groups the UI knows (UI/src/stages/PresetBrowser.tsx MAKE_GROUPS).
using System.Linq;
using Citymaker.Core;
using Xunit;

namespace Citymaker.Tests
{
    public class MakeGroupsTests
    {
        private static readonly string[] Known =
        {
            "parks", "squares", "roads", "commercial", "parking", "gardens", "water", "nature", "sports", "events", "farms",
            "transport", "industrial", "other",
        };

        [Fact]
        public void EveryVisibleType_DeclaresAKnownGroup()
        {
            var missing = GeneratorRegistry.All
                .Where(g => g.Kind == LayerKind.Fill && !GeneratorRegistry.IsHidden(g))
                .Where(g => !Known.Contains(GeneratorGroups.Of(g)))
                .Select(g => g.Id + "=" + GeneratorGroups.Of(g))
                .ToList();
            Assert.Empty(missing);
        }

        [Theory]
        [InlineData("park", "parks")]
        [InlineData("plaza", "squares")]
        [InlineData("campground", "nature")]
        public void CoreTypes_HaveTheirGroup(string id, string group)
        {
            Assert.Equal(group, GeneratorGroups.Of(GeneratorRegistry.Get(id)));
        }
    }
}
