// Waterfront type: presets, promenade along the water, railings, steps, piers over water, styles.

using System.Collections.Generic;
using System.Linq;
using Citymaker.Core;
using Citymaker.Core.Geometry;
using Citymaker.Core.Layers.Common;
using Citymaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;
using static Citymaker.Tests.Types.WaterTypesKit;

namespace Citymaker.Tests.Types
{
    [Collection(Citymaker.Tests.TimingCollection.Name)]
    public class WaterfrontTests
    {
        private readonly ITestOutputHelper _out;
        public WaterfrontTests(ITestOutputHelper output) { _out = output; }

        public static IEnumerable<object[]> PresetIds() => TestKit.Gen("waterfront").Presets.Select(p => new object[] { p.Id });
        private static PresetInfo Preset(string id) => TestKit.Gen("waterfront").Presets.Single(p => p.Id == id);

        private static bool PointOnBezier(PlannedNet n, float2 p)
        {
            for (int i = 0; i <= 32; i++) if (math.distance(Composer.Bezier(n.A, n.B, n.C, n.D, i / 32f).xz, p) < 0.6f) return true;
            return false;
        }

        [Fact]
        public void HasFourToSixPresetsAndDeepControls()
        {
            var g = TestKit.Gen("waterfront");
            Assert.InRange(g.Presets.Count, 4, 6);
            Assert.InRange(g.Controls.Count(c => !Citymaker.Core.Decals.DressingControls.IsDressingId(c.Id)), 15, 48);   // s20: water edge, deck height, quay/boardwalk assets, beds
            Assert.InRange(g.Controls.Where(c => !Citymaker.Core.Decals.DressingControls.IsDressingId(c.Id)).Select(c => c.Group).Distinct().Count(), 4, 10);
            Assert.Equal("water", GeneratorGroups.Of(g));
        }

        [Theory]
        [MemberData(nameof(PresetIds))]
        public void PresetPlansOneHectareAndSixHectareL(string preset)
        {
            foreach (var (ring, site) in new[] { (OneHa, SeaSouth(0.05f)), (SixHaL, SeaSouth(0.03f)), (OneHa, new SiteContext()), (FiveHa, WaterInside(50f)) })
            {
                var plan = PlanPreset("waterfront", Preset(preset), ring, site);
                Assert.False(plan.HasErrors, preset + ": " + string.Join(",", plan.Issues.Select(i => i.Key)));
                Assert.True(plan.Objects.Count > 10, preset + " placed only " + plan.Objects.Count);
                Assert.Contains(plan.Nets, n => n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide);
                AssertInside(plan, ring);
                AssertNothingOnPaths(plan);
                TestKit.AssertSurfacesAccepted(plan);
                AssertTerrainBlended(plan, ring, site, p => math.abs(p.y - (site.WaterDepthAt(new float2(125f, 49f)) > 0f && ring.Max(v => v.x) > 200f ? 50f : 0f)), 10f);
                var again = PlanPreset("waterfront", Preset(preset), ring, site);
                Assert.Equal(TestKit.Fingerprint(plan), TestKit.Fingerprint(again));
            }
        }

        [Theory]
        [MemberData(nameof(PresetIds))]
        public void PresetRespectsReserved(string preset)
        {
            var reserved = SomeReserved();
            var plan = PlanPreset("waterfront", Preset(preset), OneHa, SeaSouth(), 7, reserved);
            TestKit.AssertClearOfReserved(plan, reserved);
        }

        [Fact]
        public void FastOnFiveHectares()
        {
            PlanPreset("waterfront", TestKit.Gen("waterfront").Presets[0], FiveHa, SeaSouth(0.04f));   // JIT (as the other types' timing tests)
            foreach (var p in TestKit.Gen("waterfront").Presets)
                foreach (var site in new[] { SeaSouth(0.04f), WaterInside(50f) })
                {
                    long ms = Time(() => PlanPreset("waterfront", p, FiveHa, site));
                    _out.WriteLine($"{p.Id}: {ms} ms");
                    Assert.True(ms < TimingBudget.Ms(250), $"waterfront/{p.Id} took {ms} ms for 5 ha");
                }
        }

        [Fact]
        public void PromenadeBoardwalkRunsAlongTheWaterWithBenchesLampsAndBeds()
        {
            var plan = PlanPreset("waterfront", Preset("promenade"), FiveHa, SeaSouth(0.02f));
            // s20: a timber boardwalk just off the bank (pier nets over the water), joined by links to ONE promenade
            // parallel to the water promWidth (14 m) from it, on a level terrace.
            var boardwalk = plan.Nets.Where(n => n.Category == AssetCategories.NetPier).ToList();
            Assert.NotEmpty(boardwalk);
            Assert.All(boardwalk, n => Assert.False(n.FollowTerrain));
            Assert.True(plan.Facts["edgeLength"] > 180f);
            // s30: the walk is the band centre, a smooth line set back from the water by half the band, its shoulder and the bank.
            Assert.True(plan.Nets.Any(n => n.Role == NetRole.PathwayWide && math.abs(n.A.z - n.D.z) < 1f && n.A.z > 12f && n.A.z < 20f), string.Join(" ", plan.Nets.Select(n => n.Role + ":" + n.A.xz + "-" + n.D.xz)));
            Assert.True(plan.Facts["edgeLinks"] >= 2, string.Join(" ", plan.Facts.Select(kv => kv.Key + "=" + kv.Value)));
            // No railings on a graded embankment (nothing drops).
            Assert.DoesNotContain(plan.Nets, n => n.Role == NetRole.Fence);
            // Benches on the promenade's water side face the water; lamps on its land side; a tree row and planted beds behind.
            var benches = plan.Objects.Where(o => o.Role == ObjectRole.Bench).ToList();
            Assert.True(benches.Count >= 8);
            Assert.All(benches, b => Assert.True(math.cos(b.Yaw) < -0.7f && b.Position.z < 16.5f));
            Assert.Contains(plan.Objects, o => o.Role == ObjectRole.Lamp && o.Position.z > 16.5f);
            Assert.True(plan.Objects.Any(o => o.Role == ObjectRole.Tree && o.Position.z > 18f && o.Position.z < 30f));
            Assert.True(plan.Facts["beds"] >= 3);
            Assert.DoesNotContain(plan.Issues, i => i.Key == "Citymaker.Waterfront.Issue.PiersNeedWater");
        }

        [Fact]
        public void PiersReachOutOverWaterInsideTheArea()
        {
            var site = WaterInside(60f);
            var plan = PlanPreset("waterfront", Preset("pier"), FiveHa, site);
            Assert.Equal(2, plan.Facts["piers"]);
            var decks = plan.Nets.Where(n => !n.FollowTerrain).ToList();
            Assert.True(decks.Count(n => n.Category == AssetCategories.NetPier) >= 2 + 2, "piers with T-heads");
            // The pier park's water edge is a walkway quay; s30: each pier is a T junction ON it (no landing pads cut into the bank).
            Assert.Contains(decks, n => n.Category == AssetCategories.NetQuay);
            Assert.True(plan.Surfaces.Count(s => s.Role == SurfaceRole.Paving) >= 1);
            var quay = decks.Where(n => n.Category == AssetCategories.NetQuay).ToList();
            Assert.All(decks.Where(n => n.Category == AssetCategories.NetPier), p => Assert.True(quay.Any(q => math.distance(q.A.xz, p.A.xz) < 0.5f || math.distance(q.D.xz, p.A.xz) < 0.5f || PointOnBezier(q, p.A.xz)) || decks.Any(o => o != p && o.Category == AssetCategories.NetPier && math.distance(o.D.xz, p.A.xz) < 0.5f), $"pier at {p.A.xz} does not start on the quay"));
            Assert.All(decks, n => Assert.True(n.A.y > 0f && n.D.y > 0f, $"deck above the water: {n.Role} {n.Category} {n.A} {n.D}"));
            Assert.Contains(decks, n => site.WaterDepthAt(n.D.xz) > 0f && n.D.z < 30f);
            // Nothing placed in the water.
            Assert.All(plan.Objects, o => Assert.False(site.WaterDepthAt(o.Position.xz) > 0f, $"{o.Role} in the water at {o.Position.xz}"));
            AssertInside(plan, FiveHa);
        }

        [Fact]
        public void StylesLookDifferent()
        {
            AreaPlan P(string preset) => PlanPreset("waterfront", Preset(preset), FiveHa, SeaSouth(0.03f));
            // Harbour quay: a wide quay at the water (8 m deck), its apron at deck level, kiosks, nautical props, no fences.
            var harbor = P("harbor");
            Assert.Contains(harbor.Nets, n => n.Category == AssetCategories.NetQuay && n.Role == NetRole.PathwayWide && !n.FollowTerrain);
            Assert.DoesNotContain(harbor.Nets, n => n.Role == NetRole.Fence);
            Assert.Contains(harbor.Objects, o => o.Category == AssetCategories.WaterfrontNautical);
            Assert.True(harbor.Facts["kiosks"] >= 2);

            // River steps: paved flights on the smooth bank down to a low walkway quay (s30: no level patches), an avenue behind.
            var steps = P("riverSteps");
            Assert.True(steps.Facts["steps"] >= 2, "step flights");
            Assert.DoesNotContain(steps.Terraform, t => t.Kind == TerraformKind.Level && !t.Sloped);
            Assert.Contains(steps.Nets, n => n.Category == AssetCategories.NetQuay && n.Role == NetRole.Pathway);
            Assert.True(steps.Objects.Count(o => o.Role == ObjectRole.Tree) > P("promenade").Objects.Count(o => o.Role == ObjectRole.Tree));

            var fishing = P("fishing");
            Assert.True(fishing.Objects.Count(o => o.Category == AssetCategories.WaterfrontFishing) >= 8);
            Assert.True(fishing.Objects.Count(o => o.Category == AssetCategories.Rock) > 25, "riprap edge");
            Assert.Contains(fishing.Surfaces, s => s.Category == AssetCategories.SurfaceNatural);
        }

        [Fact]
        public void WaterSideChoice()
        {
            var west = Preset("promenade").Values.Clone();
            west.Values["waterSide"] = "west";
            var plan = TestKit.Plan("waterfront", TestKit.Input(FiveHa, west, 3));
            // No water to face: a natural bank, the promenade parallel to the west edge.
            Assert.Contains(plan.Nets, n => n.Role == NetRole.PathwayWide && math.abs(n.A.x - n.D.x) < 1f && n.A.x > 10f && n.A.x < 18f);
            Assert.DoesNotContain(plan.Nets, n => n.Category == AssetCategories.NetQuay || n.Category == AssetCategories.NetPier);
        }

        [Fact]
        public void MissingCategoriesDegradeToInfo()
        {
            var none = new Dictionary<string, int>
            {
                [AssetCategories.WaterfrontMooring] = 0, [AssetCategories.Bollard] = 0, [AssetCategories.WaterfrontNautical] = 0, [AssetCategories.Decoration] = 0,
                [AssetCategories.WaterfrontFishing] = 0, [AssetCategories.Kiosk] = 0, [AssetCategories.Rock] = 0,
            };
            using (AssetAvailability.Scope(none))
                foreach (var preset in new[] { "harbor", "fishing" })
                {
                    var plan = PlanPreset("waterfront", Preset(preset), FiveHa, SeaSouth());
                    Assert.False(plan.HasErrors);
                    Assert.Contains(plan.Issues, i => i.Key == Keys.NoAssetsIssue && i.Severity == IssueSeverity.Info);
                    Assert.DoesNotContain(plan.Objects, o => none.ContainsKey(o.Category ?? ""));
                    Assert.NotEmpty(plan.Nets);
                }
        }

        [Fact]
        public void EveryFeatureComposes()
        {
            var features = Features.Of(TestKit.Gen("waterfront")).Select(f => f.Layer).ToArray();
            var plan = Compose(With("waterfront", features), FiveHa, SeaSouthRoadNorth(250, 200));
            Assert.True(string.IsNullOrEmpty(Errors(plan)), Errors(plan));
            foreach (var l in plan.Layers) AssertInside(l, FiveHa, 1.2f);
            foreach (var f in features) Assert.Contains(plan.Layers, l => l.GeneratorId == f);
            Assert.Contains(plan.Layers[0].Regions, r => r.Tags.Contains(RegionTags.Core));
        }

        [Fact]
        public void LabelsAndRecipe()
        {
            AssertLabels("waterfront");
            AssertSourceKeys("Waterfront");
            AssertRecipe("waterfront");
            Assert.False(GeneratorRegistry.IsHidden(TestKit.Gen("waterfront")));
            Assert.Contains(AssetCategories.WaterfrontMooring, Citymaker.Core.Build.AssetHeuristics.Classify("MooringBollard01", Citymaker.Core.Build.AssetTraits.StaticObject | Citymaker.Core.Build.AssetTraits.HasMesh));
        }
    }
}
