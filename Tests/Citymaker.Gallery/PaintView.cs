// Citymaker gallery — terrain paint drawn truthfully (s33, docs/TERRAIN-PAINT.md).
//
// The bare ground inside the area is the map's procedural grass, painted materials are composited over it with their
// real (census) colours: per material the 4-bit weight the splatmap would store (strength quantised to 1/15), blended
// in slot order (Extra1..4) — the same max-merge raster the game paints (PaintRaster stamps). Real terrain albedo is
// dark (grass ≈ #444b36), so everything terrain is shown at one fixed exposure (×3.2 in linear light, chroma ×1.35):
// relations between materials stay true, objects on top stay readable. --map arid|tropic|… renders with another
// map's material slots (planners remap their intents to it). --paint adds per-type ground paint variants and a
// toolkit sheet (_paint/toolkit) showing every primitive.

using System;
using System.Collections.Generic;
using System.Linq;
using Citymaker.Core;
using Citymaker.Core.Build;
using Citymaker.Core.Paint;
using Unity.Mathematics;

namespace Citymaker.Gallery
{
    public static class PaintView
    {
        public const float Exposure = 3.2f, Chroma = 1.35f;

        /// <summary>Display colour of a real terrain albedo (sRGB 0..1).</summary>
        public static Rgba Display(float3 srgb, float a = 1f)
        {
            var lin = TerrainPalette.Linear(srgb) * Exposure;
            var s = TerrainPalette.Srgb(lin);
            float grey = (s.x + s.y + s.z) / 3f;
            s = math.saturate(grey + (s - grey) * Chroma);
            return new Rgba(s.x, s.y, s.z, a);
        }

        public static Rgba Lerp(Rgba a, Rgba b, float t) => new Rgba(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, 1f);

        /// <summary>Named map palettes for --map (the sets that ship in Blob_Environment, as a map maker would assign them).</summary>
        public static TerrainPalette ForMap(string name)
        {
            switch ((name ?? "").ToLowerInvariant())
            {
                case "arid": return Make("arid (custom map)", "Grass_dry_arid_01_Basecolor", "Grass_fertile_arid_01_Basecolor", "Dirt_arid_01_Basecolor", "Sand_arid_01_Basecolor", "Grass_arid_01_Basecolor");
                case "mediterranean": return Make("mediterranean (custom map)", "Grass_dry_mediterranean_01_Basecolor", "Grass_fertile_mediterranean_01_Basecolor", "Dirt_mediterranean_01_Basecolor", "Sand_mediterranean_01_Basecolor", "Grass_mediterranean_01_Basecolor");
                case "tropic": return Make("tropic (custom map)", "Grass_dry_tropic_01_Basecolor", "Forest_tropic_01_Basecolor", "Dirt_tropic_01_Basecolor", "Sand_tropic_01_Basecolor", "Grass_tropic_01_Basecolor");
                case "alpine": return Make("alpine (custom map)", "Grass_dry_alpine_01_Basecolor", "Grass_fertile_temperate_01_Basecolor", "Gravel_alpine_Basecolor_01", "Sand_alpine_01_Basecolor", "Grass_temperate_01_Basecolor");
                // A map maker who put the slots in another order (sand first): intents must follow the names.
                case "shuffled": return Make("shuffled slots", "Sand_temperate_01_Basecolor", "Light_dirt_temperate_01_Basecolor", "Forest_temperate_01_Basecolor", "Grass_Dry_Temperate_01_Basecolor", "Grass_temperate_01_Basecolor");
                default: return TerrainPalette.Vanilla();
            }
        }

        private static TerrainPalette Make(string label, string e1, string e2, string e3, string e4, string grass)
        {
            var p = new TerrainPalette { Name = label, BaseGrass = TerrainPalette.KnownColor(grass) ?? TerrainPalette.VanillaGrass, BaseDirt = TerrainPalette.VanillaDirt, BaseRock = TerrainPalette.VanillaRock };
            var t = new[] { null, e1, e2, e3, e4 };
            for (int i = 1; i <= 4; i++) p.Slots[i] = new TerrainSlot { Index = i, Texture = t[i], Color = TerrainPalette.KnownColor(t[i]) ?? TerrainPalette.Vanilla().Slots[i].Color, Measured = true };
            return p;
        }

        /// <summary>The bare ground (procedural grass) inside the area and outside it (muted).</summary>
        public static Rgba Ground(TerrainPalette pal) => Display(pal.BaseGrass);
        public static Rgba Outside(TerrainPalette pal) => Lerp(Display(pal.BaseGrass), Rgba.Hex("#d9d6ce"), 0.6f);

        /// <summary>
        /// Paints the terrain: base grass inside the ring (when <paramref name="fillBase"/>), then the composited material weights.
        /// Returns the paint layers present (for the legend).
        /// </summary>
        public static HashSet<int> Draw(Canvas cv, ComposedPlan plan, float2[] ring, float2 min, float2 max, TerrainPalette pal, float waterZ, bool underWater, bool fillBase)
        {
            var present = new HashSet<int>();
            if (fillBase) cv.FillPolygon(ring, Ground(pal));
            var paints = plan.Layers.Where(l => l != null).SelectMany(l => l.Paint).Where(p => p?.Ring != null && p.Ring.Length >= 3).ToList();
            if (paints.Count == 0) return present;
            var stamps = PaintRaster.Build(paints, new PaintRasterOptions { Cell = 1f, Align = 1f, TileCells = 2048, MaxStamps = 64 }).Where(s => !s.Erase).ToList();
            if (stamps.Count == 0) return present;
            foreach (var s in stamps) present.Add(s.Layer);
            float2 smin = new float2(float.MaxValue), smax = new float2(float.MinValue);
            foreach (var s in stamps) { smin = math.min(smin, s.Min); smax = math.max(smax, s.Min + s.Size); }
            var cols = new Rgba[5];
            for (int i = 1; i <= 4; i++) cols[i] = Display(pal.ColorOf(i));
            var byLayer = new List<PaintStamp>[5];
            for (int i = 1; i <= 4; i++) byLayer[i] = stamps.Where(s => s.Layer == i).ToList();
            var baseCol = Ground(pal);
            cv.Shade(p =>
            {
                float total = 0f;
                var c = baseCol;
                var w = new float[5];
                for (int i = 1; i <= 4; i++)
                {
                    float v = 0f;
                    foreach (var s in byLayer[i]) v = math.max(v, s.Sample(p));
                    // The splatmap keeps 4 bits per material: what the player will actually see.
                    v = math.round(v * 15f) / 15f;
                    if (underWater && !float.IsNaN(waterZ) && p.y < waterZ) v *= 0.45f;
                    w[i] = v; total += v;
                }
                if (total <= 0f) return default;
                for (int i = 1; i <= 4; i++) if (w[i] > 0f) c = Lerp(c, cols[i], w[i]);
                // Alpha: full where painted, so the composite replaces the ground under it (base grass + weights).
                return c.WithA(math.saturate(total * 4f));
            }, math.max(min, smin), math.min(max, smax));
            return present;
        }

        /// <summary>Top-right chips: the materials this image paints, in the map's colours.</summary>
        public static void Legend(Canvas cv, TerrainPalette pal, HashSet<int> present)
        {
            if (present == null || present.Count == 0) return;
            var names = new[] { "", "dry grass", "forest floor", "dirt", "sand" };
            var list = present.Where(l => l >= 1 && l <= 4).OrderBy(l => l).ToList();
            int row = 13, w = 150, x = cv.W - w - 4, y = 6;
            cv.RectPx(x - 3, y - 3, w, row * (list.Count + 1) + 4, Rgba.Hex("#ffffff", 0.82f));
            cv.TextPx(x, y, "PAINT " + (pal.IsVanilla ? "VANILLA" : "CUSTOM MAP"), Rgba.Hex("#202020"), 1);
            int k = 1;
            foreach (var l in list)
            {
                int yy = y + k * row;
                cv.RectPx(x, yy, 9, 9, Display(pal.ColorOf(l)));
                string label = pal.IsVanilla ? names[l] : l + " " + Short(pal.Slots[l]?.Texture);
                cv.TextPx(x + 14, yy + 1, label, Rgba.Hex("#202020"), 1);
                k++;
            }
        }

        private static string Short(string tex)
        {
            if (string.IsNullOrEmpty(tex)) return "?";
            var s = tex.Replace("_Basecolor", "").Replace("_01", "").Replace("_", " ");
            return s.Length > 20 ? s.Substring(0, 20) : s;
        }

        // ───────────────────────── toolkit sheet ─────────────────────────

        /// <summary>A flat 176 × 100 m sheet with one panel per toolkit primitive (labels in <see cref="Labels"/>).</summary>
        public static (float3[] ring, ComposedPlan plan) Toolkit()
        {
            var ring = new[] { new float3(0, 0, 0), new float3(176, 0, 0), new float3(176, 0, 100), new float3(0, 0, 100) };
            var plan = new ComposedPlan();
            var l = new AreaPlan { GeneratorId = "_paint" };
            plan.Layers.Add(l);
            var kit = new PaintKit(l.Paint, null, 0x5EEDu);
            float2[] Panel(int col, int row) { float x0 = 2 + col * 44, y0 = 52 - row * 50; return new[] { new float2(x0, y0), new float2(x0 + 40, y0), new float2(x0 + 40, y0 + 44), new float2(x0, y0 + 44) }; }
            float2 C(int col, int row) => new float2(22 + col * 44, 74 - row * 50);
            // Top row: fill / half / speckle / linear.
            kit.Fill(Panel(0, 0), TerrainMaterials.Dirt, 1f, 3f, 1.2f);
            kit.Fill(Panel(1, 0), TerrainMaterials.Sand, 0.5f, 3f, 1.2f);
            kit.Speckle(Panel(2, 0), TerrainMaterials.Dry, 0.9f, 4f, 0.45f, 0.35f);
            kit.Gradient(Panel(3, 0), TerrainMaterials.Lush, 1f, C(3, 0) + new float2(-20, 0), C(3, 0) + new float2(20, 0), 1f, 0f, Falloff.Linear, 2f, 0.5f);
            // Bottom row: trail stroke / sphere gradients / blend / mulch rings.
            var trail = new List<float2>();
            for (int i = 0; i <= 24; i++) { float t = i / 24f; trail.Add(new float2(4 + t * 36, 8 + 30 * t + 6 * math.sin(t * 9f))); }
            kit.Stroke(trail, TerrainMaterials.Dirt, 0.9f, 3.2f, 1.1f, start: 1f, end: 0.35f, endFade: 4f, wobble: 0.35f, wobbleScale: 5f, knockout: true);
            kit.Stroke(new[] { new float2(6, 40), new float2(40, 40) }, TerrainMaterials.Dry, 0.6f, 6f, 2.5f);
            kit.Radial(C(1, 1) + new float2(-10, 8), TerrainMaterials.Lush, 1f, 0f, 9f, Falloff.Sphere);
            kit.Radial(C(1, 1) + new float2(10, 8), TerrainMaterials.Lush, 1f, 0f, 9f, Falloff.Fast);
            kit.Radial(C(1, 1) + new float2(-10, -11), TerrainMaterials.Dirt, 0.9f, 2f, 8f, Falloff.Smooth, 1.2f);
            kit.Radial(C(1, 1) + new float2(10, -11), TerrainMaterials.Sand, 1f, 0f, 9f, Falloff.Smooth, 0f, 0f, 2f, 0.6f);
            kit.Blend(PaintShape.Polygon(Panel(2, 1), 2.5f, 1f), PaintShape.Linear(C(2, 1) + new float2(-18, 0), C(2, 1) + new float2(18, 0), Falloff.Smooth, 0f, 1f),
                TerrainMaterials.Sand, TerrainMaterials.Dry, 1f, Panel(2, 1));
            var crowns = new List<PaintShape>();
            for (int i = 0; i < 9; i++) { var p = C(3, 1) + new float2((i % 3 - 1) * 12f, (i / 3 - 1) * 12f); crowns.Add(PaintShape.Radial(p, 1.4f, 5f, Falloff.Smooth, 0.8f, 3f, (uint)i + 7u)); l.Objects.Add(new PlannedObject { Role = ObjectRole.Tree, Position = new float3(p.x, 0, p.y), Scale = 1f }); }
            kit.Shape(PaintShape.Union(crowns), TerrainMaterials.Lush, 0.7f);
            return (ring, plan);
        }

        public static readonly (float2 at, string text)[] Labels =
        {
            (new float2(4, 98), "FILL"), (new float2(48, 98), "HALF 50%"), (new float2(92, 98), "SPECKLE"), (new float2(136, 98), "LINEAR"),
            (new float2(4, 48), "TRAIL, VERGE"), (new float2(48, 48), "SPHERES"), (new float2(92, 48), "BLEND"), (new float2(136, 48), "MULCH"),
        };
    }
}
