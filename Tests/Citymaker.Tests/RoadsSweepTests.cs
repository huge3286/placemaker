using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Citymaker.Core;
using Citymaker.Core.Layers.Roads;
using Citymaker.Core.Layers.Roads.Generators;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;
using Geo = Citymaker.Core.Geometry.Poly;
using static Citymaker.Tests.RoadsTestUtil;

namespace Citymaker.Tests
{
    /// <summary>
    /// Full sweeps, adapted from Grid Road Generator's AllModesSweepTests: every pattern on every way of
    /// choosing an area (existing perimeter, drawn, painted) at extreme and middle slider values.
    /// 1. The ported generators reproduce Grid Road Generator's output exactly (golden file produced by
    ///    the original code), so the port — static caches and lock removed, comments translated — did
    ///    not change behaviour.
    /// 2. The whole roads layer runs on the 13 shapes × 9 patterns without exceptions or NaN, and the
    ///    timings are reported, slowest first.
    /// </summary>
    public class RoadsSweepTests
    {
        private readonly ITestOutputHelper _out;
        public RoadsSweepTests(ITestOutputHelper o) { _out = o; }

        private static readonly Func<float2, float> HillsFn = q => 500f + 40f * math.sin(q.x / 260f) * math.cos(q.y / 310f) + 0.04f * q.x;

        public static IEnumerable<(string name, bool free, List<float3> shape)> Areas()
        {
            yield return ("rect600x400", false, Poly(0, 0, 600, 0, 600, 400, 0, 400));
            yield return ("L", false, Poly(0, 0, 600, 0, 600, 300, 300, 300, 300, 600, 0, 600));
            yield return ("rot30", false, Rotated(Poly(0, 0, 800, 0, 800, 500, 0, 500), 30f));
            yield return ("triangle", false, Poly(0, 0, 700, 0, 200, 600));
            yield return ("small90", false, Poly(0, 0, 90, 0, 90, 90, 0, 90));
            yield return ("narrow1200x90", false, Poly(0, 0, 1200, 0, 1200, 90, 0, 90));
            yield return ("twonodes", false, Poly(0, 0, 500, 0));
            yield return ("curved", false, LoadRing("real-perimeter-backtrack.txt"));
            yield return ("hexagon", true, FreeAreaPerimeter.Smooth(Poly(0, 0, 900, -100, 1400, 400, 1100, 1000, 300, 1100, -200, 500)));
            yield return ("drawnNarrow", true, FreeAreaPerimeter.Smooth(Poly(0, 0, 1500, 0, 1500, 140, 0, 140)));
            yield return ("drawnGame", true, LoadRing("free-area-ring.txt"));
            // Brush outlines produced by Grid Road Generator's BrushMask (strokes of 100 m and 1000 m).
            yield return ("painted100", true, LoadRing("painted-100.txt"));
            yield return ("painted1000", true, LoadRing("painted-1000.txt"));
        }

        private static IEnumerable<(string name, bool loop, GridParameters p)> Patterns()
        {
            GridParameters D() => GridParameters.Default;
            foreach (int n in new[] { 1, 6, 12 })
            {
                var p = D(); p.Columns = n; p.Rows = n; yield return ($"grid fit {n}x{n}", false, p);
            }
            foreach (float s in new[] { 60f, 150f, 300f })
            {
                var p = D(); p.Mode = SpacingMode.FixedSpacing; p.SpacingMeters = s; p.AngleOffsetDegrees = s == 150f ? 33f : 0f;
                yield return ($"grid spacing {s}", false, p);
            }
            foreach (CulDeSacAxis axis in new[] { CulDeSacAxis.Columns, CulDeSacAxis.Rows, CulDeSacAxis.Both })
            foreach (float ratio in new[] { 0f, 50f, 100f })
            {
                var p = D(); p.Columns = 6; p.Rows = 6; p.CulDeSacMode = true; p.CulDeSacAxis = axis; p.CulDeSacRatio = ratio; p.CulDeSacDepth = ratio == 50f ? 0.8f : 0.5f;
                yield return ($"grid cul-de-sacs {axis} {ratio}%", false, p);
            }
            {
                var p = D(); p.Columns = 8; p.Rows = 8; p.AvenueColumnEnabled = true; p.AvenueRowEnabled = true; p.AvenueColumnIndex = 23; p.AvenueRowIndex = 3;
                yield return ("grid avenues + roundabout", false, p);
                var q = D(); q.Columns = 6; q.Rows = 6; q.AlignToTerrain = true; yield return ("grid aligned to terrain", false, q);
            }
            foreach (float b in new[] { 160f, 400f })
            foreach (float l in new[] { 40f, 150f })
            {
                var p = D(); p.TreeMode = true; p.TreeBranchSpacing = b; p.TreeCulDeSacSpacing = 60f + l / 2f; p.TreeCulDeSacLength = l;
                yield return ($"tree branches {b} cul-de-sacs {l}", false, p);
            }
            foreach (float s in new[] { 60f, 150f })
            foreach (float c in new[] { 0f, 100f })
            {
                var p = D(); p.OrganicMode = true; p.OrganicStreetSpacing = s; p.OrganicCurviness = c; p.OrganicLoopShare = 100f - c; p.OrganicSeed = (int)s;
                yield return ($"organic spacing {s} curviness {c}", false, p);
            }
            foreach (float s in new[] { 60f, 150f })
            foreach (float k in new[] { 150f, 500f })
            {
                var p = D(); p.ContourMode = true; p.ContourSpacing = s; p.ContourConnectorSpacing = k;
                yield return ($"contour spacing {s} links {k}", false, p);
            }
            foreach (float r in new[] { 100f, 250f, 400f })
            foreach (int a in new[] { 3, 16 })
            {
                var p = D(); p.MixedMode = true; p.MixedCoreRadius = r; p.RadialAvenues = a; p.RadialRoundaboutRadius = 40f; p.RadialLayers = 2;
                yield return ($"mixed centre {r} avenues {a}", false, p);
            }
            foreach (float s in new[] { 200f, 400f })
            foreach (float r in new[] { 0f, 100f })
            {
                var p = D(); p.CollectorSpacingMeters = s; p.LoopCulDeSacRatio = r;
                yield return ($"loop collectors {s} cul-de-sacs {r}%", true, p);
            }
            foreach (float z in new[] { 100f, 400f })
            {
                var p = D(); p.SuperblockMode = true; p.SuperblockZoneMeters = z; yield return ($"superblock {z}", true, p);
            }
            foreach (int layers in new[] { 1, 10 })
            foreach (int links in new[] { 2, 12 })
            {
                var p = D(); p.ConcentricMode = true; p.ConcentricLayers = layers; p.ConcentricConnections = links;
                yield return ($"concentric rings {layers} links {links}", true, p);
            }
            foreach (int a in new[] { 3, 16 })
            foreach (float rb in new[] { 20f, 150f })
            foreach (int layers in new[] { 0, 5 })
            {
                var p = D(); p.ConcentricMode = true; p.RadialMode = true; p.RadialAvenues = a; p.RadialRoundaboutRadius = rb; p.RadialLayers = layers;
                yield return ($"radial avenues {a} roundabout {rb} rings {layers}", true, p);
            }
        }

        [Fact]
        public void PortedGeneratorsReproduceGridRoadGeneratorExactly()
        {
            var golden = File.ReadAllLines(Path.Combine(FixtureDir(), "grg-golden.txt"))
                .Where(l => l.Length > 0).Select(l => l.Split('|')).ToDictionary(a => a[0] + "|" + a[1], a => a[2]);
            var failures = new List<string>();
            var timings = new List<(double ms, string what)>();
            foreach (var (area, free, shape) in Areas())
            {
                int index = 0;
                foreach (var (pattern, loop, template) in Patterns())
                {
                    var p = template;
                    p.HeightAt = HillsFn;
                    var positions = shape.Select(q => new float3(q.x, HillsFn(q.xz), q.z)).ToList();
                    string result;
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        var segs = loop ? GridGenerator.GenerateLoopGrid(positions, p) : GridGenerator.GenerateGrid(positions, p);
                        if (free && positions.Count >= 3 && segs.Count > 0) segs.AddRange(FreeAreaPerimeter.PerimeterRoad(positions, segs));
                        double sum = 0;
                        foreach (var s in segs)
                        {
                            if (!MathFinite(s.Start) || !MathFinite(s.End)) failures.Add($"{area} / {pattern}: NaN point");
                            sum += s.Start.x * 1.0 + s.Start.z * 3.0 + s.End.x * 5.0 + s.End.z * 7.0 + (s.IsArc ? 11 : 0) + (s.IsCulDeSacEnd ? 13 : 0) + (s.IsAvenue ? 17 : 0);
                        }
                        result = segs.Count + " " + sum.ToString("R", CultureInfo.InvariantCulture);
                    }
                    catch (Exception e) { result = "EX " + e.GetType().Name; }
                    timings.Add((sw.Elapsed.TotalMilliseconds, $"{area} / {pattern}"));
                    string key = area + "|" + index++;
                    if (!golden.TryGetValue(key, out var expected)) failures.Add($"{key}: not in golden file");
                    else if (expected != result) failures.Add($"{area} / {pattern}: expected {expected}, got {result}");
                }
            }
            _out.WriteLine($"{timings.Count} generations, {failures.Count} differences");
            foreach (var f in failures.Take(40)) _out.WriteLine(f);
            foreach (var t in timings.OrderByDescending(t => t.ms).Take(10)) _out.WriteLine($"  {t.ms,7:F0} ms  {t.what}");
            Assert.Empty(failures);
        }

        private static bool MathFinite(float3 p) => math.all(math.isfinite(p));

        [Fact]
        public void LayerRunsEveryPatternOnEveryShape()
        {
            var layer = new RoadsLayer();
            var failures = new List<string>();
            var timings = new List<(double ms, string what, int nets, int blocks)>();
            foreach (var (area, free, shape) in Areas())
            {
                if (shape.Count < 3) continue; // a two-node pick is not an area in Citymaker
                var source = free ? (area.StartsWith("painted") ? AreaSource.Painted : AreaSource.Drawn) : AreaSource.Block;
                var ring = shape.Select(q => new float3(q.x, HillsFn(q.xz), q.z)).ToList();
                foreach (var pattern in RoadsSchema.PatternValues)
                {
                    var input = Input(ring, source, Settings((RoadsIds.Pattern, pattern)));
                    input.Site.HeightAt = HillsFn;
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        var plan = layer.Plan(input, CancellationToken.None);
                        sw.Stop();
                        foreach (var n in plan.Nets)
                            if (!MathFinite(n.A) || !MathFinite(n.B) || !MathFinite(n.C) || !MathFinite(n.D)) { failures.Add($"{area}/{pattern}: NaN net"); break; }
                        foreach (var r in plan.Regions)
                        {
                            if (r.Ring.Any(p => !MathFinite(p))) { failures.Add($"{area}/{pattern}: NaN region"); break; }
                            if (!Geo.IsSimple(Ring2(r.Ring)) || Geo.SignedArea(Ring2(r.Ring)) <= 0) { failures.Add($"{area}/{pattern}: invalid region {r.Key}"); break; }
                        }
                        if (plan.HasErrors) failures.Add($"{area}/{pattern}: " + string.Join(",", plan.Issues.Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Key)));
                        timings.Add((sw.Elapsed.TotalMilliseconds, $"{area} / {pattern}", plan.Nets.Count, plan.Regions.Count(r => r.Tags.Contains(RegionTags.Block))));
                    }
                    catch (Exception e)
                    {
                        failures.Add($"{area}/{pattern}: {e.GetType().Name} {e.Message} {e.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}");
                    }
                }
            }
            _out.WriteLine($"{timings.Count} plans, {failures.Count} failures");
            foreach (var f in failures) _out.WriteLine("FAIL " + f);
            _out.WriteLine("Slowest:");
            foreach (var t in timings.OrderByDescending(t => t.ms).Take(25)) _out.WriteLine($"  {t.ms,7:F0} ms  {t.nets,5} nets {t.blocks,4} blocks  {t.what}");
            Assert.Empty(failures);
        }
    }
}
