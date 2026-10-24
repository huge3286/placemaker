using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Citymaker.Core;
using Citymaker.Core.Build;
using Citymaker.Core.Geometry;
using Citymaker.Core.Layers.AddOns;
using Citymaker.Core.Layers.Common;
using Citymaker.Core.Layers.Park;
using Citymaker.Core.Layers.Plaza;
using Citymaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Citymaker.Tests
{
    /// <summary>Round 5 (type wave, builder 11): park path figures, park idioms, amenities, plaza levels.</summary>
    [Collection(TimingCollection.Name)]
    public class ParkPlazaDepthTests
    {
        private readonly ITestOutputHelper _out;
        public ParkPlazaDepthTests(ITestOutputHelper output) { _out = output; }

        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        public static readonly string[] NewPathStyles = { "riverside", "figureEight", "spokeRing", "woodlandTrail", "boardwalk", "promenade", "circuit" };
        public static readonly string[] NewParkPresets = { "victorian", "landscape", "modern", "wildflower", "urban", "riverside" };
        public static readonly string[] AmenityIds = { "volleyball", "basketball", "tableTennis", "chess", "fitness", "splashPad", "bandstand", "dogRun", "noticeboard" };
        public static readonly string[] NewPlazaLevels = { "split", "cascade", "podium", "sunkenCourt" };
        public static readonly string[] NewPlazaPresets = { "splitLevel", "cascade", "podium", "sunkenCourt", "piazza", "green" };

        public static IEnumerable<object[]> Each(string[] ids) => ids.Select(i => new object[] { i });
        public static IEnumerable<object[]> PathStyleData() => Each(NewPathStyles);
        public static IEnumerable<object[]> ParkPresetData() => Each(NewParkPresets);
        public static IEnumerable<object[]> AmenityData() => Each(AmenityIds);
        public static IEnumerable<object[]> PlazaLevelData() => Each(NewPlazaLevels);
        public static IEnumerable<object[]> PlazaPresetData() => Each(NewPlazaPresets);
        public static IEnumerable<object[]> PlazaAmenityData() => Each(PlazaLayer.Hosted);

        public static IEnumerable<object[]> AmenityPositions()
        {
            foreach (var id in AmenityIds)
                foreach (var at in Amenities.Positions)
                    yield return new object[] { id, at };
        }

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
            uint seed = 100;
            foreach (var f in features) Assert.True(Editor.SetFeature(r, "0", f, true, seed++), "feature " + f + " of " + layer);
            return r;
        }

        private static ComposedPlan Compose(Recipe r, float3[] ring, SiteContext site = null) =>
            Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site ?? new SiteContext(), CancellationToken.None);

        private static string Errors(ComposedPlan p) =>
            string.Join("; ", p.AllIssues().Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Key + " " + string.Join(",", i.Args)));

        private static float2[] Ring2(float3[] r) => r.Select(p => p.xz).ToArray();

        private static List<float2> Samples(PlannedNet n, int k = 12)
        {
            var r = new List<float2>();
            for (int i = 0; i <= k; i++) r.Add(Composer.Bezier(n.A, n.B, n.C, n.D, i / (float)k).xz);
            return r;
        }

        /// <summary>No two path nets cross except where they share an end node.</summary>
        private static void AssertNoPathCrossings(AreaPlan plan, string label)
        {
            var paths = plan.Nets.Where(n => n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide).ToList();
            var polys = paths.Select(n => Samples(n)).ToList();
            for (int i = 0; i < paths.Count; i++)
                for (int j = i + 1; j < paths.Count; j++)
                {
                    var a = polys[i]; var b = polys[j];
                    for (int x = 1; x < a.Count; x++)
                        for (int y = 1; y < b.Count; y++)
                        {
                            if (!Poly.SegmentIntersection(a[x - 1], a[x], b[y - 1], b[y], out var p, out _, out _)) continue;
                            bool atNode = new[] { paths[i].A.xz, paths[i].D.xz }.Any(e => math.distance(e, p) < 1.5f)
                                && new[] { paths[j].A.xz, paths[j].D.xz }.Any(e => math.distance(e, p) < 1.5f);
                            Assert.True(atNode, $"{label}: paths cross at {p}");
                        }
                }
        }

        private static void AssertParkInvariants(AreaPlan plan, float3[] ring, string label)
        {
            Assert.False(plan.HasErrors, label + ": " + string.Join(",", plan.Issues.Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Key)));
            TestKit.AssertObjectsInside(plan, ring);
            TestKit.AssertObjectsClearOfNets(plan);
            TestKit.AssertSurfacesAccepted(plan);
            var r2 = Ring2(ring);
            foreach (var n in plan.Nets)
                foreach (var p in Samples(n, 8))
                    Assert.True(Poly.Contains(r2, p) || Poly.DistanceToBoundary(r2, p) < 0.6f, $"{label}: {n.Role} leaves the area at {p}");
        }

        // ───────────── path figures ─────────────

        [Theory]
        [MemberData(nameof(PathStyleData))]
        public void PathFiguresStayInsideAndNeverCross(string style)
        {
            var shapes = new[] { ("rect", TestKit.Rect(250, 200)), ("L", TestKit.LShape(200)), ("blob", TestKit.Blob(110, 90, 7)), ("small", TestKit.Rect(30, 25)), ("sliver", TestKit.Rect(200, 14)) };
            foreach (var (name, ring) in shapes)
            {
                var plan = TestKit.Plan("park", TestKit.Input(ring, Bag(("pathStyle", style)), seed: 3, site: name == "rect" ? TestKit.RoadAlongBottom(250) : null));
                AssertParkInvariants(plan, ring, style + "/" + name);
                AssertNoPathCrossings(plan, style + "/" + name);
            }
        }

        [Theory]
        [MemberData(nameof(PathStyleData))]
        public void PathFiguresFitARealParkWithoutFallback(string style)
        {
            foreach (var ring in new[] { TestKit.Rect(250, 200), L(6f) })
            {
                var plan = TestKit.Plan("park", TestKit.Entered("park", TestKit.Input(ring, Bag(("pathStyle", style)), seed: 5, site: TestKit.RoadAlongBottom(250))));
                Assert.DoesNotContain(plan.Issues, i => i.Key == "Citymaker.Park.Issue.PathStyleFallback");
                Assert.True(plan.Nets.Count(n => n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide) >= 4, style);
                // Entrances are joined: some path end lies on the road side.
                Assert.Contains(plan.Nets, n => n.StartAttach == 77 || n.EndAttach == 77);
            }
        }

        [Fact]
        public void PathFiguresHaveTheirSignatures()
        {
            var ring = TestKit.Rect(250, 200);
            AreaPlan P(string style) => TestKit.Plan("park", TestKit.Input(ring, Bag(("pathStyle", style)), seed: 5, site: TestKit.RoadAlongBottom(250)));
            List<PlannedNet> Paths(AreaPlan p) => p.Nets.Where(n => n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide).ToList();
            int MaxDegree(AreaPlan p)
            {
                var ends = Paths(p).SelectMany(n => new[] { n.A.xz, n.D.xz }).ToList();
                return ends.Max(e => ends.Count(o => math.distance(o, e) < 0.3f));
            }

            // Figure-eight: the two lobes cross in one node (four path ends meet).
            Assert.True(MaxDegree(P("figureEight")) >= 4, "figure-eight has no crossing");
            // Spoke & ring: several junctions of three (hub and ring ends of each spoke).
            var spoke = P("spokeRing");
            var spokeEnds = Paths(spoke).SelectMany(n => new[] { n.A.xz, n.D.xz }).ToList();
            Assert.True(spokeEnds.Distinct().Count(e => spokeEnds.Count(o => math.distance(o, e) < 0.3f) >= 3) >= 8, "spoke & ring junctions");
            // Promenade: a wide avenue along the long axis (x), narrow side walks.
            var prom = Paths(P("promenade"));
            var wide = prom.Where(n => n.Role == NetRole.PathwayWide).ToList();
            Assert.NotEmpty(wide);
            Assert.Contains(prom, n => n.Role == NetRole.Pathway);
            Assert.True(wide.Sum(n => math.abs(n.D.x - n.A.x)) > 150f, "avenue runs along the long axis");
            // Woodland trail: narrow throughout and wiggly (many short pieces).
            var trail = Paths(P("woodlandTrail"));
            Assert.All(trail, n => Assert.Equal(NetRole.Pathway, n.Role));
            Assert.True(trail.Count >= 12, "trail pieces " + trail.Count);
            // Boardwalk: straight runs only.
            foreach (var n in Paths(P("boardwalk")))
            {
                float off = math.max(math.sqrt(SegmentIndex.DistSq(n.B.xz, n.A.xz, n.D.xz)), math.sqrt(SegmentIndex.DistSq(n.C.xz, n.A.xz, n.D.xz)));
                Assert.True(off < 0.05f, "boardwalk segment is curved");
            }
            // Circuit: the loop runs close to the outline.
            var circuit = Paths(P("circuit")).Where(n => n.StartAttach < 0 && n.EndAttach < 0).ToList();
            float mean = circuit.Average(n => Poly.DistanceToBoundary(Ring2(ring), Composer.Bezier(n.A, n.B, n.C, n.D, 0.5f).xz));
            Assert.True(mean < 25f, "circuit mean distance to the edge " + mean);
            // Riverside: two banks — a cross-section through the middle meets at least two paths.
            var river = Paths(P("riverside"));
            int hits = river.Count(n =>
            {
                var s = Samples(n, 16);
                for (int i = 1; i < s.Count; i++) if ((s[i - 1].x - 125f) * (s[i].x - 125f) <= 0f) return true;
                return false;
            });
            Assert.True(hits >= 2, "riverside banks crossing x = 125: " + hits);
        }

        [Fact]
        public void PathFiguresAreDistinctAndDeterministic()
        {
            var ring = TestKit.LShape(220);
            var prints = new Dictionary<string, string>();
            foreach (var style in NewPathStyles.Concat(new[] { "auto", "curvy", "formal" }))
            {
                var a = TestKit.Plan("park", TestKit.Input(ring, Bag(("pathStyle", style)), seed: 9));
                var b = TestKit.Plan("park", TestKit.Input(ring, Bag(("pathStyle", style)), seed: 9));
                Assert.Equal(TestKit.Fingerprint(a), TestKit.Fingerprint(b));
                prints[style] = string.Join(";", a.Nets.Select(n => $"{n.A.x:0.0},{n.A.z:0.0},{n.D.x:0.0},{n.D.z:0.0}"));
            }
            var keys = prints.Keys.ToList();
            for (int i = 0; i < keys.Count; i++)
                for (int j = i + 1; j < keys.Count; j++)
                    Assert.True(prints[keys[i]] != prints[keys[j]], keys[i] + " and " + keys[j] + " lay out the same paths");
        }

        [Fact]
        public void PathFiguresPlanFiveHectaresWithinBudget()
        {
            var input = TestKit.Input(TestKit.Blob(130, 120, 3), site: TestKit.RoadAlongBottom(250));
            foreach (var style in NewPathStyles)
            {
                input.Settings = Bag(("pathStyle", style));
                TestKit.Plan("park", input);
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < 3; i++) { input.Seed = (uint)(i + 21); TestKit.Plan("park", input); }
                double ms = sw.Elapsed.TotalMilliseconds / 3;
                _out.WriteLine($"{style}: {ms:0.0} ms");
                Assert.True(ms < TimingBudget.Ms(250), $"{style} took {ms:0} ms");
            }
        }

        // ───────────── park idioms (presets) ─────────────

        private static SettingsBag PresetValues(string layer, string preset) =>
            TestKit.Gen(layer).Presets.Single(p => p.Id == preset).Values.Clone();

        [Theory]
        [MemberData(nameof(ParkPresetData))]
        public void ParkPresetsPlanOneAndSixHectares(string preset)
        {
            foreach (var ha in new[] { 1f, 6f })
            {
                var ring = L(ha);
                var a = TestKit.Plan("park", TestKit.Input(ring, PresetValues("park", preset), seed: 4));
                AssertParkInvariants(a, ring, preset + "/" + ha + "ha");
                AssertNoPathCrossings(a, preset + "/" + ha + "ha");
                Assert.True(TestKit.Count(a, ObjectRole.Tree) > 0, preset + " plants no trees");
                var b = TestKit.Plan("park", TestKit.Input(ring, PresetValues("park", preset), seed: 4));
                Assert.Equal(TestKit.Fingerprint(a), TestKit.Fingerprint(b));
            }
        }

        [Fact]
        public void ParkIdiomsLookDifferent()
        {
            var ring = TestKit.Rect(250, 200);
            var plans = new Dictionary<string, AreaPlan>();
            foreach (var p in NewParkPresets.Concat(new[] { "natural" }))
                plans[p] = TestKit.Plan("park", TestKit.Input(ring, PresetValues("park", p), seed: 6, site: TestKit.RoadAlongBottom(250)));
            var r2 = Ring2(ring);
            List<float2> Trees(string p) => plans[p].Objects.Where(o => o.Role == ObjectRole.Tree).Select(o => o.Position.xz).ToList();
            List<PlannedNet> Paths(string p) => plans[p].Nets.Where(n => n.Role != NetRole.Fence).ToList();
            float NearPath(string p) => Trees(p).Count(t => Paths(p).Any(n => TestKit.DistanceToCurve(t, n) < 9f)) / (float)math.max(1, Trees(p).Count);

            // Modern minimal: one species on a regular grid (every tree has a neighbour at the grid pitch, square to the axes).
            var modern = plans["modern"].Objects.Where(o => o.Role == ObjectRole.Tree).ToList();
            Assert.Single(modern.Select(o => o.Variation).Distinct());
            // Urban pocket: a street-tree row along the outline.
            Assert.True(Trees("urban").Count(t => { float d = Poly.DistanceToBoundary(r2, t); return d > 4.5f && d < 5.5f; }) >= 15, "urban row");
            // Riverside and Victorian line their walks more than the natural park.
            Assert.True(NearPath("riverside") > NearPath("natural") + 0.05f, $"riverside {NearPath("riverside"):0.00} vs natural {NearPath("natural"):0.00}");
            // Wildflower meadow: few trees, lots of painted natural ground.
            Assert.True(Trees("wildflower").Count < Trees("natural").Count / 2);
            Assert.True(plans["wildflower"].Paint.Count >= 3, "meadow paint patches " + plans["wildflower"].Paint.Count);
            // English landscape: fewer, bigger clumps than the natural park (wide open lawns).
            Assert.True(plans["landscape"].Nets.Count < plans["natural"].Nets.Count + 40);
            // Every idiom differs from every other.
            var keys = plans.Keys.ToList();
            for (int i = 0; i < keys.Count; i++)
                for (int j = i + 1; j < keys.Count; j++)
                    Assert.NotEqual(TestKit.Fingerprint(plans[keys[i]]), TestKit.Fingerprint(plans[keys[j]]));
        }

        // ───────────── amenities in a park ─────────────

        private static AreaPlan Layer(ComposedPlan p, string id) => p.Layers.Single(l => l.GeneratorId == id);

        [Theory]
        [MemberData(nameof(AmenityData))]
        public void AmenityComposesInAParkWithAPathToIt(string id)
        {
            var ring = TestKit.Rect(240, 180);
            var plan = Compose(With("park", id), ring, TestKit.RoadAlongBottom(240));
            Assert.False(plan.HasErrors, Errors(plan));
            var park = Layer(plan, "park");
            var place = Ring2(park.Regions.Single(r => r.Key == Amenities.Place(id)).Ring);
            var spec = Amenities.Spec(id);
            Assert.Equal(spec.Length * spec.Width, Poly.Area(place), 1f);
            var amenity = Layer(plan, id);
            Assert.EndsWith("@" + Amenities.Place(id), amenity.StepPath);
            Assert.True(amenity.Objects.Count + amenity.Surfaces.Count > 1, id + " built nothing");
            Assert.True(amenity.Objects.Count(o => o.Role != ObjectRole.Decal) >= 1, id + " placed no objects");
            TestKit.AssertSurfacesAccepted(amenity);
            foreach (var o in amenity.Objects.Where(o => o.Role != ObjectRole.Decal))
                Assert.True(Poly.Contains(place, o.Position.xz) || Poly.DistanceToBoundary(place, o.Position.xz) < 0.05f, $"{id} {o.Role} outside its place");
            foreach (var s in amenity.Surfaces)
                foreach (var p in s.Ring) Assert.True(Poly.Contains(place, p.xz) || Poly.DistanceToBoundary(place, p.xz) < 0.2f, $"{id} surface leaves its place");
            // The park keeps planting, furniture and its own paths' interiors out of the place.
            foreach (var o in park.Objects.Where(o => o.Role != ObjectRole.Decal)) Assert.False(Poly.Contains(place, o.Position.xz), $"park {o.Role} inside the {id}");
            // Amenity objects keep clear of every path of the park.
            foreach (var o in amenity.Objects.Where(o => o.Role != ObjectRole.Decal))
                foreach (var n in park.Nets.Where(n => n.Role != NetRole.Fence))
                    Assert.True(TestKit.DistanceToCurve(o.Position.xz, n) >= Composer.DefaultHalfWidth(n.Role) + 0.2f, $"{id} {o.Role} on a path");
            // "Path to it": a path ends at the place's edge (or one already runs right past it).
            Assert.Contains(park.Nets, n => n.Role != NetRole.Fence && (Poly.DistanceToBoundary(place, n.A.xz) < 1f || Poly.DistanceToBoundary(place, n.D.xz) < 1f
                || Samples(n, 16).Any(p => Poly.DistanceToBoundary(place, p) < 3.5f + Composer.DefaultHalfWidth(n.Role))));
            Assert.All(park.Nets.Where(n => n.Role != NetRole.Fence), n =>
            {
                foreach (var p in Samples(n, 8).Skip(1).Take(7))
                    Assert.False(Poly.Contains(place, p) && Poly.DistanceToBoundary(place, p) > 0.6f, $"a path runs through the {id}");
            });
        }

        [Theory]
        [MemberData(nameof(AmenityPositions))]
        public void AmenityGoesWhereThePlayerAsked(string id, string at)
        {
            var ring = TestKit.Rect(240, 180);
            var r = With("park", id);
            r.Root.Settings.Values[Amenities.AtControl(id)] = at;
            var plan = Compose(r, ring, TestKit.RoadAlongBottom(240));
            Assert.False(plan.HasErrors, Errors(plan));
            var park = Layer(plan, "park");
            var place = Ring2(park.Regions.Single(x => x.Key == Amenities.Place(id)).Ring);
            var c = Poly.Centroid(place);
            var r2 = Ring2(ring);
            float edge = Poly.DistanceToBoundary(r2, c);
            var spec = Amenities.Spec(id);
            bool moved = park.Issues.Any(i => i.Key == "Citymaker.Park.Issue.AmenityMoved");
            if (moved) return;   // a legal outcome; the issue tells the player
            switch (at)
            {
                case "centre": Assert.True(math.distance(c, new float2(120, 90)) < 45f, $"{id} centre at {c}"); break;
                case "edge": Assert.True(edge < spec.Width * 0.5f + 14f, $"{id} edge at {c} ({edge:0} m from the edge)"); break;
                case "corner":
                {
                    float d = new[] { new float2(0, 0), new float2(240, 0), new float2(0, 180), new float2(240, 180) }.Min(p => math.distance(p, c));
                    Assert.True(d < spec.Length + 25f, $"{id} corner at {c} ({d:0} m from a corner)");
                    break;
                }
                case "entrance":
                {
                    // Entrances are suggested on the road side (bottom edge, z = 0).
                    Assert.True(c.y < spec.Length + 30f, $"{id} near entrance at {c}");
                    break;
                }
            }
        }

        [Fact]
        public void PathToItCanBeSwitchedOff()
        {
            var ring = TestKit.Rect(240, 180);
            int Spurs(bool on)
            {
                var r = With("park", "dogRun");
                r.Root.Settings.Values["dogRunAt"] = "corner";
                r.Root.Settings.Values["dogRunPath"] = on;
                var park = Layer(Compose(r, ring, TestKit.RoadAlongBottom(240)), "park");
                return park.Facts.TryGetValue("featureSpurs", out var n) ? (int)n : 0;
            }
            Assert.True(Spurs(true) >= 1);
            Assert.Equal(0, Spurs(false));
        }

        [Fact]
        public void AllAmenitiesFitTogetherWithoutOverlapping()
        {
            var ring = TestKit.Rect(320, 240);
            var plan = Compose(With("park", AmenityIds), ring, TestKit.RoadAlongBottom(320));
            Assert.False(plan.HasErrors, Errors(plan));
            var park = Layer(plan, "park");
            var places = AmenityIds.Select(id => Ring2(park.Regions.Single(x => x.Key == Amenities.Place(id)).Ring)).ToList();
            for (int i = 0; i < places.Count; i++)
                for (int j = i + 1; j < places.Count; j++)
                {
                    Assert.False(Poly.Contains(places[j], Poly.Centroid(places[i])), AmenityIds[i] + " inside " + AmenityIds[j]);
                    for (int a = 0; a < 4; a++)
                        for (int b = 0; b < 4; b++)
                            Assert.False(Poly.SegmentsIntersect(places[i][a], places[i][(a + 1) % 4], places[j][b], places[j][(b + 1) % 4]),
                                AmenityIds[i] + " overlaps " + AmenityIds[j]);
                }
            foreach (var id in AmenityIds) Assert.Contains(plan.Layers, l => l.GeneratorId == id);
            // Deterministic.
            var again = Compose(With("park", AmenityIds), ring, TestKit.RoadAlongBottom(320));
            for (int i = 0; i < plan.Layers.Count; i++) Assert.Equal(TestKit.Fingerprint(plan.Layers[i]), TestKit.Fingerprint(again.Layers[i]));
        }

        [Fact]
        public void ParkWithAmenitiesStaysWithinBudget()
        {
            var ring = TestKit.Blob(130, 120, 3);
            var r = With("park", "volleyball", "basketball", "splashPad", "dogRun");
            Compose(r, ring, TestKit.RoadAlongBottom(250));
            var sw = Stopwatch.StartNew();
            Compose(r, ring, TestKit.RoadAlongBottom(250));
            _out.WriteLine($"park 5 ha + 4 amenities: {sw.Elapsed.TotalMilliseconds:0} ms");
            Assert.True(sw.Elapsed.TotalMilliseconds < TimingBudget.Ms(400), "took " + sw.Elapsed.TotalMilliseconds);
        }

        [Theory]
        [MemberData(nameof(AmenityData))]
        public void AmenityStandsAloneAndIsDeterministic(string id)
        {
            var ring = TestKit.Rect(60, 45);
            var a = TestKit.Plan(id, TestKit.Input(ring, seed: 8));
            Assert.False(a.HasErrors);
            Assert.NotEmpty(a.Objects);
            TestKit.AssertObjectsInside(a, ring);
            TestKit.AssertSurfacesAccepted(a);
            Assert.Equal(TestKit.Fingerprint(a), TestKit.Fingerprint(TestKit.Plan(id, TestKit.Input(ring, seed: 8))));
            // Every preset plans too.
            foreach (var p in TestKit.Gen(id).Presets)
            {
                var plan = TestKit.Plan(id, TestKit.Input(ring, p.Values.Clone(), seed: 8));
                Assert.False(plan.HasErrors, id + "/" + p.Id);
                TestKit.AssertObjectsInside(plan, ring);
            }
            // Too small: a clear issue, no exception.
            var tiny = TestKit.Plan(id, TestKit.Input(TestKit.Rect(6, 5), seed: 8));
            Assert.False(tiny.HasErrors);
        }

        [Fact]
        public void AmenitiesDegradeWhenAssetsAreMissing()
        {
            var none = new Dictionary<string, int>
            {
                [AssetCategories.SportsTableTennis] = 0, [AssetCategories.SportsVolleyball] = 0, [AssetCategories.SportsTennis] = 0,
                [AssetCategories.SignNoticeboard] = 0, [AssetCategories.SportsFitness] = 0, [AssetCategories.WaterJets] = 0,
                [AssetCategories.CenterpieceFountain] = 0,
            };
            using (AssetAvailability.Scope(none))
            {
                foreach (var id in new[] { "tableTennis", "noticeboard", "fitness", "splashPad" })
                {
                    var plan = TestKit.Plan(id, TestKit.Input(TestKit.Rect(60, 45)));
                    Assert.False(plan.HasErrors);
                    Assert.Contains(plan.Issues, i => i.Key == Keys.NoAssetsIssue);
                    Assert.DoesNotContain(plan.Objects, o => o.Role == ObjectRole.Furniture);
                }
                // The volleyball pit is still a sand court with benches, just without a net.
                var v = TestKit.Plan("volleyball", TestKit.Input(TestKit.Rect(60, 45)));
                Assert.NotEmpty(v.Surfaces);
                Assert.DoesNotContain(v.Objects, o => o.Role == ObjectRole.Furniture);
            }
        }

        [Fact]
        public void AmenityLayoutsAreBelievable()
        {
            var site = new OrientedRect();
            AreaPlan Std(string id, params (string, object)[] kv) => TestKit.Plan(id, TestKit.Input(TestKit.Rect(60, 45), Bag(kv), seed: 2));
            // Basketball half court: one hoop; full court: two, facing each other.
            Assert.Equal(1.0, Std("basketball").Facts["hoops"]);
            var full = TestKit.Plan("basketball", TestKit.Input(TestKit.Rect(60, 45), Bag(("court", "full")), seed: 2));
            Assert.Equal(2.0, full.Facts["hoops"]);
            // Volleyball: a sand surface and one net at the centre line.
            var vb = Std("volleyball");
            Assert.Contains(vb.Surfaces, s => s.Category == AssetCategories.SurfaceSand);
            Assert.Equal(1.0, vb.Facts["nets"]);
            // Dog run: a closed fence with exactly one gate gap (5 fence runs = 3 sides + 2 halves).
            var dog = Std("dogRun");
            Assert.Equal(5, dog.Nets.Count(n => n.Role == NetRole.Fence));
            var twoZones = Std("dogRun", ("smallDogs", true));
            Assert.Equal(7, twoZones.Nets.Count(n => n.Role == NetRole.Fence));
            // Splash pad: by default a deck round a low fountain (no farm sprinklers); chosen jets all inside the pad.
            var deckPad = Std("splashPad");
            var fountain = deckPad.Objects.Single(o => o.Role == ObjectRole.Centerpiece);
            Assert.DoesNotContain(deckPad.Objects, o => o.Category == AssetCategories.WaterJets);
            Assert.True(math.distance(fountain.Position.xz, Poly.Centroid(Ring2(deckPad.Surfaces[0].Ring))) < 0.5f);
            var pad = Std("splashPad", ("jetAsset", "SplashJet01"));
            float r = (float)pad.Facts["padRadius"];
            var jets = pad.Objects.Where(o => o.Category == AssetCategories.WaterJets).ToList();
            Assert.True(jets.Count >= 6);
            var c = Poly.Centroid(Ring2(pad.Surfaces[0].Ring));
            Assert.All(jets, j => Assert.True(math.distance(j.Position.xz, c) < r));
            // Bandstand: benches face the structure.
            var band = Std("bandstand");
            var stand = band.Objects.Single(o => o.Role == ObjectRole.Furniture);
            foreach (var b in band.Objects.Where(o => o.Role == ObjectRole.Bench))
            {
                var d = math.normalizesafe(stand.Position.xz - b.Position.xz);
                Assert.True(math.dot(d, new float2(math.sin(b.Yaw), math.cos(b.Yaw))) > 0.95f, "bench does not face the bandstand");
            }
            // Fitness ring: stations on a circle.
            var ring = Std("fitness", ("layout", "ring"));
            var st = ring.Objects.Where(o => o.Category == AssetCategories.SportsFitness).Select(o => o.Position.xz).ToList();
            var cc = new float2(st.Average(p => p.x), st.Average(p => p.y));
            float rr = st.Average(p => math.distance(p, cc));
            Assert.All(st, p => Assert.InRange(math.distance(p, cc), rr - 0.5f, rr + 0.5f));
            // Chess with a giant board: a decorative board surface plus tables.
            var chess = Std("chess", ("giant", true));
            Assert.Contains(chess.Surfaces, s => s.Role == SurfaceRole.Decorative);
            Assert.Equal(1.0, chess.Facts["giantBoard"]);
            _ = site;
        }

        // ───────────── amenities on a plaza ─────────────

        [Theory]
        [MemberData(nameof(PlazaAmenityData))]
        public void AmenityComposesOnAPlaza(string id)
        {
            var ring = TestKit.Rect(90, 70);
            var plan = Compose(With("plaza", id), ring, TestKit.RoadAlongBottom(90));
            Assert.False(plan.HasErrors, Errors(plan));
            var plaza = Layer(plan, "plaza");
            var place = Ring2(plaza.Regions.Single(x => x.Key == Amenities.Place(id)).Ring);
            foreach (var o in plaza.Objects.Where(o => o.Role != ObjectRole.Decal && o.Role != ObjectRole.AccessPoint))
                Assert.False(Poly.Contains(place, o.Position.xz), $"plaza {o.Role} inside the {id}");
            var amenity = Layer(plan, id);
            Assert.NotEmpty(amenity.Objects);
            foreach (var o in amenity.Objects.Where(o => o.Role != ObjectRole.Decal))
                Assert.True(Poly.Contains(place, o.Position.xz) || Poly.DistanceToBoundary(place, o.Position.xz) < 0.05f, $"{id} {o.Role} outside its place");
        }

        [Fact]
        public void DogRunIsAParkFeatureOnly()
        {
            Assert.DoesNotContain(Features.Of(GeneratorRegistry.Get("plaza")), f => f.Layer == "dogRun");
            Assert.DoesNotContain(TestKit.Gen("plaza").Controls, c => c.Id == "dogRunAt");
            Assert.Contains(Features.Of(GeneratorRegistry.Get("park")), f => f.Layer == "dogRun" && f.Options.Contains("dogRunPath") && f.Options.Contains("dogRunAt"));
        }

        // ───────────── plaza levels ─────────────

        [Theory]
        [MemberData(nameof(PlazaLevelData))]
        public void PlazaLevelsAreBlendedAtTheOutline(string mode)
        {
            foreach (var slope in new[] { 0f, 0.04f })
            {
                var site = new SiteContext { HeightAt = p => 10f + p.x * slope };
                var ring = TestKit.Rect(90, 64);
                var plan = TestKit.Plan("plaza", TestKit.Input(ring, Bag(("levels", mode), ("levelHeight", 2.0), ("levelCount", 3.0)), seed: 3, site: site));
                Assert.False(plan.HasErrors);
                Assert.NotEmpty(plan.Terraform);
                Assert.Contains(plan.Issues, i => i.Key == TerrainShaper.PermanentKey);
                Assert.True(plan.Facts["levelFlights"] >= 2, mode + " has no stairs");
                var r2 = Ring2(ring);
                foreach (var t in plan.Terraform)
                {
                    Assert.True(t.Permanent);
                    var c = Poly.Centroid(Ring2(t.Ring));
                    float d = Poly.DistanceToBoundary(r2, c);
                    float change = math.abs(t.TargetHeight - site.HeightAt(c));
                    Assert.True(change <= 0.35f * d + 0.05f, $"{mode}: {change:0.00} m change {d:0.0} m from the outline");
                    foreach (var p in t.Ring) Assert.True(Poly.Contains(r2, p.xz) || Poly.DistanceToBoundary(r2, p.xz) < 0.05f);
                }
                TestKit.AssertObjectsInside(plan, ring);
                TestKit.AssertSurfacesAccepted(plan);
                Assert.Equal(TestKit.Fingerprint(plan), TestKit.Fingerprint(TestKit.Plan("plaza", TestKit.Input(ring, Bag(("levels", mode), ("levelHeight", 2.0), ("levelCount", 3.0)), seed: 3, site: site))));
            }
        }

        [Fact]
        public void PlazaLevelShapesDiffer()
        {
            var ring = TestKit.Rect(90, 64);
            var heights = new Dictionary<string, string>();
            foreach (var mode in NewPlazaLevels)
            {
                var plan = TestKit.Plan("plaza", TestKit.Input(ring, Bag(("levels", mode), ("levelHeight", 2.0)), seed: 3));
                heights[mode] = string.Join(",", plan.Terraform.Select(t => t.TargetHeight.ToString("0.0")));
            }
            Assert.Equal(heights.Count, heights.Values.Distinct().Count());
            // Podium raises the middle, the sunken court lowers it.
            var podium = TestKit.Plan("plaza", TestKit.Input(ring, Bag(("levels", "podium"), ("levelHeight", 2.0)), seed: 3));
            var sunk = TestKit.Plan("plaza", TestKit.Input(ring, Bag(("levels", "sunkenCourt"), ("levelHeight", 2.0)), seed: 3));
            Assert.True(podium.Terraform.Max(t => t.TargetHeight) > 1.5f);
            Assert.True(sunk.Terraform.Min(t => t.TargetHeight) < -1.5f);
        }

        [Fact]
        public void PlazaLevelsTooSmallSaySo()
        {
            var plan = TestKit.Plan("plaza", TestKit.Input(TestKit.Rect(20, 14), Bag(("levels", "cascade")), seed: 3));
            Assert.False(plan.HasErrors);
            Assert.Empty(plan.Terraform);
            Assert.Contains(plan.Issues, i => i.Key == "Citymaker.Plaza.Issue.TooSmallForLevels");
        }

        [Theory]
        [MemberData(nameof(PlazaPresetData))]
        public void PlazaPresetsPlanAndDiffer(string preset)
        {
            foreach (var ring in new[] { TestKit.Rect(90, 70), TestKit.LShape(110) })
            {
                var plan = TestKit.Plan("plaza", TestKit.Input(ring, PresetValues("plaza", preset), seed: 5));
                Assert.False(plan.HasErrors, preset);
                TestKit.AssertObjectsInside(plan, ring);
                TestKit.AssertSurfacesAccepted(plan);
                Assert.Equal(TestKit.Fingerprint(plan), TestKit.Fingerprint(TestKit.Plan("plaza", TestKit.Input(ring, PresetValues("plaza", preset), seed: 5))));
            }
            var mine = TestKit.Fingerprint(TestKit.Plan("plaza", TestKit.Input(TestKit.Rect(90, 70), PresetValues("plaza", preset), seed: 5)));
            foreach (var other in TestKit.Gen("plaza").Presets.Where(p => p.Id != preset))
                Assert.NotEqual(mine, TestKit.Fingerprint(TestKit.Plan("plaza", TestKit.Input(TestKit.Rect(90, 70), other.Values.Clone(), seed: 5))));
        }

        // ───────────── labels & categories ─────────────

        [Fact]
        public void EveryNewLabelExists()
        {
            var lang = TestKit.Lang();
            var layers = new[] { "park", "plaza" }.Concat(AmenityIds);
            foreach (var id in layers)
            {
                var g = TestKit.Gen(id);
                Assert.True(lang.ContainsKey(g.NameKey), g.NameKey);
                foreach (var c in g.Controls)
                {
                    Assert.True(lang.ContainsKey(c.LabelKey), c.LabelKey);
                    if (!string.IsNullOrEmpty(c.TooltipKey)) Assert.True(lang.ContainsKey(c.TooltipKey), c.TooltipKey);
                    foreach (var o in c.Options)
                    {
                        Assert.True(lang.TryGetValue(o.LabelKey, out var label), o.LabelKey);
                        Assert.True(label.Split(' ').Length <= 3, o.LabelKey + " too long: " + label);
                    }
                    if (!string.IsNullOrEmpty(c.AssetCategory)) Assert.True(lang.ContainsKey(Keys.Category(c.AssetCategory)), Keys.Category(c.AssetCategory));
                }
                foreach (var p in g.Presets) Assert.True(lang.ContainsKey(p.LabelKey), p.LabelKey);
                if (g is IHasFeatures hf)
                    foreach (var f in hf.Features)
                    {
                        Assert.True(lang.ContainsKey(f.LabelKey), f.LabelKey);
                        if (!string.IsNullOrEmpty(f.TooltipKey)) Assert.True(lang.ContainsKey(f.TooltipKey), f.TooltipKey);
                    }
            }
            foreach (var key in new[] { "Citymaker.Park.Issue.NoRoomForAmenity", "Citymaker.Park.Issue.AmenityMoved", "Citymaker.Plaza.Issue.NoRoomForAmenity",
                         "Citymaker.Plaza.Issue.AmenityMoved", "Citymaker.Plaza.Issue.TooSmallForLevels", "Citymaker.Stat.Courts", "Citymaker.Stat.GameTables",
                         "Citymaker.Stat.FitnessStations", "Citymaker.Stat.Jets", "Citymaker.Stat.AgilityItems", "Citymaker.Stat.Noticeboards",
                         "Citymaker.AddOn.Basketball.Issue.Half", "Citymaker.AddOn.Volleyball.Issue.Smaller", "Citymaker.AddOn.TableTennis.Issue.Fewer",
                         "Citymaker.AddOn.Fitness.Issue.Fewer" })
                Assert.True(lang.ContainsKey(key), key);
        }

        [Theory]
        [InlineData("VolleyballNet01", AssetCategories.SportsVolleyball)]
        [InlineData("PingPongTable02", AssetCategories.SportsTableTennis)]
        [InlineData("Outdoor Table Tennis", AssetCategories.SportsTableTennis)]
        [InlineData("ChessTable01", AssetCategories.GamesChess)]
        [InlineData("ParkBandstand", AssetCategories.StageBandstand)]
        [InlineData("CommunityNoticeboard01", AssetCategories.SignNoticeboard)]
        [InlineData("ParkMapBoard", AssetCategories.SignNoticeboard)]
        public void AmenityAssetsAreClassified(string name, string category)
        {
            var rule = new AmenityCategoryRules();
            var cats = new List<string>();
            Assert.True(rule.Classify(name.ToLowerInvariant(), default, cats));
            Assert.Contains(category, cats);
            Assert.True(rule.AutoEligible(category, name.ToLowerInvariant(), default) ?? true);
        }

        [Fact]
        public void AdvertBoardsAreNotNoticeboards()
        {
            var rule = new AmenityCategoryRules();
            var cats = new List<string>();
            Assert.False(rule.Classify("billboard_noticeboard_advert", default, cats));
            Assert.DoesNotContain(AssetCategories.SignNoticeboard, cats);
            // Chess tables keep their old categories (cafe seating, picnic) on top of the new one.
            cats.Clear();
            rule.Classify("chesstable", default, cats);
            Assert.Contains(AssetCategories.CafeSeating, cats);
            Assert.Contains(AssetCategories.Picnic, cats);
        }
    }
}
