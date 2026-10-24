// Shared checks for the industrial work types (constructionSite, quarry): the type-wave definition of
// done (every preset on 1 ha and a 6 ha L-shape, inside, clear of paths and Reserved, surfaces accepted,
// terraform blended at the edge, deterministic, fast, features compose, English for every key).

using System.Collections.Generic;
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
    public static class WorkSiteTestKit
    {
        public static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        /// <summary>1 ha square, 1 ha L-shape, 6 ha L-shape.</summary>
        public static IEnumerable<(string name, float3[] ring)> Shapes()
        {
            yield return ("1ha", TestKit.Rect(100, 100));
            yield return ("1haL", TestKit.LShape(120));
            yield return ("6haL", TestKit.LShape(293));
        }

        public static Recipe WithPreset(string layer, PresetInfo preset, params string[] features)
        {
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get(layer), 11);
            if (preset != null) foreach (var kv in preset.Values.Values) r.Root.Settings.Values[kv.Key] = kv.Value;
            uint seed = 100;
            foreach (var f in features) Assert.True(Editor.SetFeature(r, "0", f, true, seed++), "feature " + f + " of " + layer);
            return r;
        }

        public static ComposedPlan Compose(Recipe r, float3[] ring, SiteContext site = null) =>
            Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site ?? new SiteContext(), CancellationToken.None);

        public static string Errors(ComposedPlan p) =>
            string.Join("; ", p.AllIssues().Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Key + " " + string.Join(",", i.Args)));

        /// <summary>A road along the bottom of a rect / L-shape (both start at the origin along +x).</summary>
        public static SiteContext Road(float3[] ring) => TestKit.RoadAlongBottom(ring.Max(p => p.x));

        public static void AssertInside(AreaPlan plan, float3[] ring, float tolerance = 0.05f)
        {
            var r = Poly.To2D(ring);
            foreach (var o in plan.Objects)
                Assert.True(Poly.Contains(r, o.Position.xz) || Poly.DistanceToBoundary(r, o.Position.xz) < tolerance,
                    $"{plan.GeneratorId}: {o.Role}/{o.Category} at {o.Position.xz} lies outside the area");
            foreach (var n in plan.Nets)
                foreach (var p in new[] { n.A, n.D })
                    Assert.True(Poly.Contains(r, p.xz) || Poly.DistanceToBoundary(r, p.xz) < 0.5f, $"{plan.GeneratorId}: {n.Role} end {p.xz} outside");
            foreach (var t in plan.Terraform)
                foreach (var p in t.Ring)
                    Assert.True(Poly.Contains(r, p.xz) || Poly.DistanceToBoundary(r, p.xz) < 0.5f, $"{plan.GeneratorId}: terraform vertex {p.xz} outside");
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
                    Assert.True(d >= Composer.DefaultHalfWidth(n.Role) + 0.3f, $"{plan.GeneratorId}: {o.Role}/{o.Category} at {o.Position.xz} is {d:0.00} m from a {n.Role}");
                }
            }
        }

        /// <summary>Every terraform change is within maxSlope × (distance to the outline) of the original ground.</summary>
        public static void AssertBlended(AreaPlan plan, float3[] ring, SiteContext site, float maxSlopePct)
        {
            var r = Poly.To2D(ring);
            float slope = maxSlopePct / 100f;
            foreach (var t in plan.Terraform)
            {
                Assert.True(t.Permanent, "terraform must be flagged permanent");
                var c = Poly.Centroid(Poly.To2D(t.Ring));
                float edge = Poly.DistanceToBoundary(r, c);
                float change = math.abs(t.TargetHeight - site.HeightAt(c));
                if (!float.IsNaN(t.WaterLevel)) continue;   // the lake floor ring sits deep inside the pit, checked separately
                Assert.True(change <= edge * slope + 0.6f, $"{plan.GeneratorId}: {change:0.00} m change {edge:0.0} m from the edge (cliff)");
            }
            if (plan.Terraform.Count > 0)
                Assert.Contains(plan.Issues, i => i.Severity == IssueSeverity.Warning && (i.Key == TerrainShaper.PermanentKey || i.Key.Contains("Permanent")));
        }

        public static void AssertClearOfReserved(AreaPlan plan, ReservedSpace reserved)
        {
            foreach (var o in plan.Objects)
            {
                if (o.Role == ObjectRole.AccessPoint) continue;
                float rr = o.Role == ObjectRole.Decal ? 0.3f : 0.5f;
                foreach (var d in reserved.Discs)
                    Assert.True(math.distance(d.Center, o.Position.xz) >= d.Radius + rr - 0.05f, $"{o.Role}/{o.Category} at {o.Position.xz} overlaps reserved disc");
                foreach (var c in reserved.Corridors)
                    Assert.True(math.sqrt(SegmentIndex.DistSq(o.Position.xz, c.A, c.B)) >= c.HalfWidth + rr - 0.05f, $"{o.Role}/{o.Category} at {o.Position.xz} overlaps reserved corridor");
            }
        }

        /// <summary>Every key a layer's schema, presets, features and source literals refer to has English text.</summary>
        public static List<string> MissingKeys(IAreaGenerator g, params string[] sourceDirs)
        {
            var lang = TestKit.Lang();
            var missing = new List<string>();
            void Need(string key) { if (!string.IsNullOrEmpty(key) && !lang.ContainsKey(key)) missing.Add(key); }
            Need(g.NameKey);
            foreach (var c in g.Controls)
            {
                Need(c.LabelKey); Need(c.TooltipKey);
                if (!string.IsNullOrEmpty(c.Group) && c.Group != "Basics") Need("Citymaker.UI.Group." + c.Group);
                foreach (var o in c.Options) { Need(o.LabelKey); Need(o.TooltipKey); }
                Assert.True(lang.TryGetValue(c.LabelKey, out var label) ? label.Split(' ').Length <= 3 : true, g.Id + "." + c.Id + " label longer than 3 words");
            }
            foreach (var p in g.Presets) Need(p.LabelKey);
            foreach (var f in Features.Of(g))
            {
                Need(f.LabelKey); Need(f.TooltipKey);
                var fg = GeneratorRegistry.Get(f.Layer);
                Assert.NotNull(fg);
                Need(fg.NameKey);
                foreach (var c in fg.Controls) { Need(c.LabelKey); Need(c.TooltipKey); foreach (var o in c.Options) Need(o.LabelKey); }
            }
            var root = TestKit.RepoRoot();
            foreach (var dir in sourceDirs)
                foreach (var file in Directory.GetFiles(Path.Combine(root, dir), "*.cs"))
                    foreach (Match m in Regex.Matches(File.ReadAllText(file), "\"(Citymaker\\.[A-Za-z0-9_.:]+[A-Za-z0-9])\""))
                        Need(m.Groups[1].Value);
            foreach (var f in typeof(AssetCategories).GetFields())
            {
                var v = (string)f.GetValue(null);
                if (v.StartsWith("construction.") || v.StartsWith("quarry.")) Need(Keys.Category(v));
            }
            return missing.Distinct().ToList();
        }
    }
}
