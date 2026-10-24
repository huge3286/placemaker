// Citymaker — reads the current map's terrain paint materials into a Core TerrainPalette (s33). GPL-3.0.
//
// CS2 1.6.2f1 (decompiled): TerrainMaterialSystem.currentTerrainRenderSettings is the map's TerrainRenderSettingsPrefab
// (vanilla maps: "DefaultTerrainRenderSettings"; custom maps may carry their own, edited in the map editor). Its
// m_Extra1..4Diffuse are AssetReference<TextureAsset>; the implicit conversion gives the TextureAsset (metadata only,
// no texture load) whose name identifies the texture. Known textures (the census in docs/TERRAIN-PAINT.md) take their
// measured colour; an unknown one is measured once from the texture the terrain system already bound globally
// (colossal_TerrainExtraNDiffuse): blit to a 64² render texture, read back, average in linear space. Runs on the main
// thread, cached per settings entity; logged once per change so a playtest log shows what each slot looks like.

using System;
using Game.Prefabs;
using Game.Prefabs.Terrain;
using Game.Rendering;
using Citymaker.Core.Paint;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Citymaker.Game.Preview
{
    public static class TerrainPaletteReader
    {
        private static Entity s_entity = Entity.Null;
        private static string s_key;
        private static TerrainPalette s_palette;

        /// <summary>The last palette read (null before the first capture). Main thread.</summary>
        public static TerrainPalette Last => s_palette;

        private static readonly string[] ExtraIds = { null, "colossal_TerrainExtra1Diffuse", "colossal_TerrainExtra2Diffuse", "colossal_TerrainExtra3Diffuse", "colossal_TerrainExtra4Diffuse" };

        /// <summary>The current map's palette (null when it cannot be read: planners then assume vanilla).</summary>
        public static TerrainPalette Current(World world)
        {
            try
            {
                var materials = world?.GetExistingSystemManaged<TerrainMaterialSystem>();
                var prefabs = world?.GetExistingSystemManaged<PrefabSystem>();
                if (materials == null || prefabs == null) return s_palette;
                var e = materials.currentTerrainRenderSettings;
                if (e == Entity.Null || !prefabs.TryGetPrefab(e, out TerrainRenderSettingsPrefab settings) || settings == null) return s_palette;
                string key = settings.name + "|" + Name(settings.m_Extra1Diffuse) + "|" + Name(settings.m_Extra2Diffuse) + "|" + Name(settings.m_Extra3Diffuse) + "|" + Name(settings.m_Extra4Diffuse);
                if (e == s_entity && key == s_key && s_palette != null) return s_palette;
                s_entity = e; s_key = key;
                s_palette = Read(settings);
                Mod.Log.Info("Citymaker terrain paint materials: " + s_palette.Describe());
                return s_palette;
            }
            catch (Exception ex)
            {
                Mod.Log.Warn("Citymaker: reading the map's terrain materials failed (" + ex.Message + "); assuming the vanilla slots.");
                return s_palette;
            }
        }

        private static TerrainPalette Read(TerrainRenderSettingsPrefab s)
        {
            var p = new TerrainPalette { Name = s.name ?? "" };
            p.Slots[1] = Slot(1, s.m_Extra1Diffuse, s.m_Extra1DirtOverride, s.m_Extra1SplatRange, s.m_Extra1BlurDepth, s.m_Extra1HeightOffset);
            p.Slots[2] = Slot(2, s.m_Extra2Diffuse, s.m_Extra2DirtOverride, s.m_Extra2SplatRange, s.m_Extra2BlurDepth, s.m_Extra2HeightOffset);
            p.Slots[3] = Slot(3, s.m_Extra3Diffuse, s.m_Extra3DirtOverride, s.m_Extra3SplatRange, s.m_Extra3BlurDepth, s.m_Extra3HeightOffset);
            p.Slots[4] = Slot(4, s.m_Extra4Diffuse, s.m_Extra4DirtOverride, s.m_Extra4SplatRange, s.m_Extra4BlurDepth, s.m_Extra4HeightOffset);
            p.BaseGrass = TerrainPalette.KnownColor(Name(s.m_GrassDiffuse)) ?? Measure("colossal_TerrainGrassDiffuse") ?? TerrainPalette.VanillaGrass;
            p.BaseDirt = TerrainPalette.KnownColor(Name(s.m_DirtDiffuse)) ?? Measure("colossal_TerrainDirtDiffuse") ?? TerrainPalette.VanillaDirt;
            p.BaseRock = TerrainPalette.KnownColor(Name(s.m_RockDiffuse)) ?? Measure("colossal_TerrainRockDiffuse") ?? TerrainPalette.VanillaRock;
            return p;
        }

        private static TerrainSlot Slot(int i, Colossal.IO.AssetDatabase.AssetReference<Colossal.IO.AssetDatabase.TextureAsset> tex,
            float dirtOverride, float splatRange, float blurDepth, float heightOffset)
        {
            string name = Name(tex);
            var known = TerrainPalette.KnownColor(name);
            var measured = known ?? Measure(ExtraIds[i]);
            var vanilla = TerrainPalette.Vanilla().Slots[i];
            return new TerrainSlot
            {
                Index = i, Texture = name, Color = measured ?? vanilla.Color, Measured = measured.HasValue,
                DirtOverride = dirtOverride, SplatRange = splatRange, BlurDepth = blurDepth, HeightOffset = heightOffset,
            };
        }

        private static string Name(Colossal.IO.AssetDatabase.AssetReference<Colossal.IO.AssetDatabase.TextureAsset> r)
        {
            try
            {
                if (r == null) return "";
                Colossal.IO.AssetDatabase.TextureAsset t = r;
                return t?.name ?? "";
            }
            catch (Exception) { return ""; }
        }

        /// <summary>Mean sRGB colour of a globally bound terrain texture (null when unavailable).</summary>
        private static float3? Measure(string globalName)
        {
            if (string.IsNullOrEmpty(globalName)) return null;
            RenderTexture rt = null;
            var active = RenderTexture.active;
            Texture2D read = null;
            try
            {
                var tex = Shader.GetGlobalTexture(globalName);
                if (tex == null) return null;
                const int n = 64;
                rt = RenderTexture.GetTemporary(n, n, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                read = new Texture2D(n, n, TextureFormat.RGBA32, false, true);
                read.ReadPixels(new Rect(0, 0, n, n), 0, 0, false);
                read.Apply(false, false);
                var px = read.GetPixels();
                double r = 0, g = 0, b = 0;
                foreach (var c in px) { r += c.r; g += c.g; b += c.b; }
                if (px.Length == 0) return null;
                var lin = new float3((float)(r / px.Length), (float)(g / px.Length), (float)(b / px.Length));
                return TerrainPalette.Srgb(lin);
            }
            catch (Exception) { return null; }
            finally
            {
                RenderTexture.active = active;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (read != null) UnityEngine.Object.Destroy(read);
            }
        }
    }
}
