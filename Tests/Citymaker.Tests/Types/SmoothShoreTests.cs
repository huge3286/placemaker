// s30: smooth, glitch-free waterfronts (playtests 54–57). The promenade is ONE smooth curve at ONE smooth height, the
// edge ONE continuous run, the ground one continuous field. Measured on every waterfront and beach preset over every shore
// site (flat, bank, headland, cliff, curved lake bay with a jagged waterline):
//   zig-zag  — the walk's turning direction never flips on sharp bends (Wiggles = 0), radius ≥ 40 m, grade ≤ 2 % along
//   curtain  — no texel pair of the CS2 heightmap (3.5 m) made steeper than 50 % by the build (a vertical sheet in game)
//   seat     — terrain-following paths sit on ground level across their width (a net flattens its width: a cross slope
//              leaves a lip along its edge), quays have their land side at deck level
//   edge     — one quay / boardwalk run per stretch of shore, one railing chain per run of drop

using System.Collections.Generic;
using System.Linq;
using Citymaker.Core;
using Citymaker.Core.Build;
using Citymaker.Core.Geometry;
using Citymaker.Core.Layers.Common;
using Citymaker.Core.Layers.Types.Beach;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Citymaker.Tests.Types
{
    [Collection(Citymaker.Tests.TimingCollection.Name)]
    public class SmoothShoreTests
    {
        private readonly ITestOutputHelper _out;
        public SmoothShoreTests(ITestOutputHelper output) { _out = output; }

        public static IEnumerable<object[]> Cases()
        {
            foreach (var layer in new[] { "beach", "waterfront" })
                foreach (var p in TestKit.Gen(layer).Presets)
                    foreach (var kind in ShoreSites.Kinds)
                        yield return new object[] { layer, p.Id, kind };
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void ShoreIsSmoothAndGlitchFree(string layer, string preset, string kind)
        {
            var site = ShoreSites.Site(kind);
            var plan = ShoreSites.Plan(layer, preset, kind);
            Assert.False(plan.HasErrors, string.Join(",", plan.Issues.Select(i => i.Key)));
            string tag = $"{layer}/{preset}/{kind}";
            var f = plan.Facts;
            var original = site.HeightAt;
            var final = ShoreSites.Final(plan, original);

            // 1. Zig-zag: the promenade is one smooth curve, at one smooth height.
            if (f.GetValueOrDefault("walks") > 0)
            {
                Assert.True(f["walkWiggles"] == 0, $"{tag}: walk zig-zags ({f["walkWiggles"]} turns)");
                Assert.True(f["walkMinRadius"] >= ShoreWalk.MinCurveRadius - 0.5, $"{tag}: walk radius {f["walkMinRadius"]:0} m");
                Assert.True(f["walkMaxGrade"] <= 0.021, $"{tag}: walk grade {f["walkMaxGrade"]:P1}");
            }
            if (layer == "waterfront" && f.GetValueOrDefault("edgeRuns") > 0)
            {
                Assert.True(f["edgeMinRadius"] >= ShoreWalk.MinCurveRadius - 0.5, $"{tag}: edge radius {f["edgeMinRadius"]:0} m");
                Assert.True(f["edgeWiggles"] == 0, $"{tag}: edge zig-zags");
                // One continuous edge per stretch of shore that is not cliff (the sites have at most one such stretch).
                Assert.True(f["edgeRuns"] <= 1, $"{tag}: {f["edgeRuns"]} edge runs");
            }

            // 2. Curtains: nothing the build made steeper than 50 % between two heightmap texels (off the nets, whose own cut
            //    or wall covers the ground under them). The headland's natural 26 % transition slope cut by an entrance
            //    ramp may leave a couple.
            //    Natural ground already steeper than 30 % (the headland's cliff transition) is not measured: any path cut into it has
            //    steep sides, and a cliff face is left natural anyway.
            var offNets = ShoreSites.OffNets(plan);
            System.Func<float2, bool> mask = p => offNets(p) && TerrainSurvey.Grade(original, p, 1.75f) <= 0.3f;
            var c = TerrainMetrics.Curtains(final, original, new float2(4f, -30f), new float2(246f, 186f), TerrainMetrics.Texel, mask);
            _out.WriteLine($"{tag}: {c}, cut {f.GetValueOrDefault("earthMaxCut"):0.0} fill {f.GetValueOrDefault("earthMaxFill"):0.0}");
            Assert.True(c.Count == 0, $"{tag}: {c.Count} terrain sheets (worst {c.Max:0.00} m at {c.At})");

            // 3. Seats: paths level across their width (away from the outline, where the ground may not change), quays on
            //    ground at deck level on their land side.
            var ring = ShoreSites.Ring();
            var outline = ring.Select(v => v.xz).ToArray();
            var nets = ShoreSites.NetTuples(plan).Where(n => Citymaker.Core.Geometry.Poly.DistanceToBoundary(outline, Composer.Bezier(n.a, n.b, n.c, n.d, 0.5f).xz) > 10f).ToList();
            var seat = TerrainMetrics.Seats(nets, final);
            _out.WriteLine($"{tag}: seat {seat}");
            if (kind != "headland" && kind != "cliff")
                Assert.True(seat.Cross <= 0.45f, $"{tag}: a path leans {seat.Cross:0.00} m across its width at {seat.At}");

            // 4. Railings: continuous chains (shared nodes), never a scatter of short pieces.
            var fences = plan.Nets.Where(n => n.Role == NetRole.Fence).ToList();
            if (fences.Count > 0 && layer == "waterfront")
            {
                int chains = Chains(fences);
                Assert.True(chains <= math.max(1, (int)f.GetValueOrDefault("railingRuns") + (int)f.GetValueOrDefault("lookouts") * 2 + 2), $"{tag}: railing in {chains} pieces");
            }

            // 5. No level patches (the old step flights and landings): the field is all sloped quads.
            if (layer == "waterfront") Assert.DoesNotContain(plan.Terraform, t => !t.Sloped);
        }

        private static int Chains(List<PlannedNet> nets)
        {
            var parent = Enumerable.Range(0, nets.Count).ToArray();
            int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
            for (int i = 0; i < nets.Count; i++)
                for (int j = i + 1; j < nets.Count; j++)
                {
                    var a = nets[i]; var b = nets[j];
                    if (math.distance(a.D.xz, b.A.xz) < 0.2f || math.distance(a.A.xz, b.D.xz) < 0.2f || math.distance(a.A.xz, b.A.xz) < 0.2f || math.distance(a.D.xz, b.D.xz) < 0.2f)
                        parent[Find(i)] = Find(j);
                }
            return Enumerable.Range(0, nets.Count).Select(Find).Distinct().Count();
        }

        // ───────────── the metrics themselves ─────────────

        [Fact]
        public void WigglesCountZigZagsNotMeanders()
        {
            var meander = Enumerable.Range(0, 126).Select(i => new float2(i * 2f, 20f * math.sin(i * 2f / 60f))).ToArray();
            var jagged = Enumerable.Range(0, 126).Select(i => new float2(i * 2f, 20f * math.sin(i * 2f / 60f) + 1.6f * math.sin(i * 2f / 4.1f) + 1.1f * math.sin(i * 2f / 2.3f))).ToArray();
            Assert.Equal(0, ShoreWalk.Wiggles(meander));
            Assert.True(ShoreWalk.MinRadius(meander) > 150f);
            Assert.True(ShoreWalk.Wiggles(jagged) >= 10, $"{ShoreWalk.Wiggles(jagged)}");
            Assert.True(ShoreWalk.MinRadius(jagged) < 40f);
            // Smoothing by arc length removes the zig-zag and keeps the meander.
            var smooth = ShoreWalk.SmoothLine(jagged, 12f, 3);
            Assert.Equal(0, ShoreWalk.Wiggles(smooth));
            Assert.True(ShoreWalk.MinRadius(smooth) >= 40f, $"{ShoreWalk.MinRadius(smooth):0}");
            Assert.True(smooth.Max(p => p.y) > 12f, "the meander stays");
        }

        [Fact]
        public void CurtainMetricFindsStepsNotSlopes()
        {
            System.Func<float2, float> flat = _ => 0f;
            System.Func<float2, float> slope = p => 0.3f * p.x;          // a smooth 30 % bank
            System.Func<float2, float> step = p => p.x > 50f ? 2.5f : 0f; // a 2.5 m terrace edge
            var min = new float2(0f, 0f); var max = new float2(100f, 40f);
            Assert.Equal(0, TerrainMetrics.Curtains(slope, flat, min, max).Count);
            Assert.True(TerrainMetrics.Curtains(step, flat, min, max).Count > 5);
            // A natural cliff is not ours.
            Assert.Equal(0, TerrainMetrics.Curtains(step, step, min, max).Count);
        }

        [Fact]
        public void SeatMetricFindsCrossSlopes()
        {
            System.Func<float2, float> cross = p => 0.25f * p.y;
            System.Func<float2, float> level = p => 0.02f * p.x;
            var net = (new float3(0, 0, 10), new float3(10, 0, 10), new float3(20, 0, 10), new float3(30, 0, 10), 2f, TerrainMetrics.SeatFollow);
            Assert.True(TerrainMetrics.Seats(new[] { net }, cross).Cross > 0.9f);
            Assert.True(TerrainMetrics.Seats(new[] { net }, level).Cross < 0.01f);
        }

        [Fact]
        public void WalkFollowsTheShoreButNotItsWiggles()
        {
            // The curved bay: a promenade that stays a smooth large-radius curve set back from the jagged waterline, on land.
            foreach (var preset in new[] { "promenade", "pier", "harbor", "riverSteps", "fishing" })
            {
                var plan = ShoreSites.Plan("waterfront", preset, "curved");
                var walk = plan.Nets.Where(n => (n.Role == NetRole.PathwayWide || n.Role == NetRole.Pathway) && n.Category == null).ToList();
                Assert.NotEmpty(walk);
                Assert.True(plan.Facts["walkLength"] > 120f, $"{preset}: walk only {plan.Facts["walkLength"]:0} m");
                foreach (var n in walk.Where(n => math.distance(n.A.xz, n.D.xz) > 20f && math.abs(math.normalizesafe(n.D.xz - n.A.xz).x) > 0.7f))
                    foreach (var p in ShoreSites.Samples(n, 8))
                        Assert.True(p.y > ShoreSites.CurvedCoast(p.x) + 2f, $"{preset}: walk at {p} is on or past the water");
            }
        }
    }
}

namespace Citymaker.Tests.Types
{
    /// <summary>(s30) The in-game terrain dump format: what the game writes, the gallery reads.</summary>
    public class TerrainDumpTests
    {
        public static TerrainDump FromPlan(AreaPlan plan, SiteContext site)
        {
            var final = ShoreSites.Final(plan, site.HeightAt);
            var d = new TerrainDump { Origin = new float2(-20f, -40f), Cell = 2f, Nx = 146, Ny = 131, Group = "7", When = "test" };
            d.Before = new float[d.Count]; d.After = new float[d.Count]; d.Water = new float[d.Count]; d.Planned = new float[d.Count];
            for (int j = 0; j < d.Ny; j++)
                for (int i = 0; i < d.Nx; i++)
                {
                    var p = d.Pos(i, j); int k = j * d.Nx + i;
                    d.Before[k] = site.HeightAt(p); d.After[k] = final(p); d.Water[k] = math.max(0f, ShoreSites.Level - final(p));
                    d.Planned[k] = math.abs(d.After[k] - d.Before[k]) > 0.01f ? d.After[k] : float.NaN;
                }
            d.WaterLevel = ShoreSites.Level;
            foreach (var n in plan.Nets)
                d.Nets.Add(new TerrainDump.Net { A = n.A, B = n.B, C = n.C, D = n.D, Width = Composer.DefaultHalfWidth(n.Role) * 2f, Follow = n.FollowTerrain, Quay = n.Category == AssetCategories.NetQuay, Prefab = n.Asset.Id ?? "" });
            foreach (var o in plan.Objects) d.Objects.Add(new TerrainDump.Obj { Position = o.Position, Yaw = o.Yaw, Prefab = o.Asset.Id ?? "" });
            foreach (var kv in plan.Facts) d.Facts.Add(new KeyValuePair<string, double>(kv.Key, kv.Value));
            d.Outlines.Add(ShoreSites.Ring().Select(v => v.xz).ToArray());
            return d;
        }

        [Fact]
        public void DumpRoundTripsAndMeasures()
        {
            var site = ShoreSites.Site("curved");
            var plan = ShoreSites.Plan("waterfront", "promenade", "curved");
            var d = FromPlan(plan, site);
            var json = d.ToJson();
            var back = TerrainDump.FromJson(json);
            Assert.Equal(d.Nx, back.Nx);
            Assert.Equal(d.Nets.Count, back.Nets.Count);
            Assert.Equal(d.Objects.Count, back.Objects.Count);
            Assert.True(math.isnan(back.Planned[0]) == math.isnan(d.Planned[0]));
            for (int k = 0; k < d.Count; k += 97) Assert.InRange(back.After[k] - d.After[k], -0.006f, 0.006f);
            Assert.Contains(back.Facts, kv => kv.Key == "walkMinRadius");
            Assert.Contains("curtain", back.Metrics());
            // A sample for the gallery's --terrain-dump (the game writes the same file to ModsData/Citymaker/debug).
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "citymaker-terrain-dump-sample.json"), json);
        }
    }
}
