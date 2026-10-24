// Shared checks for the water types (beach, waterfront): the type-wave definition of done
// (presets on 1 ha and a 6 ha L-shape, determinism, containment, paths, Reserved, surfaces, terrain
// blended at the outline away from the water, speed, features, localisation) plus water sites.

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

namespace Citymaker.Tests.Types
{
    public static class WaterTypesKit
    {
        public static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        public static float3[] OneHa => TestKit.Rect(100, 100);
        /// <summary>6 ha L-shape (area = size² · 0.6975).</summary>
        public static float3[] SixHaL => TestKit.LShape(293.3f);
        public static float3[] FiveHa => TestKit.Rect(250, 200);

        /// <summary>Sea below z = 0 (outside a rect that starts at z = 0), terrain rising inland.</summary>
        public static SiteContext SeaSouth(float rise = 0f) => new SiteContext
        {
            WaterDepthAt = p => p.y < 0f ? 3f : 0f,
            HeightAt = p => p.y < 0f ? -0.5f : rise * p.y,
        };

        /// <summary>Sea below z = 0 and a road along the top edge of a w × h rect (street shops need frontage).</summary>
        public static SiteContext SeaSouthRoadNorth(float w, float h)
        {
            var site = SeaSouth(0.03f);
            site.NearbyNets.Add(new NearbyNet
            {
                ExternalId = 78, A = new float3(-20, 0, h + 6), B = new float3(w / 3, 0, h + 6), C = new float3(2 * w / 3, 0, h + 6),
                D = new float3(w + 20, 0, h + 6), Width = 12, Category = NetCategory.Road, HasSidewalk = true,
            });
            return site;
        }

        /// <summary>Water inside the area: everything below z = waterZ is 2 m deep.</summary>
        public static SiteContext WaterInside(float waterZ) => new SiteContext
        {
            WaterDepthAt = p => p.y < waterZ ? 2f : 0f,
            HeightAt = p => p.y < waterZ ? -2f : 0.02f * (p.y - waterZ),
        };

        public static Recipe With(string layer, params string[] features)
        {
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get(layer), 11);
            uint seed = 100;
            foreach (var f in features) Assert.True(Editor.SetFeature(r, "0", f, true, seed++), "feature " + f + " of " + layer);
            return r;
        }

        public static ComposedPlan Compose(Recipe r, float3[] ring, SiteContext site = null) =>
            Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site ?? new SiteContext(), CancellationToken.None);

        public static string Errors(ComposedPlan p) =>
            string.Join("; ", p.AllIssues().Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Key + " " + string.Join(",", i.Args)));

        public static AreaPlan PlanPreset(string id, PresetInfo preset, float3[] ring, SiteContext site, uint seed = 5, ReservedSpace reserved = null)
        {
            var input = TestKit.Input(ring, preset.Values.Clone(), seed, null, site);
            if (reserved != null) input.Reserved = reserved;
            return TestKit.Gen(id).Plan(input, CancellationToken.None);
        }

        public static void AssertInside(AreaPlan plan, float3[] ring, float tolerance = 0.05f)
        {
            var r = Poly.To2D(ring);
            foreach (var o in plan.Objects)
                Assert.True(Poly.Contains(r, o.Position.xz) || Poly.DistanceToBoundary(r, o.Position.xz) < tolerance,
                    $"{plan.GeneratorId}: {o.Role}/{o.Category} at {o.Position.xz} lies outside the area");
            foreach (var n in plan.Nets)
            {
                // s20: a quay or boardwalk is built AT the water, which may lie a few metres past the drawn outline.
                bool deck = !n.FollowTerrain && (n.Category == AssetCategories.NetQuay || n.Category == AssetCategories.NetPier);
                // rf-beach: piers and jetties reach out over the real water past an outline drawn along the shore.
                bool pier = !n.FollowTerrain && n.Category == AssetCategories.NetPier;
                for (int i = 0; i <= 8; i++)
                {
                    var p = Composer.Bezier(n.A, n.B, n.C, n.D, i / 8f).xz;
                    Assert.True(Poly.Contains(r, p) || Poly.DistanceToBoundary(r, p) < (pier ? 130f : deck ? 10.5f : 0.5f), $"{plan.GeneratorId}: {n.Role} at {p} outside");
                }
            }
        }

        public static void AssertNothingOnPaths(AreaPlan plan)
        {
            foreach (var o in plan.Objects)
            {
                if (o.Role == ObjectRole.Decal || o.Role == ObjectRole.AccessPoint || o.Role == ObjectRole.AnimalSpawner) continue;
                foreach (var n in plan.Nets)
                {
                    if (n.Role != NetRole.Pathway && n.Role != NetRole.PathwayWide) continue;
                    float d = TestKit.DistanceToCurve(o.Position.xz, n);
                    Assert.True(d >= Composer.DefaultHalfWidth(n.Role) + 0.3f,
                        $"{plan.GeneratorId}: {o.Role}/{o.Category} at {o.Position.xz} is {d:0.00} m from a {n.Role}");
                }
            }
        }

        /// <summary>
        /// Away from the water every terraform cell is blended back to the original ground at the
        /// outline (≤ 30 % of its distance to it); cells right at the waterline may meet the water.
        /// </summary>
        public static void AssertTerrainBlended(AreaPlan plan, float3[] ring, SiteContext site, Func<float2, float> distanceToWater, float shoreBand)
        {
            var r = Poly.To2D(ring);
            Assert.All(plan.Terraform, t => Assert.True(t.Permanent));
            if (plan.Terraform.Count > 0) Assert.Contains(plan.Issues, i => i.Key == TerrainShaper.PermanentKey && i.Severity == IssueSeverity.Warning);
            foreach (var t in plan.Terraform)
            {
                var c = Poly.Centroid(Poly.To2D(t.Ring));
                if (distanceToWater(c) < shoreBand) continue;
                float edge = Poly.DistanceToBoundary(r, c);
                float change = math.abs(t.TargetHeight - site.HeightAt(c));
                Assert.True(change < edge * 0.3f + 0.3f, $"{plan.GeneratorId}: {change:0.00} m change {edge:0.0} m from the edge at {c} (cliff)");
            }
        }

        public static void AssertLabels(string id)
        {
            var lang = TestKit.Lang();
            var g = TestKit.Gen(id);
            var missing = new List<string>();
            void Need(string key) { if (!string.IsNullOrEmpty(key) && !lang.ContainsKey(key)) missing.Add(key); }
            Need(g.NameKey);
            foreach (var c in g.Controls)
            {
                Need(c.LabelKey); Need(c.TooltipKey);
                if (!string.IsNullOrEmpty(c.Group)) Need("Citymaker.UI.Group." + c.Group);
                foreach (var o in c.Options) { Need(o.LabelKey); Need(o.TooltipKey); }
                if (lang.TryGetValue(c.LabelKey, out var label)) Assert.True(label.Split(' ').Length <= 3, id + "." + c.Id + " label too long: " + label);
            }
            foreach (var p in g.Presets) Need(p.LabelKey);
            foreach (var f in Features.Of(g)) { Need(f.LabelKey); Need(f.TooltipKey); }
            Assert.True(missing.Count == 0, "Missing en-US keys: " + string.Join(", ", missing));
        }

        public static void AssertSourceKeys(string folder)
        {
            var lang = TestKit.Lang();
            var missing = new List<string>();
            foreach (var file in Directory.GetFiles(Path.Combine(TestKit.RepoRoot(), "Core", "Layers", "Types", folder), "*.cs"))
                foreach (Match m in Regex.Matches(File.ReadAllText(file), "\"(Citymaker\\.[A-Za-z0-9_.:]+[A-Za-z0-9])\""))
                    if (!lang.ContainsKey(m.Groups[1].Value)) missing.Add(m.Groups[1].Value + " (" + Path.GetFileName(file) + ")");
            foreach (var f in typeof(AssetCategories).GetFields())
            {
                var v = (string)f.GetValue(null);
                if ((v.StartsWith("beach.") || v.StartsWith("waterfront.")) && !lang.ContainsKey(Keys.Category(v))) missing.Add(Keys.Category(v));
            }
            Assert.True(missing.Count == 0, "Missing en-US keys: " + string.Join(", ", missing.Distinct()));
        }

        public static void AssertRecipe(string id)
        {
            var r = TestKit.LoadRecipe(id + ".json");
            Assert.Equal(id, r.Root.Layer);
            var file = File.ReadAllText(Path.Combine(TestKit.RepoRoot(), "Recipes", id + ".json"));
            var lang = TestKit.Lang();
            foreach (Match m in Regex.Matches(file, "\"(Citymaker\\.[A-Za-z0-9_.]+)\"")) Assert.True(lang.ContainsKey(m.Groups[1].Value), m.Groups[1].Value);
        }

        public static long Time(Action a)
        {
            a();   // JIT
            var sw = Stopwatch.StartNew();
            a();
            return sw.ElapsedMilliseconds;
        }

        public static ReservedSpace SomeReserved()
        {
            var r = new ReservedSpace();
            r.AddDisc(new float2(50, 60), 6f, "test");
            r.AddCorridor(new float2(10, 30), new float2(90, 35), 2f, "test");
            return r;
        }
    }
}
