// s30 — renders an in-game terrain dump (ModsData/Citymaker/debug/*.json, written with the setting "Debug: dump terrain
// after build"): what CS2 really built, offline.
//
//   dotnet run -c Release --project Tests/Citymaker.Gallery -- --terrain-dump <file.json | folder> [--out dir]
//
// Per dump: <name>.png  — hillshade of the live ground, water where the live surface has depth, 1 m contours, the
//                         built nets (paths tan, decks and quays brown, fences dark), objects as dots, the build's outline,
//                         every curtain (a texel pair the build made steeper than 50 % by > 0.6 m) as a red tick and the
//                         worst net seat circled magenta
//           <name>-cut.png — the change (after − before): blue = cut, red = fill, 0.5 m steps
//           <name>-3d.png  — oblique view and three cross-sections (before dashed grey, after brown, planned target
//                         orange, water blue) — the same views as the shore renders
// The metrics line is printed and written into the image.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Citymaker.Core;
using Citymaker.Core.Build;
using Unity.Mathematics;

namespace Citymaker.Gallery
{
    public static class DumpView
    {
        public static int Run(string path, string outDir)
        {
            var files = Directory.Exists(path) ? Directory.GetFiles(path, "*.json").OrderBy(f => f).ToArray() : new[] { path };
            Directory.CreateDirectory(outDir);
            foreach (var f in files)
            {
                try
                {
                    var d = TerrainDump.FromJson(File.ReadAllText(f));
                    string name = Path.GetFileNameWithoutExtension(f);
                    string metrics = d.Metrics();
                    Render(d, Path.Combine(outDir, name + ".png"), Path.Combine(outDir, name + "-cut.png"), Path.Combine(outDir, name + "-3d.png"), metrics);
                    string facts = string.Join(" ", d.Facts.Where(kv => kv.Key.StartsWith("walk") || kv.Key.StartsWith("edge") || kv.Key.StartsWith("wall") || kv.Key.StartsWith("hillside") || kv.Key.StartsWith("earth"))
                        .Select(kv => kv.Key + "=" + kv.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));
                    Console.WriteLine($"{name}: {d.Nx}x{d.Ny} @ {d.Cell:0.0} m, {d.Nets.Count} nets, {d.Objects.Count} objects | {metrics} | {facts}");
                }
                catch (Exception e) { Console.WriteLine($"{f}: {e.Message}"); }
            }
            return 0;
        }

        private static void Render(TerrainDump d, string top, string cut, string oblique, string metrics)
        {
            var min = d.Origin; var max = d.Origin + new float2(d.Nx - 1, d.Ny - 1) * d.Cell;
            float ext = math.cmax(max - min);
            float ppm = 900f / ext;
            int w = (int)((max.x - min.x) * ppm) + 1, h = (int)((max.y - min.y) * ppm) + 1;
            var after = d.AfterAt; var before = d.BeforeAt;
            Func<float2, float> water = p => { float v = d.Sample(d.Water, p); return math.isfinite(v) ? v : 0f; };

            // Top view.
            var cv = new Canvas(w, h + 40, min - new float2(0f, 40f / ppm), ppm, Rgba.Hex("#d5dcc6"), 2);
            cv.Shade(p =>
            {
                float sh = TerrainView.Shade(after, p);
                return sh < 1f ? new Rgba(0.05f, 0.08f, 0.12f, (1f - sh) * 0.85f) : new Rgba(1f, 1f, 0.95f, (sh - 1f) * 0.6f);
            }, min, max);
            float px = cv.Px(1.5f);
            cv.Shade(p =>
            {
                float dep = water(p);
                if (dep > 0.05f) return Rgba.Hex("#3f86c6", 0.3f + 0.6f * math.saturate(dep / 3.5f));
                float a = after(p), ax = after(p + new float2(px, 0)), az = after(p + new float2(0, px));
                bool contour = math.floor(a) != math.floor(ax) || math.floor(a) != math.floor(az);
                return contour ? new Rgba(0.2f, 0.2f, 0.2f, 0.25f) : new Rgba(0, 0, 0, 0);
            }, min, max);
            foreach (var r in d.Outlines) cv.StrokePolyline(r, cv.Px(1.5f), Rgba.Hex("#1f2f4f"), closed: true);
            foreach (var n in d.Nets)
            {
                var pts = new List<float2>();
                for (int i = 0; i <= 16; i++) pts.Add(Composer.Bezier(n.A, n.B, n.C, n.D, i / 16f).xz);
                var col = n.Quay ? Rgba.Hex("#8c8c8c") : !n.Follow ? Rgba.Hex("#8b5a2b") : n.Width < 1f ? Rgba.Hex("#333333") : Rgba.Hex("#f2e3c0");
                cv.StrokePolyline(pts, math.max(cv.Px(1.5f), n.Width), col.WithA(0.9f));
            }
            foreach (var o in d.Objects) cv.FillCircle(o.Position.xz, cv.Px(2.5f), Rgba.Hex("#6a3fb0"));
            // Curtains: every offending texel pair.
            float t = TerrainMetrics.Texel;
            for (float z = min.y; z + t <= max.y; z += t)
                for (float x = min.x; x + t <= max.x; x += t)
                {
                    var p = new float2(x, z);
                    foreach (var dd in new[] { new float2(t, 0), new float2(0, t) })
                    {
                        float df = math.abs(after(p + dd) - after(p)), dob = math.abs(before(p + dd) - before(p));
                        if (df > TerrainMetrics.SheetGrade * t && dob <= TerrainMetrics.SheetGrade * t && df - dob > TerrainMetrics.CurtainLimit)
                            cv.StrokePolyline(new[] { p, p + dd }, cv.Px(3f), Rgba.Hex("#e01010"));
                    }
                }
            var nets = d.Nets.Select(n => (n.A, n.B, n.C, n.D, math.max(0.5f, n.Width * 0.5f), n.Follow ? TerrainMetrics.SeatFollow : n.Quay ? TerrainMetrics.SeatQuay : TerrainMetrics.SeatDeck)).ToList();
            var seat = TerrainMetrics.Seats(nets, after);
            if (seat.Cross > 0.3f || seat.Gap > 0.3f) cv.StrokeCircle(seat.At, 5f, cv.Px(2.5f), Rgba.Hex("#d020d0"));
            cv.TextPx(6, h + 8, Trim(metrics), Rgba.Hex("#202020"), 1);
            cv.TextPx(6, h + 22, "GROUP " + d.Group + "  " + d.When, Rgba.Hex("#202020"), 1);
            cv.SavePng(top);

            // Cut/fill.
            var cc = new Canvas(w, h, min, ppm, Rgba.Hex("#f4f4f0"), 2);
            cc.Shade(p =>
            {
                float c = after(p) - before(p);
                if (math.abs(c) < 0.1f) return new Rgba(0, 0, 0, 0);
                float k = math.saturate(math.abs(c) / 4f);
                float band = (math.floor(math.abs(c) / 0.5f) % 2f) * 0.12f;
                return c < 0f ? new Rgba(0.15f, 0.35f, 0.85f, 0.25f + 0.6f * k - band) : new Rgba(0.85f, 0.25f, 0.15f, 0.25f + 0.6f * k - band);
            }, min, max);
            foreach (var n in d.Nets)
            {
                var pts = new List<float2>();
                for (int i = 0; i <= 16; i++) pts.Add(Composer.Bezier(n.A, n.B, n.C, n.D, i / 16f).xz);
                cc.StrokePolyline(pts, cc.Px(1.5f), Rgba.Hex("#222222", 0.7f));
            }
            cc.SavePng(cut);

            // Oblique + sections: the shore render's view, fed with the dump's grids.
            float level = math.isfinite(d.WaterLevel) ? d.WaterLevel : float.NaN;
            var plan = new ComposedPlan();
            var layer = new AreaPlan();
            foreach (var n in d.Nets) layer.Nets.Add(new PlannedNet { A = n.A, B = n.B, C = n.C, D = n.D, FollowTerrain = n.Follow, Role = NetRole.Pathway });
            foreach (var o in d.Objects) layer.Objects.Add(new PlannedObject { Position = o.Position, Yaw = o.Yaw, Role = ObjectRole.Furniture });
            plan.Layers.Add(layer);
            TerrainView.Oblique(oblique, cv, plan, new Catalog(), before, after, level, min, max);
        }

        private static string Trim(string s) => s.Length > 140 ? s.Substring(0, 140) : s;
    }
}
