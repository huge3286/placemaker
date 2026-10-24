// In-game test suite: a deterministic, readable world for every shot (set once after the fixture, re-checked per
// scenario). The user's hints: noon, more light, no notification icons, nothing tall in the way — plus what makes a
// pixel-stability check meaningful: no weather, no wind sway, no auto exposure drift, no LOD cross-fade dither.
//   * window = shot size (both shot modes share size, aspect and framing);
//   * sun: noon in the summer of the hemisphere the sun is set for (latitude 45 N, day 172, 12:00 local solar time);
//   * a look volume above the game's lighting volume (priority 1500 > 1000, < photo mode 2000): fixed exposure
//     calibrated to a target brightness, lifted indirect light, wind strength 0;
//   * climate overrides: no rain, clouds, fog or aurora; a warm temperature (no snow);
//   * notification icons off the way the game's own debug tab does it (tool-error icons stay);
//   * street-name labels unhooked; full level of detail without cross-fade dither.
// Nothing here is saved: overrides live in this session only.

using System;
using System.Collections;
using System.Collections.Generic;
using Game.Common;
using Game.Notifications;
using Game.Prefabs;
using Game.Rendering;
using Game.Simulation;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Citymaker.Game.Suite
{
    internal sealed class SuiteLook
    {
        private readonly World _world;
        private readonly SuiteRunConfig _cfg;
        public readonly JObject Record = new JObject();
        private Volume _volume;
        private Exposure _exposure;
        public float Ev = float.NaN;

        public SuiteLook(World world, SuiteRunConfig cfg)
        {
            _world = world;
            _cfg = cfg;
        }

        public bool Enabled => !string.Equals(_cfg.look, "game", StringComparison.OrdinalIgnoreCase);

        // ───────────── window ─────────────

        /// <summary>Sets the window to the shot size (windowed) and pins the game's own setting to it so a later Apply cannot undo it.</summary>
        public IEnumerator Window(Action<string> say)
        {
            int w = _cfg.shotWidth, h = _cfg.shotHeight;
            Record["windowRequested"] = $"{w}x{h}";
            Record["windowBefore"] = $"{Screen.width}x{Screen.height}";
            if (Screen.width != w || Screen.height != h)
            {
                try
                {
                    Screen.SetResolution(w, h, FullScreenMode.Windowed);
                    var g = global::Game.Settings.SharedSettings.instance?.graphics;
                    if (g != null)
                    {
                        var f = g.GetType().GetField("m_Resolution", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (f != null)
                        {
                            var r = (global::Game.Settings.ScreenResolution)f.GetValue(g);
                            r.width = w;
                            r.height = h;
                            f.SetValue(g, r);
                        }
                    }
                }
                catch (Exception e) { say("window resize failed: " + e.Message); }
                for (int i = 0; i < 30 && (Screen.width != w || Screen.height != h); i++) yield return null;
            }
            Record["window"] = $"{Screen.width}x{Screen.height}";
            Record["windowOk"] = Screen.width == w && Screen.height == h;
            say($"window {Screen.width}x{Screen.height} (asked {w}x{h})");
        }

        public bool WindowOk => Screen.width == _cfg.shotWidth && Screen.height == _cfg.shotHeight;

        // ───────────── sun, climate, icons, labels, LOD ─────────────

        /// <summary>The real mouse must not move the camera (edge scrolling while the cursor rests on a window edge).</summary>
        public void CameraInputOff()
        {
            try
            {
                var c = _world.GetExistingSystemManaged<CameraUpdateSystem>()?.gamePlayController;
                if (c != null && c.edgeScrolling) c.edgeScrolling = false;
                Record["edgeScrolling"] = false;
            }
            catch (Exception e) { Record["edgeScrollingError"] = e.Message; }
        }

        public void Apply(Action<string> say)
        {
            CameraInputOff();
            if (!Enabled) { Record["mode"] = "game"; return; }
            Record["mode"] = "suite";
            try { Sun(); } catch (Exception e) { Record["sunError"] = e.Message; say("sun: " + e.Message); }
            try { Climate(); } catch (Exception e) { Record["climateError"] = e.Message; say("climate: " + e.Message); }
            try { Volume(); } catch (Exception e) { Record["volumeError"] = e.Message; say("volume: " + e.Message); }
            try { Record["iconPrefabsHidden"] = HideIcons(); } catch (Exception e) { Record["iconsError"] = e.Message; say("icons: " + e.Message); }
            SuiteCapture.Labels(false);
            Record["streetLabels"] = SuiteCapture.LabelsOff ? "off" : "on";
            Detail();
            try
            {
                var gp = global::Game.Settings.SharedSettings.instance?.gameplay;
                if (gp != null) gp.snowVisual = false;
                Record["snowVisual"] = false;
            }
            catch (Exception e) { say("snow: " + e.Message); }
        }

        /// <summary>Per scenario: LOD and icons can be re-applied by the game's own settings and infoviews.</summary>
        public void Reassert()
        {
            CameraInputOff();
            if (!Enabled) return;
            Detail();
            try { HideIcons(); } catch (Exception) { }
            if (!SuiteCapture.LabelsOff) SuiteCapture.Labels(false);
        }

        private void Detail()
        {
            var rs = _world.GetExistingSystemManaged<RenderingSystem>();
            if (rs == null) return;
            rs.levelOfDetail = 1f;
            rs.lodCrossFade = false;
            Record["levelOfDetail"] = rs.levelOfDetail;
            Record["lodCrossFade"] = rs.lodCrossFade;
        }

        private void Sun()
        {
            var planet = _world.GetOrCreateSystemManaged<PlanetarySystem>();
            Record["mapLatitude"] = Math.Round(planet.latitude, 3);
            Record["mapLongitude"] = Math.Round(planet.longitude, 3);
            planet.overrideTime = true;
            if (_cfg.lookLatitude != 0f) planet.latitude = _cfg.lookLatitude;
            planet.year = 2024;
            planet.day = _cfg.lookDay;
            planet.time = _cfg.lookTime;
            Record["latitude"] = Math.Round(planet.latitude, 3);
            Record["day"] = planet.day;
            Record["time"] = planet.time;
        }

        /// <summary>Sun elevation / azimuth (degrees) as the light is placed this frame; azimuth 180 = south (-z).</summary>
        public (float Elevation, float Azimuth) SunAngles()
        {
            var planet = _world.GetOrCreateSystemManaged<PlanetarySystem>();
            var sun = planet.SunLight;
            if (!sun.isValid) return (float.NaN, float.NaN);
            float3 p = math.normalizesafe((float3)sun.transform.position);
            float el = math.degrees(math.asin(math.clamp(p.y, -1f, 1f)));
            float az = math.degrees(math.atan2(p.x, p.z));
            if (az < 0f) az += 360f;
            return (el, az);
        }

        public void RecordSun()
        {
            var (el, az) = SunAngles();
            Record["sunElevation"] = float.IsNaN(el) ? null : (JToken)Math.Round(el, 1);
            Record["sunAzimuth"] = float.IsNaN(az) ? null : (JToken)Math.Round(az, 1);
            // the oblique camera (yaw 25) sits south of its target: light from the south is light from behind the camera
            Record["sunBehindCamera"] = !float.IsNaN(az) && math.abs(az - 180f) < 70f;
            try
            {
                var hdc = HDCamera.GetOrCreate(Camera.main);
                var stack = hdc.volumeStack;
                var w = stack.GetComponent<WindVolumeComponent>();
                var x = stack.GetComponent<Exposure>();
                Record["windInEffect"] = w != null ? Math.Round(w.windGlobalStrengthScale.value * w.windGlobalStrengthScale2.value, 3) : -1;
                Record["exposureInEffect"] = x != null ? x.mode.value + " " + Math.Round(x.fixedExposure.value, 2) : "?";
            }
            catch (Exception e) { Record["stackError"] = e.Message; }
            Record["captureAntiAliasing"] = SuiteCapture.Deterministic ? "SMAA (deterministic captures; the game uses " + SuiteCapture.CameraAa() + ")" : SuiteCapture.CameraAa();
        }

        private void Climate()
        {
            var cs = _world.GetOrCreateSystemManaged<ClimateSystem>();
            // the climate's warmest date: summer colours, no snow cover
            float best = 0f, bestT = float.MinValue;
            try
            {
                var ps = _world.GetOrCreateSystemManaged<PrefabSystem>();
                var prefab = ps.GetPrefab<global::Game.Prefabs.Climate.ClimatePrefab>(cs.currentClimate);
                for (int i = 0; i < 48; i++)
                {
                    float t = i / 48f;
                    float temp = cs.SampleClimate(prefab, t).temperature;
                    if (temp > bestT) { bestT = temp; best = t; }
                }
                cs.currentDate.overrideValue = best;
                Record["climateDate"] = Math.Round(best, 3);
                Record["climateTemperature"] = Math.Round(bestT, 1);
            }
            catch (Exception e) { Record["climateDateError"] = e.Message; }
            cs.precipitation.overrideValue = 0f;
            cs.cloudiness.overrideValue = 0f;
            cs.fog.overrideValue = 0f;
            cs.aurora.overrideValue = 0f;
            cs.thunder.overrideValue = 0f;
            if (bestT == float.MinValue || bestT < 15f) cs.temperature.overrideValue = 20f;
            Record["weather"] = "precipitation 0, cloudiness 0, fog 0, aurora 0, thunder 0";
        }

        private void Volume()
        {
            _volume = VolumeHelper.CreateVolume("CitymakerSuiteLook", 1500);
            var profile = _volume.profile;
            _exposure = profile.Add<Exposure>();
            _exposure.active = true;
            _exposure.mode.overrideState = true;
            _exposure.mode.value = ExposureMode.Fixed;
            _exposure.fixedExposure.overrideState = true;
            _exposure.fixedExposure.value = 13.5f;
            var indirect = profile.Add<IndirectLightingController>();
            indirect.indirectDiffuseLightingMultiplier.overrideState = true;
            indirect.indirectDiffuseLightingMultiplier.value = 1.3f;
            var wind = profile.Add<WindVolumeComponent>();
            wind.windGlobalStrengthScale.overrideState = true;
            wind.windGlobalStrengthScale.value = 0f;
            wind.windGlobalStrengthScale2.overrideState = true;
            wind.windGlobalStrengthScale2.value = 0f;
            Record["volume"] = "CitymakerSuiteLook priority 1500: exposure fixed, indirect diffuse x1.3, wind 0";
        }

        public void SetEv(float ev)
        {
            if (_exposure == null) return;
            _exposure.fixedExposure.value = ev;
            Ev = ev;
        }

        public bool HasExposure => _exposure != null;

        /// <summary>
        /// Calibrates the fixed exposure: renders the current view at several EV values and keeps the one whose mean
        /// brightness is closest to the target (bisection; a higher EV is darker).
        /// </summary>
        public IEnumerator CalibrateExposure(Action<string> say)
        {
            if (_exposure == null) yield break;
            float lo = 10f, hi = 17f;
            var tries = new JArray();
            float bestEv = 13.5f, bestErr = float.MaxValue;
            for (int i = 0; i < 7; i++)
            {
                float ev = (lo + hi) * 0.5f;
                SetEv(ev);
                for (int k = 0; k < 6; k++) yield return null;
                yield return new WaitForEndOfFrame();
                var t = SuiteCapture.RenderTexture2D(_cfg.shotWidth / 4, _cfg.shotHeight / 4, false, out var err);
                if (t == null) { say("exposure calibration capture failed: " + err); break; }
                float luma = SuiteCapture.MeanLuma(t);
                UnityEngine.Object.Destroy(t);
                tries.Add(new JArray(Math.Round(ev, 2), Math.Round(luma, 1)));
                float e = math.abs(luma - _cfg.lookLuma);
                if (e < bestErr) { bestErr = e; bestEv = ev; }
                if (luma > _cfg.lookLuma) lo = ev; else hi = ev;
            }
            SetEv(bestEv);
            Record["exposureEv"] = Math.Round(bestEv, 2);
            Record["exposureTries"] = tries;
            Record["targetLuma"] = _cfg.lookLuma;
            say($"exposure: EV {bestEv:F2} (target luma {_cfg.lookLuma}, tries {tries.ToString(Newtonsoft.Json.Formatting.None)})");
        }

        /// <summary>Notification icons off for every icon prefab except tool errors (the game's debug tab does the same).</summary>
        private int HideIcons()
        {
            var em = _world.EntityManager;
            int n = 0;
            using (var q = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<NotificationIconData>(), ComponentType.ReadOnly<NotificationIconDisplayData>() },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            }))
            using (var all = q.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in all)
                {
                    if (em.HasComponent<ToolErrorData>(e)) continue;
                    if (!em.IsComponentEnabled<NotificationIconDisplayData>(e)) { n++; continue; }
                    em.SetComponentEnabled<NotificationIconDisplayData>(e, false);
                    n++;
                }
            }
            _world.GetOrCreateSystemManaged<IconClusterSystem>().RecalculateClusters();
            return n;
        }
    }
}
