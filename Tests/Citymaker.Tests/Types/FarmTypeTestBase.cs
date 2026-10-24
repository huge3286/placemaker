// Shared definition-of-done checks for the farm types (urbanFarm, animalPark): every preset plans a
// 1 ha square and a 6 ha L-shape cleanly, deterministically, inside the area, clear of its paths and
// of reserved space, with accepted surfaces and terraform blended at the edge; 5 ha in < 250 ms;
// every feature composes; every key has English text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Citymaker.Core;
using Citymaker.Core.Geometry;
using Citymaker.Core.Layers.Common;
using Citymaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Citymaker.Tests.Types
{
    public abstract class FarmTypeTestBase
    {
        protected readonly ITestOutputHelper Out;
        protected FarmTypeTestBase(ITestOutputHelper output) { Out = output; }

        protected abstract string TypeId { get; }
        /// <summary>Folder under Core/Layers/Types holding the type's sources (literal keys are checked).</summary>
        protected abstract string Folder { get; }

        protected static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);
        protected static readonly float3[] OneHa = TestKit.Rect(100, 100);
        protected static readonly float3[] SixHaL = TestKit.LShape(293);   // 0.6975 · 293² ≈ 6.0 ha
        protected static readonly float3[] FiveHa = TestKit.Rect(250, 200);

        protected IAreaGenerator Gen => TestKit.Gen(TypeId);

        protected Recipe With(params string[] features)
        {
            var r = Editor.SingleLayerRecipe(Gen, 11);
            uint seed = 100;
            foreach (var f in features) Assert.True(Editor.SetFeature(r, "0", f, true, seed++), "feature " + f);
            return r;
        }

        protected static ComposedPlan Compose(Recipe r, float3[] ring, SiteContext site = null) =>
            Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site ?? new SiteContext(), CancellationToken.None);

        protected static string Errors(ComposedPlan p) =>
            string.Join("; ", p.AllIssues().Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Key + " " + string.Join(",", i.Args)));

        protected AreaPlan PlanPreset(PresetInfo p, float3[] ring, uint seed = 5, SiteContext site = null, ReservedSpace reserved = null)
        {
            var input = TestKit.Input(ring, p.Values.Clone(), seed, null, site ?? TestKit.RoadAlongBottom(300));
            if (reserved != null) input.Reserved = reserved;
            return Gen.Plan(input, CancellationToken.None);
        }

        public static void AssertInside(AreaPlan plan, float3[] ring, float tolerance = 0.05f)
        {
            var r = Poly.To2D(ring);
            foreach (var o in plan.Objects)
                Assert.True(Poly.Contains(r, o.Position.xz) || Poly.DistanceToBoundary(r, o.Position.xz) < tolerance,
                    $"{plan.GeneratorId}: {o.Role}/{o.Category} at {o.Position.xz} lies outside the area");
            foreach (var n in plan.Nets)
                foreach (var p in new[] { n.A, n.D })
                    Assert.True(Poly.Contains(r, p.xz) || Poly.DistanceToBoundary(r, p.xz) < 0.5f, $"{plan.GeneratorId}: {n.Role} end {p.xz} outside");
            foreach (var s in plan.Surfaces)
                foreach (var p in s.Ring)
                    Assert.True(Poly.Contains(r, p.xz) || Poly.DistanceToBoundary(r, p.xz) < 0.3f, $"{plan.GeneratorId}: surface point {p.xz} outside");
            foreach (var t in plan.Terraform)
                Assert.True(Poly.Contains(r, Poly.Centroid(Poly.To2D(t.Ring))), $"{plan.GeneratorId}: terraform outside");
            foreach (var p in plan.Paint)
                foreach (var v in p.Ring) Assert.True(Poly.Contains(r, v.xz) || Poly.DistanceToBoundary(r, v.xz) < 0.3f, "paint outside the area");
        }

        public static void AssertNothingOnPaths(AreaPlan plan)
        {
            var paths = plan.Nets.Where(n => n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide).ToList();
            foreach (var o in plan.Objects)
            {
                if (o.Role == ObjectRole.Decal || o.Role == ObjectRole.AccessPoint || o.Role == ObjectRole.AnimalSpawner) continue;
                foreach (var n in paths)
                {
                    if (math.distance(o.Position.xz, n.A.xz) > math.distance(n.A.xz, n.D.xz) + 10f) continue;
                    float d = TestKit.DistanceToCurve(o.Position.xz, n);
                    Assert.True(d >= Composer.DefaultHalfWidth(n.Role) + 0.3f,
                        $"{plan.GeneratorId}: {o.Role}/{o.Category} at {o.Position.xz} is {d:0.00} m from a {n.Role}");
                }
            }
        }

        public static void AssertTerraformBlended(AreaPlan plan, float3[] ring, Func<float2, float> height)
        {
            var r = Poly.To2D(ring);
            foreach (var t in plan.Terraform)
            {
                Assert.True(t.Permanent);
                var c = Poly.Centroid(Poly.To2D(t.Ring));
                float edge = Poly.DistanceToBoundary(r, c);
                float change = math.abs(t.TargetHeight - height(c));
                Assert.True(change < edge * 0.3f + 0.3f, $"{plan.GeneratorId}: {change:0.00} m change {edge:0.0} m from the edge (cliff)");
            }
        }

        // ───────────── facts ─────────────

        [Fact]
        public void HasFourToSixDistinctPresetsAndASubtypeChoice()
        {
            Assert.InRange(Gen.Presets.Count, 4, 6);
            Assert.Contains(Gen.Controls, c => c.Kind == ControlKind.Choice && c.Tier == ControlTier.Essential);
            Assert.InRange(Gen.Controls.Count(c => !Citymaker.Core.Decals.DressingControls.IsDressingId(c.Id)), 15, 40);
            var groups = Gen.Controls.Where(c => !Citymaker.Core.Decals.DressingControls.IsDressingId(c.Id)).Select(c => c.Group).Where(g => g != "entrances").Distinct().Count();
            Assert.InRange(groups, 4, 8);
            Assert.Equal("farms", GeneratorGroups.Of(Gen));
            // Presets differ in more than one value from each other.
            var ps = Gen.Presets.ToList();
            for (int i = 0; i < ps.Count; i++)
                for (int j = i + 1; j < ps.Count; j++)
                    Assert.True(ps[i].Values.Values.Count(kv => !Equals(ps[j].Values.Values[kv.Key], kv.Value)) >= 2, ps[i].Id + " ~ " + ps[j].Id);
        }

        [Fact]
        public void EveryPresetPlansOneHectareAndSixHectareLShapeCleanly()
        {
            foreach (var p in Gen.Presets)
                foreach (var ring in new[] { OneHa, SixHaL })
                {
                    var plan = PlanPreset(p, ring);
                    Assert.False(plan.HasErrors, TypeId + "/" + p.Id + ": " + string.Join(",", plan.Issues.Select(i => i.Key)));
                    Assert.True(plan.Objects.Count > 20, $"{TypeId}/{p.Id} placed only {plan.Objects.Count} objects");
                    AssertInside(plan, ring);
                    AssertNothingOnPaths(plan);
                    TestKit.AssertSurfacesAccepted(plan);
                    Assert.Contains(plan.Regions, r => r.Tags.Contains(RegionTags.Core));
                    AssertTerraformBlended(plan, ring, _ => 0f);
                }
        }

        [Fact]
        public void EveryPresetIsDeterministic()
        {
            foreach (var p in Gen.Presets)
                Assert.Equal(TestKit.Fingerprint(PlanPreset(p, SixHaL, 9)), TestKit.Fingerprint(PlanPreset(p, SixHaL, 9)));
        }

        [Fact]
        public void PresetsKeepOutOfReservedSpace()
        {
            var reserved = new ReservedSpace();
            reserved.AddCorridor(new float2(-5, 140), new float2(300, 160), 2.5f, "Pathway");
            reserved.AddDisc(new float2(70, 70), 8f, "Centerpiece");
            reserved.AddDisc(new float2(200, 40), 5f, "Furniture");
            reserved.Polygons.Add(new[] { new float2(20, 200), new float2(60, 200), new float2(60, 240), new float2(20, 240) });
            foreach (var p in Gen.Presets)
            {
                var plan = PlanPreset(p, SixHaL, 3, null, reserved.Clone());
                Assert.False(plan.HasErrors);
                TestKit.AssertClearOfReserved(plan, reserved);
                foreach (var o in plan.Objects)
                    Assert.False(Poly.Contains(reserved.Polygons[0], o.Position.xz), $"{p.Id}: {o.Role} in a reserved polygon");
            }
        }

        [Fact]
        public void TerrainIsBlendedOnASlope()
        {
            var site = new SiteContext { HeightAt = q => 0.03f * q.x + 0.01f * q.y };
            site.NearbyNets.AddRange(TestKit.RoadAlongBottom(300).NearbyNets);
            int shaped = 0;
            foreach (var p in Gen.Presets)
            {
                var plan = PlanPreset(p, SixHaL, 4, site);
                Assert.False(plan.HasErrors);
                AssertTerraformBlended(plan, SixHaL, site.HeightAt);
                if (plan.Terraform.Count > 0)
                {
                    shaped++;
                    Assert.Contains(plan.Issues, i => i.Severity == IssueSeverity.Warning && (i.Key == TerrainShaper.PermanentKey || i.Key.Contains("Permanent") || i.Key.Contains("Lake") || i.Key.Contains("Pond")));
                }
            }
            Assert.True(shaped >= 1, "no preset shapes the terrain");
        }

        [Fact]
        public void FiveHectaresInUnder250Ms()
        {
            var site = TestKit.RoadAlongBottom(250);
            foreach (var p in Gen.Presets) PlanPreset(p, FiveHa, 2, site);   // JIT
            long worst = 0; string slow = "";
            foreach (var p in Gen.Presets)
            {
                var sw = Stopwatch.StartNew();
                PlanPreset(p, FiveHa, 2, site);
                sw.Stop();
                Out.WriteLine($"{TypeId}/{p.Id}: {sw.ElapsedMilliseconds} ms");
                if (sw.ElapsedMilliseconds > worst) { worst = sw.ElapsedMilliseconds; slow = p.Id; }
            }
            Assert.True(worst < TimingBudget.Ms(250), $"{TypeId}/{slow} took {worst} ms for 5 ha");
        }

        [Fact]
        public void EveryFeatureComposes()
        {
            var features = Features.Of(Gen).Select(f => f.Layer).ToArray();
            Assert.True(features.Length >= 5);
            var plan = Compose(With(features), FiveHa, TestKit.RoadAlongBottom(250));
            Assert.True(string.IsNullOrEmpty(Errors(plan)), Errors(plan));
            foreach (var l in plan.Layers) AssertInside(l, FiveHa, 1.2f);
            foreach (var f in features) Assert.Contains(plan.Layers, l => l.GeneratorId == f);
            // Features keep out of the type's places (fields / enclosures).
            var host = plan.Layers.Single(l => l.GeneratorId == TypeId);
            var places = host.Regions.Where(r => r.Tags.Any(t => t.StartsWith("place:"))).Select(r => Poly.To2D(r.Ring)).ToList();
            Assert.NotEmpty(places);
            foreach (var l in plan.Layers.Where(l => l.GeneratorId != TypeId && l.GeneratorId != "decals" && l.GeneratorId != "edgeZoning"))
                foreach (var o in l.Objects)
                    Assert.False(places.Any(pl => Poly.Contains(pl, o.Position.xz) && Poly.DistanceToBoundary(pl, o.Position.xz) > 1.5f),
                        $"{l.GeneratorId}: {o.Role}/{o.Category} at {o.Position.xz} stands in a {TypeId} place");
        }

        [Fact]
        public void EveryKeyHasEnglishText()
        {
            var lang = TestKit.Lang();
            var missing = new List<string>();
            void Need(string key) { if (!string.IsNullOrEmpty(key) && !lang.ContainsKey(key)) missing.Add(key); }
            var gens = new List<IAreaGenerator> { Gen };
            foreach (var f in Features.Of(Gen)) { gens.Add(TestKit.Gen(f.Layer)); Need(f.LabelKey); Need(f.TooltipKey); }
            foreach (var g in gens)
            {
                Need(g.NameKey);
                foreach (var c in g.Controls)
                {
                    Need(c.LabelKey); Need(c.TooltipKey);
                    foreach (var o in c.Options) { Need(o.LabelKey); Need(o.TooltipKey); }
                    if (!string.IsNullOrEmpty(c.Group)) Need("Citymaker.UI.Group." + c.Group);
                }
                foreach (var p in g.Presets) Need(p.LabelKey);
            }
            var root = TestKit.RepoRoot();
            foreach (var file in Directory.GetFiles(Path.Combine(root, "Core", "Layers", "Types", Folder), "*.cs"))
                foreach (Match m in Regex.Matches(File.ReadAllText(file), "\"(Citymaker\\.[A-Za-z0-9_.:]+[A-Za-z0-9])\""))
                    Need(m.Groups[1].Value);
            foreach (var f in typeof(AssetCategories).GetFields())
            {
                var cat = (string)f.GetValue(null);
                if (cat.StartsWith(Folder.ToLowerInvariant() + ".")) Need(Keys.Category(cat));
            }
            // Shipped recipe with name, description and an icon.
            var recipe = TestKit.LoadRecipe(TypeId + ".json");
            Assert.Equal(TypeId, recipe.Root.Layer);
            Need(recipe.NameKey);
            var json = File.ReadAllText(Path.Combine(root, "Recipes", TypeId + ".json"));
            var desc = Regex.Match(json, "\"descriptionKey\"\\s*:\\s*\"([^\"]+)\"");
            Assert.True(desc.Success, "recipe has no descriptionKey");
            Need(desc.Groups[1].Value);
            Assert.StartsWith("Media/Game/", recipe.Icon);
            Assert.True(missing.Count == 0, "Missing en-US keys: " + string.Join(", ", missing.Distinct()));
        }

        [Fact]
        public void RandomizeIsValid()
        {
            var g = Gen;
            for (uint seed = 1; seed < 12; seed++)
            {
                var v = g.Randomize(g.Defaults(), seed, new HashSet<string>());
                var plan = g.Plan(TestKit.Input(TestKit.Rect(160, 120), v, seed, null, TestKit.RoadAlongBottom(160)), CancellationToken.None);
                Assert.False(plan.HasErrors, "seed " + seed);
            }
        }
    }
}
