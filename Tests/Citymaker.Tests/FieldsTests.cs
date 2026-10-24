using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Citymaker.Core;
using Citymaker.Core.Geometry;
using Citymaker.Core.Layers.AddOns;
using Citymaker.Core.Layers.AddOns.Fields;
using Citymaker.Core.Layers.Common;
using Citymaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Citymaker.Tests
{
    /// <summary>Grand park wave g1: sports fields as park / sports-park features.</summary>
    [Collection(TimingCollection.Name)]
    public class FieldsTests
    {
        private readonly ITestOutputHelper _out;
        public FieldsTests(ITestOutputHelper output) { _out = output; }

        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        public static IEnumerable<object[]> FieldData() => FieldSpecs.Ids.Select(i => new object[] { i });

        private static SettingsBag Bag(params (string key, object value)[] kv)
        {
            var b = new SettingsBag();
            foreach (var (k, v) in kv) b.Values[k] = v;
            return b;
        }

        private static float3[] L(float hectares) => TestKit.LShape(math.sqrt(hectares * 10000f / 0.6975f));

        private static Recipe With(string layer, params string[] features)
        {
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get(layer), 11);
            uint seed = 300;
            foreach (var f in features) Assert.True(Editor.SetFeature(r, "0", f, true, seed++), "feature " + f + " of " + layer);
            return r;
        }

        private static ComposedPlan Compose(Recipe r, float3[] ring, SiteContext site = null) =>
            Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site ?? new SiteContext(), CancellationToken.None);

        private static AreaPlan Layer(ComposedPlan p, string id) => p.Layers.Single(l => l.GeneratorId == id);
        private static float2[] Ring2(float3[] r) => r.Select(p => p.xz).ToArray();

        private static string Errors(ComposedPlan p) =>
            string.Join("; ", p.AllIssues().Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Key));

        private static SiteContext Slope(float grade = 0.05f) => new SiteContext { HeightAt = p => 20f + grade * p.x + 0.02f * p.y };

        // ───────────── standalone ─────────────

        [Theory]
        [MemberData(nameof(FieldData))]
        public void FieldStandsAloneWithEveryPresetAndIsDeterministic(string id)
        {
            var ring = TestKit.Rect(260, 200);
            var a = TestKit.Plan(id, TestKit.Input(ring, seed: 5));
            Assert.False(a.HasErrors, string.Join(",", a.Issues.Select(i => i.Key)));
            Assert.True(a.Facts.ContainsKey("fieldFormat"), id + " built no field: " + string.Join(",", a.Issues.Select(i => i.Key)));
            Assert.NotEmpty(a.Surfaces);
            Assert.Contains(a.Objects, o => o.Role == ObjectRole.Decal);
            Assert.Contains(a.Objects, o => o.Role != ObjectRole.Decal);
            TestKit.AssertObjectsInside(a, ring);
            TestKit.AssertSurfacesAccepted(a);
            TestKit.AssertObjectsClearOfNets(a);
            Assert.Equal(TestKit.Fingerprint(a), TestKit.Fingerprint(TestKit.Plan(id, TestKit.Input(ring, seed: 5))));
            var presets = TestKit.Gen(id).Presets;
            Assert.InRange(presets.Count, 3, 6);
            foreach (var p in presets)
                foreach (var shape in new[] { L(1f), L(6f) })
                {
                    var plan = TestKit.Plan(id, TestKit.Input(shape, p.Values.Clone(), seed: 9));
                    Assert.False(plan.HasErrors, id + "/" + p.Id + ": " + string.Join(",", plan.Issues.Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Key)));
                    TestKit.AssertObjectsInside(plan, shape);
                    TestKit.AssertSurfacesAccepted(plan);
                    var r2 = Ring2(shape);
                    foreach (var n in plan.Nets)
                        foreach (var q in new[] { n.A.xz, n.D.xz })
                            Assert.True(Poly.Contains(r2, q) || Poly.DistanceToBoundary(r2, q) < 0.6f, $"{id}/{p.Id}: {n.Role} leaves the area");
                }
            var tiny = TestKit.Plan(id, TestKit.Input(TestKit.Rect(12, 10), seed: 8));
            Assert.False(tiny.HasErrors, string.Join(",", tiny.Issues.Select(i => i.Key)));
        }

        [Fact]
        public void FieldsHaveRealDimensions()
        {
            var big = TestKit.Rect(300, 220);
            AreaPlan P(string id, params (string, object)[] kv) => TestKit.Plan(id, TestKit.Input(big, Bag(kv), seed: 3));
            var pitch = P("pitch", ("format", "full"));
            Assert.Equal(105.0, pitch.Facts["pitchLength"], 1);
            Assert.Equal(68.0, pitch.Facts["pitchWidth"], 1);
            Assert.Equal(2.0, pitch.Facts["equipment"]);   // two goals
            var track = P("runningTrack", ("format", "standard"));
            Assert.Equal(400.0, track.Facts["trackLength"], 0);
            Assert.Equal(8.0, track.Facts["lanes"]);
            Assert.Equal(105.0, track.Facts["pitchLength"], 1);   // a full pitch fits inside a 400 m oval
            var diamond = P("baseballDiamond", ("format", "major"));
            Assert.Equal(27.43, diamond.Facts["baseDistance"], 2);
            Assert.Equal(99.0, diamond.Facts["fenceDistance"], 1);
            var tennis = P("tennisCourts", ("courts", 4.0));
            Assert.Equal(4.0, tennis.Facts["courts"]);
            Assert.Equal(4.0, tennis.Facts["equipment"]);   // a net per court
            Assert.Equal(2.0, P("basketballCourt").Facts["hoops"]);
            var cricket = P("cricketGround", ("format", "club"));
            Assert.InRange(cricket.Facts["boundaryLong"], 50.0, 60.0);
            Assert.True(P("skateBowl", ("style", "bowl")).Facts["bowlDepth"] > 1.0);
            // Small-sided: the 7-a-side pitch is 55 × 37 m.
            var seven = P("pitch", ("format", "seven"));
            Assert.Equal(55.0, seven.Facts["pitchLength"], 1);
        }

        [Fact]
        public void LinesAreOneDecalAssetAndFollowTheField()
        {
            var plan = TestKit.Plan("pitch", TestKit.Input(TestKit.Rect(200, 150), Bag(("format", "full")), seed: 4));
            var decals = plan.Objects.Where(o => o.Role == ObjectRole.Decal).ToList();
            Assert.True(decals.Count > 150, "too few line decals: " + decals.Count);
            Assert.Single(decals.Select(d => d.Variation).Distinct());
            Assert.All(decals, d => Assert.Equal(AssetCategories.DecalFieldLine, d.Category));
            // Lines off: no decals at all.
            var off = TestKit.Plan("pitch", TestKit.Input(TestKit.Rect(200, 150), Bag(("lines", false)), seed: 4));
            Assert.DoesNotContain(off.Objects, o => o.Role == ObjectRole.Decal);
        }

        // ───────────── ground ─────────────

        [Theory]
        [MemberData(nameof(FieldData))]
        public void PadIsLevelledAndBlendedAtTheEdge(string id)
        {
            var ring = TestKit.Rect(260, 200);
            var site = Slope();
            var plan = TestKit.Plan(id, TestKit.Input(ring, seed: 6, site: site));
            Assert.False(plan.HasErrors);
            Assert.True(plan.Facts.TryGetValue("levelPieces", out var pieces) && pieces > 1, id + " did not level its pad");
            Assert.Contains(plan.Issues, i => i.Key == TerrainShaper.PermanentKey);
            Assert.All(plan.Terraform, t => Assert.True(t.Permanent));
            // Apron pieces blend towards the original ground: the further from the pad, the smaller the change.
            var levels = plan.Terraform.Where(t => t.Kind == TerraformKind.Level).ToList();
            var pad = levels.OrderByDescending(t => Poly.Area(Ring2(t.Ring))).First();
            var pr = Ring2(pad.Ring);
            var pc = Poly.Centroid(pr);
            var ax = math.normalize(pr[1] - pr[0]);
            var sd = new float2(-ax.y, ax.x);
            float phl = math.distance(pr[0], pr[1]) * 0.5f, phw = math.distance(pr[1], pr[2]) * 0.5f;
            // Box distance outside the pad (the apron's bands are rectangular rings).
            float Out(float2 p) => math.max(math.abs(math.dot(p - pc, ax)) - phl, math.abs(math.dot(p - pc, sd)) - phw);
            var apron = levels.Where(t => t != pad).Select(t =>
            {
                var c = Poly.Centroid(Ring2(t.Ring));
                return (d: Out(c), change: math.abs(t.TargetHeight - site.HeightAt(c)), local: math.abs(pad.TargetHeight - site.HeightAt(c)));
            }).ToList();
            Assert.NotEmpty(apron);
            float far = apron.Max(x => x.d);
            foreach (var x in apron)
            {
                Assert.True(x.change <= x.local + 0.01f, $"{id}: an apron piece overshoots the pad");
                // The outermost band keeps less than 40 % of the pad's difference to the ground there.
                if (x.d >= far * 0.8f) Assert.True(x.change <= x.local * 0.4f + 0.05f, $"{id}: apron edge piece changes the ground by {x.change:0.00} of {x.local:0.00} m");
            }
            // Equipment stands on the levelled pad.
            var padH = pad.TargetHeight;
            var padRing = Ring2(pad.Ring);
            foreach (var o in plan.Objects.Where(o => Poly.Contains(padRing, o.Position.xz) && Poly.DistanceToBoundary(padRing, o.Position.xz) > 0.5f))
                Assert.Equal(padH, o.Position.y, 2);
        }

        [Fact]
        public void FlatGroundIsNotTouchedAndLevellingCanBeSwitchedOff()
        {
            var flat = TestKit.Plan("pitch", TestKit.Input(TestKit.Rect(200, 150), seed: 2));
            Assert.Empty(flat.Terraform);
            Assert.DoesNotContain(flat.Issues, i => i.Key == TerrainShaper.PermanentKey);
            var off = TestKit.Plan("pitch", TestKit.Input(TestKit.Rect(200, 150), Bag(("level", false)), seed: 2, site: Slope()));
            Assert.DoesNotContain(off.Terraform, t => t.Kind == TerraformKind.Level);
        }

        // ───────────── placement ─────────────

        [Fact]
        public void FieldTurnsNorthSouthWhenAsked()
        {
            var ring = TestKit.Rect(260, 200);   // long axis east–west
            var auto = TestKit.Plan("pitch", TestKit.Input(ring, Bag(("format", "nine")), seed: 3));
            var ns = TestKit.Plan("pitch", TestKit.Input(ring, Bag(("format", "nine"), ("orientation", "northSouth")), seed: 3));
            Assert.True(math.abs((float)ns.Facts["fieldAxisZ"]) > 0.98f, "north–south pitch axis " + ns.Facts["fieldAxisX"] + "," + ns.Facts["fieldAxisZ"]);
            Assert.True(math.abs((float)auto.Facts["fieldAxisX"]) > 0.98f);
        }

        [Fact]
        public void FieldShrinksToTheBiggestFormatThatFits()
        {
            // 90 × 70 m: no full pitch, but a 9-a-side one (87 × 62) fits.
            var plan = TestKit.Plan("pitch", TestKit.Input(TestKit.Rect(92, 72), seed: 2));
            Assert.False(plan.HasErrors);
            Assert.Equal(1.0, plan.Facts["fieldFormat"]);
            // Asking for a full pitch says it was made smaller.
            var asked = TestKit.Plan("pitch", TestKit.Input(TestKit.Rect(92, 72), Bag(("format", "full")), seed: 2));
            Assert.Contains(asked.Issues, i => i.Key == FieldAddOn.SmallerKey);
            var tennis = TestKit.Plan("tennisCourts", TestKit.Input(TestKit.Rect(45, 40), Bag(("courts", 4.0)), seed: 2));
            Assert.Equal(2.0, tennis.Facts["courts"]);
            Assert.Contains(tennis.Issues, i => i.Key == "Citymaker.Field.Tennis.Issue.Fewer");
        }

        [Theory]
        [MemberData(nameof(FieldData))]
        public void FieldComposesInAParkWithAPathToIt(string id)
        {
            var ring = TestKit.Rect(340, 260);
            var plan = Compose(With("park", id), ring, TestKit.RoadAlongBottom(340));
            Assert.False(plan.HasErrors, Errors(plan));
            var park = Layer(plan, "park");
            var region = park.Regions.SingleOrDefault(r => r.Key == Amenities.Place(id));
            Assert.True(region != null, id + ": the park reserved no place: " + string.Join(",", park.Issues.Select(i => i.Key)));
            var place = Ring2(region.Ring);
            var field = Layer(plan, id);
            Assert.EndsWith("@" + Amenities.Place(id), field.StepPath);
            Assert.True(field.Facts.ContainsKey("fieldFormat"), id + " built nothing in its place");
            foreach (var o in field.Objects.Where(o => o.Role != ObjectRole.Decal))
                Assert.True(Poly.Contains(place, o.Position.xz) || Poly.DistanceToBoundary(place, o.Position.xz) < 0.05f, $"{id} {o.Role} outside its place");
            foreach (var s in field.Surfaces)
                foreach (var p in s.Ring) Assert.True(Poly.Contains(place, p.xz) || Poly.DistanceToBoundary(place, p.xz) < 0.3f, $"{id} surface leaves its place");
            foreach (var n in field.Nets)
                foreach (var p in new[] { n.A.xz, n.D.xz }) Assert.True(Poly.Contains(place, p) || Poly.DistanceToBoundary(place, p) < 0.3f, $"{id} fence leaves its place");
            foreach (var o in park.Objects.Where(o => o.Role != ObjectRole.Decal)) Assert.False(Poly.Contains(place, o.Position.xz), $"park {o.Role} inside the {id}");
            foreach (var o in field.Objects.Where(o => o.Role != ObjectRole.Decal))
                foreach (var n in park.Nets.Where(n => n.Role != NetRole.Fence))
                    Assert.True(TestKit.DistanceToCurve(o.Position.xz, n) >= Composer.DefaultHalfWidth(n.Role) + 0.2f, $"{id} {o.Role} on a path");
            TestKit.AssertSurfacesAccepted(field);
            // "Path to it": a park path ends at (or runs past) the place.
            Assert.Contains(park.Nets, n => n.Role != NetRole.Fence && Enumerable.Range(0, 17)
                .Select(i => Composer.Bezier(n.A, n.B, n.C, n.D, i / 16f).xz)
                .Any(p => Poly.DistanceToBoundary(place, p) < 3.5f + Composer.DefaultHalfWidth(n.Role)));
        }

        [Fact]
        public void BigFieldIsScaledToFitASmallerPark()
        {
            // A 1.6 ha park: no club cricket ground (124 × 112 m) with paths, so a smaller ground is reserved.
            var ring = TestKit.Rect(150, 110);
            var plan = Compose(With("park", "cricketGround"), ring, TestKit.RoadAlongBottom(150));
            Assert.False(plan.HasErrors, Errors(plan));
            var park = Layer(plan, "park");
            var region = park.Regions.SingleOrDefault(r => r.Key == Amenities.Place("cricketGround"));
            if (region == null)
            {
                Assert.Contains(park.Issues, i => i.Key == "Citymaker.Park.Issue.NoRoomForAmenity");
                return;
            }
            var spec = Amenities.Spec("cricketGround");
            Assert.True(Poly.Area(Ring2(region.Ring)) < spec.Length * spec.Width - 1f);
            Assert.True(Layer(plan, "cricketGround").Facts["fieldFormat"] >= 2.0);
        }

        [Fact]
        public void SportsParkOffersTheFieldsAndTheyFindTheirOwnSpot()
        {
            var feats = Features.Of(GeneratorRegistry.Get("sportsPark")).Select(f => f.Layer).ToList();
            foreach (var id in FieldSpecs.Ids) Assert.Contains(id, feats);
            var r = With("sportsPark", "pitch", "tennisCourts");
            r.Root.Settings.Values["pitches"] = 0.0;
            r.Root.Settings.Values["tennis"] = 0.0;
            r.Root.Settings.Values["basketball"] = 1.0;
            var ring = TestKit.Rect(240, 180);
            var plan = Compose(r, ring, TestKit.RoadAlongBottom(240));
            Assert.False(plan.HasErrors, Errors(plan));
            var pitch = Layer(plan, "pitch");
            Assert.True(pitch.Facts.ContainsKey("fieldFormat"), "pitch: " + string.Join(",", pitch.Issues.Select(i => i.Key)));
            var tennis = Layer(plan, "tennisCourts");
            Assert.True(tennis.Facts.ContainsKey("fieldFormat"), "tennis: " + string.Join(",", tennis.Issues.Select(i => i.Key)));
            TestKit.AssertObjectsInside(pitch, ring);
            // Nothing of the pitch sits on the sports park's paths or its other facilities' equipment.
            var host = Layer(plan, "sportsPark");
            foreach (var o in pitch.Objects.Where(o => o.Role != ObjectRole.Decal))
                foreach (var n in host.Nets.Where(n => n.Role != NetRole.Fence))
                    Assert.True(TestKit.DistanceToCurve(o.Position.xz, n) >= Composer.DefaultHalfWidth(n.Role) + 0.2f, $"pitch {o.Role} on a path");
            // The pitch and the courts do not overlap.
            float2 Centre(AreaPlan p) => p.Surfaces.Count > 0 ? Poly.Centroid(Ring2(p.Surfaces[0].Ring)) : float2.zero;
            Assert.True(math.distance(Centre(pitch), Centre(tennis)) > 30f);
        }

        // ───────────── degradation, strings, budget ─────────────

        [Fact]
        public void FieldsDegradeWhenAssetsAreMissing()
        {
            var none = new Dictionary<string, int>
            {
                [AssetCategories.DecalMarkings] = 0, [AssetCategories.DecalFieldLine] = 0, [AssetCategories.DecalCourtBasketball] = 0, [AssetCategories.SportsGoal] = 0, [AssetCategories.FloodLight] = 0, [AssetCategories.Lamp] = 0,
                [AssetCategories.SportsStand] = 0, [AssetCategories.SportsTennis] = 0, [AssetCategories.SportsBasketball] = 0, [AssetCategories.SportsSkate] = 0,
                [AssetCategories.SurfaceDirt] = 0,
            };
            using (AssetAvailability.Scope(none))
                foreach (var id in FieldSpecs.Ids)
                {
                    var plan = TestKit.Plan(id, TestKit.Input(TestKit.Rect(260, 200), seed: 4));
                    Assert.False(plan.HasErrors, id);
                    Assert.DoesNotContain(plan.Objects, o => o.Role == ObjectRole.Decal);
                    Assert.DoesNotContain(plan.Objects, o => o.Role == ObjectRole.Lamp);
                    Assert.DoesNotContain(plan.Objects, o => o.Category == AssetCategories.SportsGoal || o.Category == AssetCategories.SportsTennis);
                }
        }

        [Fact]
        public void EveryFieldKeyHasEnglishText()
        {
            var lang = TestKit.Lang();
            var missing = new List<string>();
            void Need(string key) { if (!string.IsNullOrEmpty(key) && !lang.ContainsKey(key)) missing.Add(key); }
            foreach (var id in FieldSpecs.Ids.Concat(new[] { "park", "sportsPark" }))
            {
                var g = TestKit.Gen(id);
                Need(g.NameKey);
                foreach (var c in g.Controls)
                {
                    Need(c.LabelKey); Need(c.TooltipKey);
                    if (c.Group != "Basics") Need("Citymaker.UI.Group." + c.Group);
                    foreach (var o in c.Options) { Need(o.LabelKey); Need(o.TooltipKey); }
                }
                foreach (var p in g.Presets) Need(p.LabelKey);
                foreach (var f in Features.Of(g)) { Need(f.LabelKey); Need(f.TooltipKey); }
            }
            foreach (var k in new[] { FieldAddOn.SmallerKey, FieldAddOn.OrientationKey, "Citymaker.Field.Tennis.Issue.Fewer",
                "Citymaker.Stat.Fields", "Citymaker.Stat.Seats", "Citymaker.Stat.Decals", "Citymaker.Stat.TerrainChange", "Citymaker.Stat.SkateFeatures" })
                Need(k);
            Assert.True(missing.Count == 0, "Missing en-US keys: " + string.Join(", ", missing.Distinct()));
        }

        [Fact]
        public void FieldsStayWithinBudget()
        {
            var ring = L(5f);
            foreach (var id in FieldSpecs.Ids)
            {
                TestKit.Plan(id, TestKit.Input(ring, seed: 1, site: Slope(0.03f)));
                var sw = Stopwatch.StartNew();
                TestKit.Plan(id, TestKit.Input(ring, seed: 1, site: Slope(0.03f)));
                _out.WriteLine($"{id} 5 ha: {sw.Elapsed.TotalMilliseconds:0} ms");
                Assert.True(sw.Elapsed.TotalMilliseconds < 250, id + " took " + sw.Elapsed.TotalMilliseconds);
            }
            var park = With("park", "pitch", "tennisCourts", "basketballCourt");
            var blob = TestKit.Blob(130, 120, 3);
            Compose(park, blob, TestKit.RoadAlongBottom(250));
            var sw2 = Stopwatch.StartNew();
            Compose(park, blob, TestKit.RoadAlongBottom(250));
            _out.WriteLine($"park 5 ha + 3 fields: {sw2.Elapsed.TotalMilliseconds:0} ms");
            Assert.True(sw2.Elapsed.TotalMilliseconds < 600, "park took " + sw2.Elapsed.TotalMilliseconds);
        }
    }
}
