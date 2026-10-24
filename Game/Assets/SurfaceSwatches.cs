// Citymaker — texture swatches for surfaces without a UI icon (GPL-3.0).
//
// Vanilla SurfacePrefabs mostly have no UIObject icon (Asset Icon Library adds 17); ImageSystem then
// returns a "thumbnail://ThumbnailCamera/…" URL, which renders nothing for an area. As a fallback we
// render the surface's own albedo (RenderedArea.m_BaseColorMap, tinted with m_BaseColor) into a small
// PNG in ModsData/Citymaker/thumbs and serve it through a coui host location:
//   coui://citymaker-thumbs/<file>.png
// PNGs are cached on disk by prefab name; a few are generated per frame so a scan never stalls.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Colossal.IO.AssetDatabase;
using Game.Prefabs;
using Citymaker.Game.Settings;
using UnityEngine;

namespace Citymaker.Game.Assets
{
    public static class SurfaceSwatches
    {
        public const string Host = "citymaker-thumbs";
        private const int Size = 96;
        private static bool _hostRegistered;
        private static bool _disabled;

        public static string Folder
        {
            get
            {
                var dir = Path.Combine(ModPaths.DataFolder, "thumbs");
                try { Directory.CreateDirectory(dir); } catch (Exception) { }
                return dir;
            }
        }

        public static string FileName(string prefabName)
        {
            var sb = new StringBuilder("surface_");
            foreach (var c in prefabName ?? "")
                sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_');
            return sb.Append(".png").ToString();
        }

        public static string Url(string prefabName) => "coui://" + Host + "/" + FileName(prefabName);

        private static bool EnsureHost()
        {
            if (_hostRegistered) return true;
            try
            {
                Colossal.UI.UIManager.defaultUISystem.AddHostLocation(Host, Folder);
                _hostRegistered = true;
            }
            catch (Exception e)
            {
                _disabled = true;
                Mod.Log.Warn("Citymaker could not register the thumbnail host; surfaces keep the game's thumbnail: " + e.Message);
            }
            return _hostRegistered;
        }

        /// <summary>URL of a cached swatch, or null when none exists yet.</summary>
        public static string Cached(string prefabName)
        {
            if (_disabled || !EnsureHost()) return null;
            try { return File.Exists(Path.Combine(Folder, FileName(prefabName))) ? Url(prefabName) : null; }
            catch (Exception) { return null; }
        }

        /// <summary>Renders and caches the swatch of a surface prefab. Returns its URL or null.</summary>
        public static string Generate(PrefabBase prefab)
        {
            if (_disabled || prefab == null || !EnsureHost()) return null;
            var cached = Cached(prefab.name);
            if (cached != null) return cached;
            if (!prefab.TryGet<RenderedArea>(out var area) || area == null) return null;
            TextureAsset asset = null;
            bool loadedHere = false;
            RenderTexture rt = null;
            Texture2D readback = null;
            try
            {
                Texture tex = null;
                try
                {
                    asset = area.m_BaseColorMap;
                    if (asset != null)
                    {
                        loadedHere = !asset.isObjectLoaded;
                        tex = asset.Load();
                    }
                }
                catch (Exception) { tex = null; }
                if (tex == null && area.m_Material != null && area.m_Material.HasProperty("_BaseColorMap"))
                    tex = area.m_Material.GetTexture("_BaseColorMap");

                readback = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                var tint = area.m_BaseColor;
                if (tex != null)
                {
                    rt = RenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    Graphics.Blit(tex, rt);
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    readback.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                    RenderTexture.active = prev;
                    var px = readback.GetPixels32();
                    for (int i = 0; i < px.Length; i++)
                        px[i] = new Color32((byte)(px[i].r * tint.r), (byte)(px[i].g * tint.g), (byte)(px[i].b * tint.b), 255);
                    readback.SetPixels32(px);
                }
                else
                {
                    var c = (Color32)new Color(tint.r, tint.g, tint.b, 1f);
                    var px = new Color32[Size * Size];
                    for (int i = 0; i < px.Length; i++) px[i] = c;
                    readback.SetPixels32(px);
                }
                readback.Apply(false);
                var bytes = readback.EncodeToPNG();
                if (bytes == null || bytes.Length == 0) return null;
                File.WriteAllBytes(Path.Combine(Folder, FileName(prefab.name)), bytes);
                return Url(prefab.name);
            }
            catch (Exception e)
            {
                Mod.Log.Info($"Citymaker could not render a swatch for surface {prefab.name}: {e.Message}");
                return null;
            }
            finally
            {
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (readback != null) UnityEngine.Object.Destroy(readback);
                if (loadedHere && asset != null) { try { asset.Unload(); } catch (Exception) { } }
            }
        }
    }
}
