// s32: the Strip mall is "perfect" (user) — the commercial pass must not change a single thing it plans. Every preset
// on the flat test sites and on sloped ground is fingerprinted (every object, net, surface, terraform, zoning quad,
// region, fact, issue and the zone guard's cells / blockers) and compared with the fingerprints recorded on master
// before s32 (Fixtures/stripmall-fingerprints.txt). Regenerate only for an intended strip mall change:
//   set PM_UPDATE_STRIPMALL=1 and run this test once.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Citymaker.Core;
using Citymaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    public class StripMallRegressionTests
    {
        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        private static string FixturePath => Path.Combine(TestKit.RepoRoot(), "Tests", "Citymaker.Tests", "Fixtures", "stripmall-fingerprints.txt");

        public static IEnumerable<(string key, Func<ComposedPlan> plan)> Cases()
        {
            var presets = new List<string> { "" };
            presets.AddRange(GeneratorRegistry.Get("stripMall").Presets.Select(p => p.Id));
            foreach (var p in presets)
            {
                foreach (var s in new[] { "small", "big", "wide", "L" })
                {
                    string preset = p, site = s;
                    yield return ($"{(p == "" ? "default" : p)}/{s}", () =>
                    {
                        var ring = s == "big" ? TestKit.Rect(160, 160) : s == "wide" ? TestKit.Rect(220, 110) : s == "L" ? TestKit.LShape(150) : TestKit.Rect(115, 115);
                        return Compose(preset, ring, ZoningCutoutTests.Streets(ring));
                    });
                }
                foreach (var g in new[] { "cross", "steep" })
                {
                    string preset = p, ground = g;
                    yield return ($"{(p == "" ? "default" : p)}/{g}", () =>
                    {
                        var ring = TestKit.Rect(140, 130);
                        var site = ZoningCutoutTests.Streets(ring);
                        var h = LotGradingTests.Ground(ground);
                        site.HeightAt = h;
                        for (int k = 0; k < site.NearbyNets.Count; k++)
                        {
                            var n = site.NearbyNets[k];
                            n.A.y = h(n.A.xz); n.B.y = h(n.B.xz); n.C.y = h(n.C.xz); n.D.y = h(n.D.xz);
                            site.NearbyNets[k] = n;
                        }
                        return Compose(preset, ring, site);
                    });
                }
            }
        }

        private static ComposedPlan Compose(string preset, float3[] ring, SiteContext site)
        {
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get("stripMall"), 5);
            if (!string.IsNullOrEmpty(preset)) Editor.ApplyPreset(r, RecipePaths.Root, preset);
            uint n = 0;
            Editor.AddDefaultFeatures(r, () => ++n);
            // (rf-shops3) Planned from the player's entrances: a car entrance mid main street, a footpath beside it, a driveway.
            ShopTestEntrances.Place(r, ring, site, footpath: true, driveway: true);
            return Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
        }

        private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
        private static string F(float3 v) => F(v.x) + "," + F(v.y) + "," + F(v.z);
        private static string F(float2 v) => F(v.x) + "," + F(v.y);

        public static string Fingerprint(ComposedPlan plan)
        {
            var sb = new StringBuilder();
            foreach (var l in plan.Layers)
            {
                if (l == null) continue;
                sb.Append("L").Append(l.GeneratorId).Append('@').Append(l.StepPath).Append('\n');
                foreach (var o in l.Objects) sb.Append($"O{(int)o.Role}:{F(o.Position)},{F(o.Yaw)},{o.Asset.Id},{o.Variation},{o.AttachedNet};");
                foreach (var x in l.Nets) sb.Append($"N{(int)x.Role}:{F(x.A)}|{F(x.B)}|{F(x.C)}|{F(x.D)}|{x.Asset.Id}|{x.StartAttach}:{F(x.StartAttachT)}|{x.EndAttach}:{F(x.EndAttachT)};");
                foreach (var s in l.Surfaces) { sb.Append($"S{(int)s.Role}{s.Asset.Id}:"); foreach (var p in s.Ring) sb.Append(F(p)).Append(' '); }
                foreach (var a in l.Areas) { sb.Append($"A{(int)a.Role}:"); foreach (var p in a.Ring) sb.Append(F(p)).Append(' '); }
                foreach (var t in l.Terraform) { sb.Append($"T{F(t.TargetHeight)}:"); foreach (var p in t.Ring) sb.Append(F(p)).Append(' '); }
                foreach (var z in l.Zoning) sb.Append($"Z{z.ZoneId}:{F(z.A)}|{F(z.B)}|{F(z.C)}|{F(z.D)};");
                foreach (var g in l.Regions) { sb.Append("R" + g.Key + string.Join("+", g.Tags) + ":"); foreach (var p in g.Ring) sb.Append(F(p)).Append(' '); }
                foreach (var kv in l.Facts.OrderBy(k => k.Key, StringComparer.Ordinal)) sb.Append($"F{kv.Key}={kv.Value.ToString("R", CultureInfo.InvariantCulture)};");
                foreach (var i in l.Issues) sb.Append("I" + i.Key + ";");
                sb.Append('\n');
            }
            foreach (var c in plan.ZoneCells) sb.Append($"C{(int)c.State}:{F(c.A)}|{F(c.C)};");
            foreach (var b in plan.ZoneBlockers) { sb.Append("B:"); foreach (var p in b) sb.Append(F(p)).Append(' '); }
            return sb.ToString();
        }

        private static string Hash(string s)
        {
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(s))).Substring(0, 24);
        }

        [Fact]
        public void StripMallPlansAreUnchanged()
        {
            var now = Cases().Select(c => (c.key, hash: Hash(Fingerprint(c.plan())))).ToList();
            if (Environment.GetEnvironmentVariable("PM_UPDATE_STRIPMALL") == "1")
            {
                File.WriteAllLines(FixturePath, now.Select(x => x.key + " " + x.hash));
                return;
            }
            Assert.True(File.Exists(FixturePath), "missing fixture " + FixturePath);
            var recorded = File.ReadAllLines(FixturePath).Where(l => l.Length > 0).Select(l => l.Split(' ')).ToDictionary(p => p[0], p => p[1]);
            Assert.Equal(recorded.Count, now.Count);
            var changed = now.Where(x => !recorded.TryGetValue(x.key, out var h) || h != x.hash).Select(x => x.key).ToList();
            Assert.True(changed.Count == 0, "strip mall plans changed: " + string.Join(", ", changed));
        }
    }
}
