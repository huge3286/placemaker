// In-game test suite: camera framing and PNG capture.
//   * One capture path for the scene: the main camera rendered into a window-sized render texture, 8 frames for TAA to
//     converge (the game's ScreenCaptureHelper does the same for save previews). "render" hides the overlay (the clean
//     scene: no outline, no notification icons, no street names); "overlay" keeps Citymaker's overlay (outline, ghost
//     symbols, entrance markers) with street names unhooked and notification icons switched off by SuiteLook.
//   * "ui": the real screen with the panel, grabbed at the end of the frame (what a person sees).
// The window is set to the shot size (SuiteLook), so every mode has the same size, aspect and framing.

using System;
using System.Collections.Generic;
using System.IO;
using Colossal.UI;
using Game;
using Game.Rendering;
using Game.UI;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Citymaker.Game.Suite
{
    internal static class SuiteCapture
    {
        private static CameraUpdateSystem Cams => World.DefaultGameObjectInjectionWorld?.GetExistingSystemManaged<CameraUpdateSystem>();

        /// <summary>Points the normal game camera at a world point: yaw/pitch in degrees, distance in metres.</summary>
        public static bool Aim(float3 pivot, float yaw, float pitch, float distance)
        {
            try
            {
                var cams = Cams;
                var c = cams?.gamePlayController;
                if (c == null) return false;
                if (!ReferenceEquals(cams.activeCameraController, c))
                {
                    c.TryMatchPosition(cams.activeCameraController);
                    cams.activeCameraController = c;
                }
                c.pivot = new Vector3(pivot.x, pivot.y, pivot.z);
                c.angle = new float2(yaw, pitch);
                c.zoom = distance;
                return true;
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Citymaker suite: camera aim failed: " + e.Message);
                return false;
            }
        }

        private static float? s_savedFov;

        /// <summary>Sets the gameplay camera's vertical field of view (null = back to the game's own).</summary>
        public static void Fov(float? degrees)
        {
            try
            {
                var c = Cams?.gamePlayController;
                if (c == null) return;
                ref var lens = ref c.lens;
                if (degrees.HasValue)
                {
                    if (!s_savedFov.HasValue) s_savedFov = lens.FieldOfView;
                    lens.FieldOfView = degrees.Value;
                }
                else if (s_savedFov.HasValue)
                {
                    lens.FieldOfView = s_savedFov.Value;
                    s_savedFov = null;
                }
            }
            catch (Exception e) { Mod.Log.Warn("Citymaker suite: camera fov failed: " + e.Message); }
        }

        /// <summary>The game camera controller's pivot is within <paramref name="tolerance"/> m of the point (x, z).</summary>
        public static bool PivotNear(float3 p, float tolerance)
        {
            try
            {
                var c = Cams?.gamePlayController;
                if (c == null) return true;
                var v = c.pivot;
                return math.distance(new float2(v.x, v.z), p.xz) <= tolerance;
            }
            catch (Exception) { return true; }
        }

        public static float Zoom()
        {
            try { return Cams?.gamePlayController?.zoom ?? 0f; }
            catch (Exception) { return 0f; }
        }

        /// <summary>What the rendered camera actually shows relative to a pivot: pitch, yaw, distance (the game lifts the eye over terrain and clamps tilt).</summary>
        public static (float Pitch, float Yaw, float Distance, float Fov, float Aspect) Effective(float3 pivot)
        {
            var cam = Camera.main;
            if (cam == null) return (float.NaN, float.NaN, float.NaN, float.NaN, float.NaN);
            float3 d = (float3)cam.transform.position - pivot;
            float h = math.length(d.xz);
            float pitch = math.degrees(math.atan2(d.y, h));
            float yaw = math.degrees(math.atan2(-d.x, -d.z));
            if (yaw < 0f) yaw += 360f;
            return (pitch, yaw, math.length(d), cam.fieldOfView, cam.aspect);
        }

        // ───────────── engine signals for the settle gate ─────────────

        private static object s_tss;
        private static System.Reflection.PropertyInfo[] s_tssProps;
        private static readonly string[] TssNames =
        {
            "VTMaterialsLeftToLoadCount", "VTMaterialsDuplicatesToProcessCount", "busyRequestsCount",
            "tilesFilledFromCPUCacheCount", "perSurfaceAsyncTileReadCount", "perTextureAsyncTileReadCount", "midMipAsyncTextureReadCount",
        };

        /// <summary>
        /// Virtual texturing state (TextureStreamingSystem, by reflection): [left to init, duplicates to process, busy requests,
        /// then four cumulative tile counters]. Null when unavailable.
        /// </summary>
        public static long[] Streaming(World world)
        {
            try
            {
                if (s_tss == null)
                {
                    foreach (var s in world.Systems)
                        if (s.GetType().Name == "TextureStreamingSystem") { s_tss = s; break; }
                    if (s_tss == null) return null;
                    var t = s_tss.GetType();
                    s_tssProps = new System.Reflection.PropertyInfo[TssNames.Length];
                    for (int i = 0; i < TssNames.Length; i++) s_tssProps[i] = t.GetProperty(TssNames[i]);
                }
                var r = new long[TssNames.Length];
                for (int i = 0; i < r.Length; i++) r[i] = s_tssProps[i] != null ? Convert.ToInt64(s_tssProps[i].GetValue(s_tss)) : -1;
                return r;
            }
            catch (Exception) { return null; }
        }

        public static string[] StreamingNames => TssNames;

        public static string CameraState()
        {
            try
            {
                var c = Cams?.gamePlayController;
                if (c == null) return "no camera";
                return $"pivot {c.pivot} angle {c.angle} zoom {c.zoom:F0} cam {Camera.main?.transform.position}";
            }
            catch (Exception e) { return e.Message; }
        }

        // ───────────── street names (AggregateRenderSystem draws them from a render-pipeline callback) ─────────────

        private static Action<ScriptableRenderContext, List<Camera>> s_labels;
        private static bool s_labelsOff;

        /// <summary>Street-name labels on or off (they are random per launch and sit on top of the build in overlay shots).</summary>
        public static void Labels(bool on)
        {
            try
            {
                if (s_labels == null)
                {
                    var sys = World.DefaultGameObjectInjectionWorld?.GetExistingSystemManaged<AggregateRenderSystem>();
                    var mi = typeof(AggregateRenderSystem).GetMethod("Render", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (sys == null || mi == null) return;
                    s_labels = (Action<ScriptableRenderContext, List<Camera>>)Delegate.CreateDelegate(typeof(Action<ScriptableRenderContext, List<Camera>>), sys, mi);
                }
                if (!on && !s_labelsOff) { RenderPipelineManager.beginContextRendering -= s_labels; s_labelsOff = true; }
                else if (on && s_labelsOff) { RenderPipelineManager.beginContextRendering += s_labels; s_labelsOff = false; }
            }
            catch (Exception e) { Mod.Log.Warn("Citymaker suite: labels toggle failed: " + e.Message); }
        }

        public static bool LabelsOff => s_labelsOff;

        // ───────────── capture ─────────────

        private static Material s_compose;

        /// <summary>Captures use SMAA (deterministic) instead of the game's TAA.</summary>
        public static bool Deterministic = true;

        /// <summary>The anti-aliasing the main camera uses (for the record).</summary>
        public static string CameraAa()
        {
            try { var hd = Camera.main?.GetComponent<HDAdditionalCameraData>(); return hd != null ? hd.antialiasing.ToString() : "?"; } catch (Exception e) { return e.Message; }
        }

        /// <summary>
        /// The main camera rendered into a new texture of the given size (8 renders for TAA, like the game's save
        /// preview). overlay=false hides the overlay (outline, icons, labels); true keeps Citymaker's overlay.
        /// </summary>
        public static Texture2D RenderTexture2D(int width, int height, bool overlay, out string error)
        {
            error = null;
            RenderTexture rt = null, dest = null;
            var previous = RenderTexture.active;
            var rs = World.DefaultGameObjectInjectionWorld?.GetExistingSystemManaged<RenderingSystem>();
            bool uiWas = UIManager.defaultUISystem != null && UIManager.defaultUISystem.enabled;
            bool hideWas = rs != null && rs.hideOverlay;
            var cam = Camera.main;
            RenderTexture camTarget = null;
            try
            {
                if (cam == null) { error = "no main camera"; return null; }
                HDRPDotsInputs.punctualLightsJobHandle.Complete();
                var fmt = (QualitySettings.GetRenderPipelineAssetAt(QualitySettings.GetQualityLevel()) as HDRenderPipelineAsset).currentPlatformRenderPipelineSettings.colorBufferFormat;
                rt = new RenderTexture(width, height, 16, (GraphicsFormat)fmt, 0);
                dest = ScreenCaptureHelper.CreateRenderTarget("Citymaker Suite Shot", width, height);
                if (!overlay) Colossal.CustomPassCache.SetPassEnabled("Outlines Pass", enabled: false);
                if (UIManager.defaultUISystem != null) UIManager.defaultUISystem.enabled = false;
                if (rs != null) rs.hideOverlay = !overlay;
                camTarget = cam.targetTexture;
                cam.forceIntoRenderTexture = true;
                cam.targetTexture = rt;
                // SMAA for the capture: no temporal jitter, so two captures of a still scene are the same picture (TAA's
                // 8-frame restart from a fresh history differs on foliage every time)
                var hd = cam.GetComponent<HDAdditionalCameraData>();
                var aaWas = hd != null ? hd.antialiasing : default;
                if (hd != null && Deterministic) hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                try { for (int i = 0; i < 8; i++) cam.Render(); }
                finally { if (hd != null) hd.antialiasing = aaWas; }
                cam.targetTexture = camTarget;
                cam.forceIntoRenderTexture = false;
                if (s_compose == null) s_compose = new Material(Shader.Find("Hidden/ScreenCaptureCompose"));
                s_compose.DisableKeyword("STYLIZE");
                Graphics.Blit(rt, dest, s_compose, 0);
                RenderTexture.active = dest;
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply(false);
                return tex;
            }
            catch (Exception e) { error = e.GetType().Name + ": " + e.Message; return null; }
            finally
            {
                if (cam != null && cam.targetTexture == rt) { cam.targetTexture = camTarget; cam.forceIntoRenderTexture = false; }
                if (rs != null) rs.hideOverlay = hideWas;
                if (UIManager.defaultUISystem != null) UIManager.defaultUISystem.enabled = uiWas;
                if (!overlay) Colossal.CustomPassCache.SetPassEnabled("Outlines Pass", enabled: true);
                RenderTexture.active = previous;
                if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); }
                if (dest != null) { dest.Release(); UnityEngine.Object.Destroy(dest); }
            }
        }

        /// <summary>The back buffer with the UI (call after WaitForEndOfFrame); the caller destroys it.</summary>
        public static Texture2D GrabTexture2D(out string error)
        {
            error = null;
            try
            {
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                if (tex == null) { error = "no screen texture"; return null; }
                return tex;
            }
            catch (Exception e) { error = e.GetType().Name + ": " + e.Message; return null; }
        }

        public static string RenderPng(string path, int width, int height, bool overlay = false)
        {
            var t = RenderTexture2D(width, height, overlay, out var err);
            if (t == null) return err;
            try { File.WriteAllBytes(path, t.EncodeToPNG()); return null; }
            catch (Exception e) { return e.Message; }
            finally { UnityEngine.Object.Destroy(t); }
        }

        // ───────────── image metrics (full resolution) ─────────────

        public struct Diff
        {
            /// <summary>Pixels whose largest channel difference is at least 16 (and 8).</summary>
            public int Changed16, Changed8;
            /// <summary>Largest mean (of the per-pixel largest channel difference) over 32x32 tiles.</summary>
            public float TileMax;
            public int TileX, TileY;
            public float Mean;
            public bool Comparable;
        }

        /// <summary>Compares two captures; rows from <paramref name="topExclude"/> (fraction of the height, from the top) up are skipped (the far distance of a low view).</summary>
        public static Diff Compare(Texture2D a, Texture2D b, float topExclude = 0f)
        {
            var d = new Diff();
            if (a == null || b == null || a.width != b.width || a.height != b.height) return d;
            var pa = a.GetPixels32();
            var pb = b.GetPixels32();
            int w = a.width, h = a.height;
            int tw = (w + 31) / 32, th = (h + 31) / 32;
            var tiles = new long[tw * th];
            var counts = new int[tw * th];
            long sum = 0;
            int yMax = (int)(h * (1f - math.clamp(topExclude, 0f, 0.9f)));
            for (int y = 0; y < yMax; y++)
            {
                int row = y * w, ty = (y >> 5) * tw;
                for (int x = 0; x < w; x++)
                {
                    var p = pa[row + x];
                    var q = pb[row + x];
                    int dr = p.r - q.r; if (dr < 0) dr = -dr;
                    int dg = p.g - q.g; if (dg < 0) dg = -dg;
                    int db = p.b - q.b; if (db < 0) db = -db;
                    int m = dr > dg ? (dr > db ? dr : db) : (dg > db ? dg : db);
                    if (m >= 16) d.Changed16++;
                    if (m >= 8) d.Changed8++;
                    sum += m;
                    int t = ty + (x >> 5);
                    tiles[t] += m;
                    counts[t]++;
                }
            }
            for (int i = 0; i < tiles.Length; i++)
            {
                if (counts[i] == 0) continue;
                float mean = tiles[i] / (float)counts[i];
                if (mean > d.TileMax) { d.TileMax = mean; d.TileX = i % tw; d.TileY = i / tw; }
            }
            d.Mean = sum / (float)(w * math.max(1, yMax));
            d.Comparable = true;
            return d;
        }

        /// <summary>Mean luma (Rec. 601, 0-255) of a capture.</summary>
        public static float MeanLuma(Texture2D t)
        {
            if (t == null) return float.NaN;
            var p = t.GetPixels32();
            double s = 0;
            for (int i = 0; i < p.Length; i += 3) s += 0.299 * p[i].r + 0.587 * p[i].g + 0.114 * p[i].b;
            return (float)(s / ((p.Length + 2) / 3));
        }

        /// <summary>A heat map of where two captures differ (orange = delta >= 16, dark red = >= 8), over a dimmed copy of b.</summary>
        public static string Heatmap(Texture2D a, Texture2D b, string path)
        {
            if (a == null || b == null || a.width != b.width || a.height != b.height) return "not comparable";
            Texture2D o = null;
            try
            {
                var pa = a.GetPixels32();
                var pb = b.GetPixels32();
                var po = new Color32[pa.Length];
                for (int i = 0; i < pa.Length; i++)
                {
                    var p = pa[i];
                    var q = pb[i];
                    int m = Math.Max(Math.Abs(p.r - q.r), Math.Max(Math.Abs(p.g - q.g), Math.Abs(p.b - q.b)));
                    byte g = (byte)((q.r + q.g + q.b) / 9);
                    po[i] = m >= 16 ? new Color32(255, 140, 0, 255) : m >= 8 ? new Color32(140, 20, 20, 255) : new Color32(g, g, g, 255);
                }
                o = new Texture2D(a.width, a.height, TextureFormat.RGBA32, false);
                o.SetPixels32(po);
                o.Apply(false);
                File.WriteAllBytes(path, o.EncodeToPNG());
                return null;
            }
            catch (Exception e) { return e.Message; }
            finally { if (o != null) UnityEngine.Object.Destroy(o); }
        }

        /// <summary>A world point on the screen (pixels, origin bottom-left as Unity's), z = depth in front of the camera.</summary>
        public static Vector3 ToScreen(float3 world)
        {
            var cam = Camera.main;
            return cam == null ? new Vector3(float.NaN, float.NaN, -1f) : cam.WorldToScreenPoint(world);
        }
    }
}
