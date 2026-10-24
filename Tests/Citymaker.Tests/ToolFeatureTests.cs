using System.Collections.Generic;
using System.Threading;
using Citymaker.Core;
using Citymaker.Core.Layers.AddOns;
using Citymaker.Core.Layers.Park;
using Citymaker.Core.Layers.Plaza;
using Citymaker.Core.Tool;
using Xunit;

namespace Citymaker.Tests
{
    public class ToolFeatureTests
    {
        /// <summary>Fill layer with one DefaultOn feature, delegating everything else to the fake generator.</summary>
        private sealed class FeatureHost : IAreaGenerator, IHasFeatures
        {
            private readonly ToolFakeGenerator _inner = new ToolFakeGenerator { Id = "host" };
            public IReadOnlyList<FeatureInfo> Features { get; } = new List<FeatureInfo>
            {
                new FeatureInfo { Layer = "playground", Target = "core", DefaultOn = true },
                new FeatureInfo { Layer = "fountain", Target = "" },
            };
            public string Id => "host";
            public string NameKey => _inner.NameKey;
            public string Icon => "";
            public LayerKind Kind => LayerKind.Fill;
            public string[] Accepts => _inner.Accepts;
            public string[] Emits => _inner.Emits;
            public int Version => 1;
            public IReadOnlyList<ControlSchema> Controls => _inner.Controls;
            public IReadOnlyList<PresetInfo> Presets => _inner.Presets;
            public SettingsBag Defaults() => _inner.Defaults();
            public float MinArea => 10;
            public float MaxArea => 1e7f;
            public Readiness CheckReady(AreaInput input) => new Readiness();
            public List<EdgeMarker> SuggestMarkers(AreaInput input) => new List<EdgeMarker>();
            public SettingsBag Randomize(SettingsBag current, uint seed, ICollection<string> locked) => current.Clone();
            public AreaPlan Plan(AreaInput input, CancellationToken cancel) => new AreaPlan();
        }

        private readonly Dictionary<string, IAreaGenerator> _gens = new Dictionary<string, IAreaGenerator>
        {
            ["park"] = new ParkLayer(),
            ["plaza"] = new PlazaLayer(),
            ["playground"] = new PlaygroundAddOn(),
            ["fountain"] = new FountainAddOn(),
            ["restrooms"] = new RestroomsAddOn(),
            ["host"] = new FeatureHost(),
        };

        private DesignEditor Editor() => new DesignEditor(id => id != null && _gens.TryGetValue(id, out var g) ? g : null);

        [Fact]
        public void SetFeatureAddsAndRemovesChildStep()
        {
            var e = Editor();
            var r = e.SingleLayerRecipe(_gens["park"], 1);
            Assert.True(e.SetFeature(r, "0", "playground", true, 5));
            Assert.Single(r.Root.Children);
            Assert.Equal("playground", r.Root.Children[0].Layer);
            Assert.Equal(RegionTags.Core, r.Root.Children[0].Target);
            Assert.Equal("medium", r.Root.Children[0].Settings.S("size", ""));
            Assert.False(e.SetFeature(r, "0", "playground", true, 6));    // already on
            Assert.False(e.SetFeature(r, "0", "nonsense", true, 6));      // not a feature of park
            Assert.True(e.SetFeature(r, "0", "restrooms", true, 7));
            Assert.True(e.SetFeature(r, "0", "playground", false, 0));
            Assert.Single(r.Root.Children);
            Assert.Equal("restrooms", r.Root.Children[0].Layer);
            Assert.False(e.SetFeature(r, "0", "playground", false, 0));
        }

        [Fact]
        public void FeaturePresetIsAppliedOverLayerDefaults()
        {
            var e = Editor();
            var r = e.SingleLayerRecipe(_gens["plaza"], 1);
            Assert.True(e.SetFeature(r, "0", "fountain", true, 2));
            Assert.Equal("large", r.Root.Children[0].Settings.S("size", ""));
            Assert.Equal("", r.Root.Children[0].Target);
        }

        [Fact]
        public void DisabledFeatureChildIsReEnabledNotDuplicated()
        {
            var e = Editor();
            var r = e.SingleLayerRecipe(_gens["park"], 1);
            e.SetFeature(r, "0", "playground", true, 2);
            r.Root.Children[0].Enabled = false;
            Assert.True(e.SetFeature(r, "0", "playground", true, 3));
            Assert.Single(r.Root.Children);
            Assert.True(r.Root.Children[0].Enabled);
        }

        [Fact]
        public void DefaultOnFeaturesAreAddedOnce()
        {
            var e = Editor();
            var r = e.SingleLayerRecipe(_gens["host"], 1);
            Assert.True(e.AddDefaultFeatures(r, () => 9u));
            Assert.Single(r.Root.Children);
            Assert.Equal("playground", r.Root.Children[0].Layer);
            Assert.False(e.AddDefaultFeatures(r, () => 9u));
        }

        [Fact]
        public void StepViewPublishesFeatures()
        {
            var e = Editor();
            var r = e.SingleLayerRecipe(_gens["park"], 1);
            e.SetFeature(r, "0", "restrooms", true, 2);
            var json = UiModels.Design(r, "park", false, "0", null, null, id => _gens.TryGetValue(id ?? "", out var g) ? g : null);
            var features = json["root"]["features"];
            Assert.NotNull(features);
            bool sawRestrooms = false, sawPlayground = false;
            foreach (var f in features.Items)
            {
                if (f["layer"].AsString() == "restrooms") { sawRestrooms = true; Assert.True(f["enabled"].AsBool()); Assert.Equal("0/0", f["path"].AsString()); }
                if (f["layer"].AsString() == "playground") { sawPlayground = true; Assert.False(f["enabled"].AsBool()); }
            }
            Assert.True(sawRestrooms && sawPlayground);
        }

        private static AreaPlan Run(string gen, string path, int objects, params PlanIssue[] issues)
        {
            var p = new AreaPlan { GeneratorId = gen, StepPath = path };
            for (int i = 0; i < objects; i++) p.Objects.Add(new PlannedObject());
            p.Issues.AddRange(issues);
            return p;
        }

        private JsonValue FeatureJson(Recipe r, ComposedPlan plan, string layer, bool stale = false)
        {
            var json = UiModels.Design(r, "park", false, "0", null, plan, id => _gens.TryGetValue(id ?? "", out var g) ? g : null, null, null, stale);
            foreach (var f in json["root"]["features"].Items) if (f["layer"].AsString() == layer) return f;
            return null;
        }

        [Fact]
        public void FeatureFitStatusFollowsTheLatestPlan()
        {
            var e = Editor();
            var r = e.SingleLayerRecipe(_gens["park"], 1);
            e.SetFeature(r, "0", "restrooms", true, 2);   // 0/0
            e.SetFeature(r, "0", "playground", true, 3);  // 0/1 (core)
            e.SetFeature(r, "0", "fountain", true, 4);    // 0/2

            // No plan yet: nothing to say.
            Assert.False(FeatureJson(r, null, "restrooms").Has("status"));

            var plan = new ComposedPlan();
            plan.Layers.Add(Run("park", "0@root", 5));
            plan.Layers.Add(Run("restrooms", "0/0@root", 2));
            plan.Layers.Add(Run("playground", "0/1@core", 4, new PlanIssue { Severity = IssueSeverity.Info, Key = "Citymaker.AddOn.Playground.Issue.Smaller" },
                new PlanIssue { Severity = IssueSeverity.Warning, Key = "Citymaker.Common.Issue.TerrainPermanent" }));
            // fountain never ran (its region was not carved out) → no room

            var ok = FeatureJson(r, plan, "restrooms")["status"];
            Assert.Equal("ok", ok["kind"].AsString());
            var reduced = FeatureJson(r, plan, "playground")["status"];
            Assert.Equal("reduced", reduced["kind"].AsString());
            Assert.Equal("Citymaker.AddOn.Playground.Issue.Smaller", reduced["text"]["key"].AsString());
            var failed = FeatureJson(r, plan, "fountain")["status"];
            Assert.Equal("failed", failed["kind"].AsString());
            Assert.Equal(UiModels.NoRoomKey, failed["text"]["key"].AsString());
            Assert.False(FeatureJson(r, plan, "picnic").Has("status"));   // off → no status

            // A stale plan never claims "no room" for a feature it simply has not run yet.
            Assert.False(FeatureJson(r, plan, "fountain", true).Has("status"));
            Assert.True(FeatureJson(r, plan, "restrooms", true)["status"]["stale"].AsBool());

            // Ran but placed nothing, with a NoSpace warning → failed with that text.
            plan.Layers.Add(Run("fountain", "0/2@root", 0, new PlanIssue { Severity = IssueSeverity.Warning, Key = "Citymaker.Common.Issue.NoSpace", Args = new object[] { "x" } }));
            var noSpace = FeatureJson(r, plan, "fountain")["status"];
            Assert.Equal("failed", noSpace["kind"].AsString());
            Assert.Equal("Citymaker.Common.Issue.NoSpace", noSpace["text"]["key"].AsString());

            // Runs in several regions, some empty → reduced "fits in 1 of 2".
            plan.Layers.Add(Run("restrooms", "0/0@block:2", 0));
            var some = FeatureJson(r, plan, "restrooms")["status"];
            Assert.Equal("reduced", some["kind"].AsString());
            Assert.Equal(UiModels.SomeMissingKey, some["text"]["key"].AsString());
        }

        [Fact]
        public void MakeRowShowsSingleTypesOnly()
        {
            var e = Editor();
            System.Func<string, IAreaGenerator> res = id => _gens.TryGetValue(id ?? "", out var g) ? g : null;
            var plain = e.SingleLayerRecipe(_gens["park"], 1);
            Assert.True(Features.ShowInMakeRow(plain, false, res));
            var withFeature = plain.Clone();
            e.SetFeature(withFeature, "0", "playground", true, 2);
            Assert.False(Features.ShowInMakeRow(withFeature, false, res));
            Assert.True(Features.ShowInMakeRow(withFeature, true, res));      // player recipes always listed
            var addOnRoot = e.SingleLayerRecipe(_gens["playground"], 1);
            Assert.False(Features.ShowInMakeRow(addOnRoot, false, res));
        }
    }
}
