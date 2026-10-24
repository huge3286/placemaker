// Grand park wave (g3): ground patches feature (dirt, mown grass, meadow, rocky area, gravel, sand).

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Citymaker.Core;
using Citymaker.Core.Geometry;
using Citymaker.Core.Layers.Common;
using Citymaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    [Collection(TimingCollection.Name)]
    public class GroundPatchesTests
    {
        private const string Id = "ground";

        public static IEnumerable<object[]> PresetIds() => GeneratorRegistry.Get(Id).Presets.Select(p => new object[] { p.Id });

        private static SettingsBag Preset(string id) => GeneratorRegistry.Get(Id).Presets.Single(p => p.Id == id).Values.Clone();

        private static SettingsBag With(SettingsBag b, params (string k, object v)[] kv)
        {
            var c = b?.Clone() ?? new SettingsBag();
            foreach (var (k, v) in kv) c.Values[k] = v;
            return c;
        }

        /// <summary>A park lawn as a park leaves it: a path across, trees, a reserved feature.</summary>
        private static AreaInput Lawn(SettingsBag s, uint seed = 1, float w = 140, float h = 100)
        {
            var input = TestKit.Input(TestKit.Rect(w, h), s, seed);
            input.Reserved.AddCorridor(new float2(0, h / 2), new float2(w, h / 2), 1.5f, nameof(NetRole.Pathway));
            input.Reserved.AddDisc(new float2(20, 20), 2.5f, "Tree");
            input.Reserved.AddDisc(new float2(100, 75), 2.5f, "Tree");
            input.Reserved.AddDisc(new float2(70, 20), 6f, "Centerpiece");
            return input;
        }

        [Fact]
        public void IsAnAddOnWithDeepTidyOptions()
        {
            var g = GeneratorRegistry.Get(Id);
            Assert.NotNull(g);
            Assert.Equal(LayerKind.AddOn, g.Kind);
            Assert.InRange(g.Presets.Count, 4, 6);
            Assert.InRange(g.Controls.Count(c => !Citymaker.Core.Decals.DressingControls.IsDressingId(c.Id)), 15, 40);
            Assert.InRange(g.Controls.Where(c => !Citymaker.Core.Decals.DressingControls.IsDressingId(c.Id)).Select(c => c.Group).Distinct().Count(), 4, 8);
            var lang = TestKit.Lang();
            foreach (var c in g.Controls)
            {
                Assert.True(lang.ContainsKey(c.LabelKey), c.LabelKey);
                if (!string.IsNullOrEmpty(c.TooltipKey)) Assert.True(lang.ContainsKey(c.TooltipKey), c.TooltipKey);
                foreach (var o in c.Options) Assert.True(lang.ContainsKey(o.LabelKey), o.LabelKey);
                Assert.True(lang.ContainsKey("Citymaker.UI.Group." + c.Group), "group " + c.Group);
            }
            foreach (var p in g.Presets) Assert.True(lang.ContainsKey(p.LabelKey), p.LabelKey);
        }

        [Theory]
        [MemberData(nameof(PresetIds))]
        public void PresetPlacesBlendedPatchesInsideAndClear(string preset)
        {
            var input = Lawn(Preset(preset));
            var plan = TestKit.Plan(Id, input);
            Assert.False(plan.HasErrors);
            Assert.True(plan.Facts["groundPatches"] >= 2, preset + " placed " + plan.Facts["groundPatches"]);
            TestKit.AssertObjectsInside(plan, input.Shape.Ring);
            TestKit.AssertClearOfReserved(plan.WithoutDecals(), input.Reserved);
            TestKit.AssertSurfacesAccepted(plan);
            var ring = Poly.To2D(input.Shape.Ring);
            foreach (var p in plan.Paint)
                foreach (var v in p.Ring)
                    Assert.True(Poly.Contains(ring, v.xz) || Poly.DistanceToBoundary(ring, v.xz) < 0.05f, "paint outside the area");
            // Blended: every painted patch has a soft outer ring (< full strength) around a full-strength core.
            Assert.Contains(plan.Paint, p => p.Strength < 0.5f);
            Assert.Contains(plan.Paint, p => p.Strength >= 0.99f);
            // Surfaces and paint stay off the path (the core is clear of the reserved corridor).
            foreach (var s in plan.Surfaces)
                foreach (var v in s.Ring)
                    Assert.True(math.abs(v.z - 50f) > 1.5f, "surface over the path");
        }

        [Theory]
        [MemberData(nameof(PresetIds))]
        public void PresetIsDeterministic(string preset)
        {
            Assert.Equal(TestKit.Fingerprint(TestKit.Plan(Id, Lawn(Preset(preset), 9))), TestKit.Fingerprint(TestKit.Plan(Id, Lawn(Preset(preset), 9))));
        }

        [Fact]
        public void EachKindHasItsOwnFinish()
        {
            var off = new[] { "dirt", "mown", "meadow", "rocky", "gravel", "sand" }.Select(k => (k, (object)0.0)).ToArray();
            AreaPlan One(string kind) => TestKit.Plan(Id, Lawn(With(With(null, off), (kind, 1.0), ("size", "large"))));
            var mown = One("mown");
            Assert.Contains(mown.Surfaces, s => s.Category == AssetCategories.SurfaceGrass);
            Assert.Contains(mown.Paint, p => p.Layer == 0);   // terrain under the mown oval back to base grass
            var sand = One("sand");
            Assert.Contains(sand.Surfaces, s => s.Category == AssetCategories.SurfaceSand || s.Category == AssetCategories.SurfaceNatural);
            var rocky = One("rocky");
            Assert.Contains(rocky.Objects, o => o.Category == AssetCategories.Rock);
            var meadow = One("meadow");
            Assert.Empty(meadow.Surfaces);
            Assert.Contains(meadow.Objects, o => o.Role == ObjectRole.Bush);
            var dirt = One("dirt");
            Assert.Empty(dirt.Surfaces);
            Assert.NotEmpty(dirt.Paint);
            var paintOnly = TestKit.Plan(Id, Lawn(With(null, ("finish", "paint"), ("sand", 2.0), ("gravel", 2.0))));
            Assert.Empty(paintOnly.Surfaces);
        }

        [Theory]
        [InlineData("scattered")]
        [InlineData("drift")]
        [InlineData("edges")]
        [InlineData("cluster")]
        public void EveryPositionPlacesPatches(string arrange)
        {
            var plan = TestKit.Plan(Id, Lawn(With(Preset("mixed"), ("arrange", arrange)), 4, 200, 150));
            Assert.False(plan.HasErrors);
            Assert.True(plan.Facts["groundPatches"] >= 3, arrange + ": " + plan.Facts["groundPatches"]);
        }

        [Fact]
        public void PathToItJoinsPatchesToTheNearestPath()
        {
            var plan = TestKit.Plan(Id, Lawn(With(Preset("sandGravel"), ("pathTo", true)), 2, 200, 150));
            Assert.True(plan.Facts.TryGetValue("groundPaths", out var n) && n >= 1);
            Assert.All(plan.Nets, net => Assert.True(math.abs(net.A.z - 75f) < 2f, "path does not start on the existing path"));
        }

        [Fact]
        public void FullRegionReportsNoSpace()
        {
            var input = TestKit.Input(TestKit.Rect(40, 30), Preset("mixed"));
            for (float x = 0; x <= 40; x += 3) input.Reserved.AddCorridor(new float2(x, 0), new float2(x, 30), 1.5f, "Pipe");
            var plan = TestKit.Plan(Id, input);
            Assert.False(plan.HasErrors);
            Assert.Empty(plan.Paint);
            Assert.Contains(plan.Issues, i => i.Key == Keys.NoSpace);
        }

        [Fact]
        public void ComposesAsAParkFeature()
        {
            var editor = new DesignEditor(GeneratorRegistry.Get);
            var r = editor.SingleLayerRecipe(GeneratorRegistry.Get("park"), 11);
            Assert.True(editor.SetFeature(r, "0", Id, true, 100));
            var ring = TestKit.LShape(math.sqrt(3f * 10000f / 0.6975f));
            var plan = Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, new SiteContext(), CancellationToken.None);
            Assert.DoesNotContain(plan.AllIssues(), i => i.Severity == IssueSeverity.Error);
            var layer = plan.Layers.Single(l => l.GeneratorId == Id);
            Assert.True(layer.Facts["groundPatches"] >= 1);
            Assert.NotEmpty(layer.Paint);
        }

        [Fact]
        public void IsFastOnFiveHectares()
        {
            var ring = TestKit.LShape(math.sqrt(5f * 10000f / 0.6975f));
            var s = With(Preset("mixed"), ("dirt", 3.0), ("rocky", 3.0), ("meadow", 3.0));
            TestKit.Plan(Id, TestKit.Input(ring, s));
            var sw = Stopwatch.StartNew();
            TestKit.Plan(Id, TestKit.Input(ring, s));
            Assert.True(sw.ElapsedMilliseconds < TimingBudget.Ms(250), sw.ElapsedMilliseconds + " ms");
        }
    }

    internal static class GroundTestExtensions
    {
        /// <summary>Decals never reserve space and may lie anywhere; the reserved check skips them.</summary>
        public static AreaPlan WithoutDecals(this AreaPlan p)
        {
            var c = new AreaPlan();
            c.Objects.AddRange(p.Objects.Where(o => o.Role != ObjectRole.Decal));
            return c;
        }
    }
}
