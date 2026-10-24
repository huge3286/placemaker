using System.Linq;
using Citymaker.Core;
using Citymaker.Core.Layers.Common;
using Citymaker.Core.Tool;
using Xunit;

namespace Citymaker.Tests
{
    /// <summary>rf-parks (docs/refactor/parks.md): the picnic lawn style and per-preset feature values.</summary>
    public class ParksRefactorTests
    {
        private static int Groups(RecipeStep root)
        {
            var c = root.Children.First(x => x.Layer == "picnic" && x.Enabled);
            return System.Convert.ToInt32(c.Settings.Values["groups"]);
        }

        [Fact]
        public void PicnicLawnGivesThePicnicFeatureMoreGroupsAndOtherStylesPutTheDefaultBack()
        {
            var editor = new DesignEditor(GeneratorRegistry.Get);
            var r = editor.SingleLayerRecipe(GeneratorRegistry.Get("park"), 3);
            editor.AddDefaultFeatures(r, () => 1);
            Assert.Equal(2, Groups(r.Root));
            Assert.True(editor.ApplyPreset(r, RecipePaths.Root, "picnic"));
            Assert.Equal(8, Groups(r.Root));
            Assert.DoesNotContain(r.Root.Children, c => c.Layer == "flowerbeds" && c.Enabled);
            editor.ApplyPreset(r, RecipePaths.Root, "natural");
            Assert.Equal(2, Groups(r.Root));
        }

        [Fact]
        public void ResolvedPicnicPresetIsNotModified()
        {
            var editor = new DesignEditor(GeneratorRegistry.Get);
            var store = new PresetStore(editor, () => System.DateTime.UtcNow);
            var step = store.Resolve("park", "picnic");
            Assert.Equal(8, Groups(step));
            // The editor's apply path and the store's resolve agree, so the picked style does not show as modified.
            var r = editor.SingleLayerRecipe(GeneratorRegistry.Get("park"), 3);
            editor.AddDefaultFeatures(r, () => 1);
            editor.ApplyPreset(r, RecipePaths.Root, "picnic");
            Assert.False(store.IsModified(r.Root, "picnic"));
        }

        [Fact]
        public void OnlyTheUrbanPocketStartsWithStreetShops()
        {
            var gen = GeneratorRegistry.Get("park");
            var shops = Features.Of(gen).First(f => f.Layer == LayerBase.StreetShopsLayer);
            Assert.False(shops.DefaultOn);
            foreach (var p in gen.Presets)
                Assert.Equal(p.Id == "urban", p.FeatureOn(shops));
        }

        [Fact]
        public void PicnicLawnPlansPicnicGroupsOnAnOpenLawn()
        {
            var editor = new DesignEditor(GeneratorRegistry.Get);
            var r = editor.SingleLayerRecipe(GeneratorRegistry.Get("park"), 3);
            editor.AddDefaultFeatures(r, () => 1);
            editor.ApplyPreset(r, RecipePaths.Root, "picnic");
            var ring = TestKit.Rect(170, 150);
            var site = TestKit.RoadAlongBottom(170);
            TestKit.Entered(r, ring, site);
            var plan = Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, System.Threading.CancellationToken.None);
            int tables = plan.Layers.SelectMany(l => l.Objects).Count(o => o.Category == AssetCategories.Picnic);
            Assert.True(tables >= 12, "picnic tables: " + tables);
        }
    }
}
