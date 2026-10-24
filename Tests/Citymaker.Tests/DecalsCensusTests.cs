// s21 decal census: replays the player's catalog-dump.json through the real decal classifier
// (Core/Decals/DecalCatalog + DecalPools) and checks the census invariants. With PM_WRITE_DECALS=1 it also
// rewrites the generated part of docs/DECALS.md (after the "census:begin" marker). Pack metadata (decal
// layer masks, verified-by-eye packs) is read from the local Paradox mods cache when present.
// Without a dump the tests do nothing.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Citymaker.Core.Decals;
using Xunit;

namespace Citymaker.Tests
{
    public class DecalsCensusTests
    {
        private sealed class Entry
        {
            public string Id;
            public bool Vanilla;
            public float X, Z;
            public List<string> Categories = new List<string>();
            public DecalSpec Spec;
            public int? Mask;
        }

        private static List<Entry> Load(out string source)
        {
            source = null;
            var path = AssetCurationTests.DumpPath();
            if (path == null || !File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            source = Path.GetFileName(path) + " v" + root.GetProperty("version").GetInt32() + " " + root.GetProperty("written").GetString();
            var byId = new Dictionary<string, Entry>(StringComparer.Ordinal);
            foreach (var cat in root.GetProperty("categories").EnumerateObject())
            {
                if (!cat.Name.StartsWith("decal", StringComparison.Ordinal) && cat.Name != "parking.decal") continue;
                foreach (var it in cat.Value.EnumerateArray())
                {
                    var id = it.GetProperty("id").GetString();
                    if (!byId.TryGetValue(id, out var e))
                    {
                        e = new Entry { Id = id, Vanilla = it.TryGetProperty("vanilla", out var v) && v.GetBoolean() };
                        if (it.TryGetProperty("bounds", out var b) && b.TryGetProperty("size", out var s))
                        {
                            var a = s.EnumerateArray().Select(x => (float)x.GetDouble()).ToArray();
                            e.X = a[0]; e.Z = a[2];
                        }
                        byId[id] = e;
                    }
                    e.Categories.Add(cat.Name);
                }
            }
            var masks = PackMasks();
            foreach (var e in byId.Values)
            {
                e.Spec = DecalCatalog.Describe(e.Id, e.X, e.Z);
                foreach (var kv in masks)
                    if (e.Id.EndsWith(" " + kv.Key + " Decal", StringComparison.Ordinal)) { e.Mask = kv.Value; break; }
            }
            // Parking stalls that are bike racks (props, not decals) were listed under parking.decal: keep only decal meshes.
            return byId.Values.Where(e => e.Categories.Any(c => c.StartsWith("decal", StringComparison.Ordinal)) || e.Spec.Family == DecalFamily.ParkingStall)
                .OrderBy(e => (int)e.Spec.Family).ThenBy(e => e.Id, StringComparer.Ordinal).ToList();
        }

        /// <summary>"Leaf Dead Leaves1 Yellow scattered" → colossal_DecalLayerMask, from CustomDecals/*/decal.json in the mods cache.</summary>
        private static Dictionary<string, int> PackMasks()
        {
            var r = new Dictionary<string, int>(StringComparer.Ordinal);
            var home = Environment.GetEnvironmentVariable("USERPROFILE") ?? "";
            var dir = Path.Combine(home, "AppData", "LocalLow", "Colossal Order", "Cities Skylines II", ".cache", "Mods", "pdx_mods");
            if (!Directory.Exists(dir)) return r;
            foreach (var f in Directory.EnumerateFiles(dir, "decal.json", SearchOption.AllDirectories))
            {
                try
                {
                    var item = Path.GetFileName(Path.GetDirectoryName(f));
                    var cat = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(f)));
                    var txt = File.ReadAllText(f).TrimStart('﻿');
                    using var d = JsonDocument.Parse(txt, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                    if (d.RootElement.TryGetProperty("Float", out var fl) && fl.TryGetProperty("colossal_DecalLayerMask", out var m))
                        r[cat + " " + item] = (int)m.GetDouble();
                }
                catch (Exception) { }
            }
            return r;
        }

        [Fact]
        public void CensusInvariants()
        {
            var all = Load(out var source);
            if (all == null) return;
            // Vanilla 1.6.2 has no decorative decal: every vanilla decal is functional.
            foreach (var e in all.Where(e => e.Vanilla)) Assert.True(e.Spec.Functional, e.Id + " → " + e.Spec);
            // Everything a dressing pool would pick is a decorative, ground-going decal.
            foreach (var e in all)
                foreach (var pool in DecalPools.PoolsFor(e.Spec))
                {
                    Assert.False(e.Spec.Functional, e.Id);
                    Assert.NotEqual(DecalFamily.BuildingOnly, e.Spec.Family);
                }
            if (Environment.GetEnvironmentVariable("PM_WRITE_DECALS") == "1") Write(all, source);
        }

        private static readonly Dictionary<DecalFamily, string> Depicts = new Dictionary<DecalFamily, string>
        {
            [DecalFamily.LeafLitter] = "fallen leaves / foliage, scattered or in drifts",
            [DecalFamily.LeafEdge] = "leaves drifted against one edge (\"lined\")",
            [DecalFamily.PalmLitter] = "single palm fronds",
            [DecalFamily.Mulch] = "bark mulch / needle bed",
            [DecalFamily.Moss] = "moss, weeds",
            [DecalFamily.Overgrowth] = "grass growing through / patchy grass on dirt",
            [DecalFamily.Dirt] = "bare dirt patch, worn ground",
            [DecalFamily.Dust] = "coloured dust (grey, brown, red)",
            [DecalFamily.Mud] = "mud, road dust, dirt-road surface strips",
            [DecalFamily.Sand] = "sand patch",
            [DecalFamily.WetSand] = "wet sand (bands, patches)",
            [DecalFamily.Rocks] = "stones, gravel, scattered rocks",
            [DecalFamily.Rubble] = "gravel/rubble grit",
            [DecalFamily.Stain] = "grime / dark stain blotch",
            [DecalFamily.Oil] = "oil blot",
            [DecalFamily.Leak] = "drip / leakage streak",
            [DecalFamily.Puddle] = "puddle (mirror water) or water-filled pothole",
            [DecalFamily.Wet] = "wet patch, water splash",
            [DecalFamily.Crack] = "cracks, broken surface",
            [DecalFamily.Patch] = "asphalt / tar / stone repair patch",
            [DecalFamily.Tyre] = "tyre tread / wheel track",
            [DecalFamily.Footprint] = "footprints",
            [DecalFamily.Litter] = "litter, papers, cardboard, rubbish",
            [DecalFamily.Glass] = "broken glass, bottles",
            [DecalFamily.BeachWrack] = "seaweed, shells, foam, sea trash, driftwood",
            [DecalFamily.Graffiti] = "graffiti on the ground",
            [DecalFamily.Paving] = "paving / tiles / cobbles texture",
            [DecalFamily.LawnPattern] = "lawn tile: mowing pattern / grass shape",
            [DecalFamily.GroundSheet] = "large ground texture sheet (aerial, terrain, road side)",
            [DecalFamily.Marking] = "road / text / arrow / icon marking",
            [DecalFamily.SportsMarking] = "court, pitch or playground pad",
            [DecalFamily.ParkingStall] = "parking stall / parking icon",
            [DecalFamily.FarmTrack] = "farm wheel ruts (laid along rows)",
            [DecalFamily.Logo] = "Chirper logo",
            [DecalFamily.BuildingOnly] = "wall / roof / curb / window decal (not ground)",
            [DecalFamily.Unknown] = "?",
        };

        private static string MaskText(int? m)
        {
            if (m == null) return "";
            var parts = new List<string>();
            if ((m & 1) != 0) parts.Add("T");
            if ((m & 2) != 0) parts.Add("R");
            if ((m & 4) != 0) parts.Add("B");
            if ((m & 32) != 0) parts.Add("O");
            return string.Join("", parts);
        }

        private static void Write(List<Entry> all, string source)
        {
            var repo = new DirectoryInfo(AppContext.BaseDirectory);
            while (repo != null && !File.Exists(Path.Combine(repo.FullName, "citymaker.csproj"))) repo = repo.Parent;
            if (repo == null) return;
            var file = Path.Combine(repo.FullName, "docs", "DECALS.md");
            const string marker = "<!-- census:begin (generated by DecalsCensusTests, PM_WRITE_DECALS=1) -->";
            string head = File.Exists(file) ? File.ReadAllText(file) : "# Decals\n\n";
            int at = head.IndexOf(marker, StringComparison.Ordinal);
            if (at >= 0) head = head.Substring(0, at);
            var sb = new StringBuilder(head);
            sb.AppendLine(marker);
            sb.AppendLine();
            sb.AppendLine("## Census (generated)");
            sb.AppendLine();
            sb.AppendLine($"Source: `{source}` — {all.Count} decal objects ({all.Count(e => e.Vanilla)} vanilla, {all.Count(e => !e.Vanilla)} from asset packs).");
            sb.AppendLine("Family/shape/ground/orientation come from `Core/Decals/DecalCatalog.Describe` with the real footprint; `pools` = the dressing pools");
            sb.AppendLine("(`DecalPools`) an item can be picked for automatically (size caps apply when the game builds the pools). `mask` = the pack's");
            sb.AppendLine("`colossal_DecalLayerMask` (read as DecalLayers: T terrain, R roads/paths, B buildings, O other; blank = vanilla/unknown — the");
            sb.AppendLine("CustomDecals encoding is unverified, see finding 3). Confidence: ●● item word explicit and pack checked by eye (icons/textures), ● item word only, ○ guessed.");
            sb.AppendLine();
            sb.AppendLine("### Counts by family");
            sb.AppendLine();
            sb.AppendLine("| family | total | vanilla | functional | depicts |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var g in all.GroupBy(e => e.Spec.Family).OrderBy(g => (int)g.Key))
                sb.AppendLine($"| {g.Key} | {g.Count()} | {g.Count(e => e.Vanilla)} | {(DecalCatalog.IsFunctional(g.Key) ? "yes" : "")} | {Depicts[g.Key]} |");
            sb.AppendLine();
            sb.AppendLine("### Every decal");
            foreach (var g in all.GroupBy(e => e.Spec.Family).OrderBy(g => (int)g.Key))
            {
                sb.AppendLine();
                sb.AppendLine($"#### {g.Key} ({g.Count()}) — {Depicts[g.Key]}");
                sb.AppendLine();
                sb.AppendLine("| decal | size m | shape | ground | orientation | mask | conf | pools |");
                sb.AppendLine("|---|---|---|---|---|---|---|---|");
                foreach (var e in g)
                {
                    string conf = e.Spec.Confidence >= 2 ? "●●" : e.Spec.Confidence == 1 ? "●" : "○";
                    string size = e.X > 0 ? $"{e.X:0.#}×{e.Z:0.#}" : "";
                    string pools = string.Join(" ", DecalPools.PoolsFor(e.Spec).Select(p => p.Substring(6)));
                    sb.AppendLine($"| {e.Id.Replace("|", "\\|")}{(e.Vanilla ? " *(vanilla)*" : "")} | {size} | {e.Spec.Shape} | {e.Spec.Ground} | {e.Spec.Orientation} | {MaskText(e.Mask)} | {conf} | {pools} |");
                }
            }
            File.WriteAllText(file, sb.ToString());
        }
    }
}
