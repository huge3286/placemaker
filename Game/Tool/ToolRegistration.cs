// Citymaker — registration of the game-tool workstream (systems, settings, key bindings,
// localisation). Called once from Mod.OnLoad by the lead: ToolRegistration.Register(updateSystem, this);
// and ToolRegistration.Unregister() from Mod.OnDispose.

using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.IO.AssetDatabase;
using Game;
using Game.Input;
using Game.SceneFlow;
using Citymaker.Game.Localization;
using Citymaker.Game.Overlay;
using Citymaker.Game.Settings;
using Citymaker.Game.UI;

namespace Citymaker.Game.Tool
{
    public static class ToolRegistration
    {
        public static CitymakerSettings Settings { get; private set; }
        public static LayerDefaultsStore Defaults { get; private set; }
        private static CitymakerToolSystem s_Tool;

        public static void Register(UpdateSystem updateSystem, Mod mod)
        {
            ModPaths.Initialize(mod);

            Settings = new CitymakerSettings(mod);
            try
            {
                AssetDatabase.global.LoadSettings(nameof(Citymaker), Settings, new CitymakerSettings(mod));
            }
            catch (Exception e) { Mod.Log.Warn("Citymaker: settings not loaded, using defaults: " + e.Message); }
            Settings.PushToServices();
            Settings.RegisterInOptionsUI();
            // Must run before any GetAction() (actions come from the ProxyBinding properties).
            Settings.RegisterKeyBindings();
            Defaults = new LayerDefaultsStore();

            LocalizationLoader.Register(SettingsLocale);

            updateSystem.UpdateAt<CitymakerToolSystem>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAt<CitymakerUISystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<CitymakerTooltipSystem>(SystemUpdatePhase.UITooltip);
            updateSystem.UpdateAt<CitymakerOverlaySystem>(SystemUpdatePhase.Rendering);
            updateSystem.UpdateAt<FilledPreviewSystem>(SystemUpdatePhase.Rendering);
            updateSystem.UpdateBefore<ZoneGridOpacitySystem, global::Game.Prefabs.ZoneSystem>(SystemUpdatePhase.PrefabUpdate);
            s_Tool = updateSystem.World.GetOrCreateSystemManaged<CitymakerToolSystem>();

            CitymakerSettings.RemoveEverythingRequested += OnRemoveEverything;
            CitymakerSettings.ResetDefaultsRequested += OnResetDefaults;
            Mod.Log.Info("Citymaker tool registered");
        }

        public static void Unregister()
        {
            CitymakerSettings.RemoveEverythingRequested -= OnRemoveEverything;
            CitymakerSettings.ResetDefaultsRequested -= OnResetDefaults;
            LocalizationLoader.Unregister();
            try { Settings?.UnregisterInOptionsUI(); } catch (Exception) { }
            Settings = null;
            s_Tool = null;
        }

        private static void OnRemoveEverything() => s_Tool?.RemoveEverything();
        private static void OnResetDefaults() => s_Tool?.ResetAllDefaults();

        /// <summary>
        /// CS2 derives the Options page's localisation ids from type and property names; map them to
        /// our readable keys ("Citymaker.Settings.<Property>" / ".Desc") from Lang/en-US.tool.json.
        /// </summary>
        private static Dictionary<string, string> SettingsLocale(Dictionary<string, string> strings)
        {
            var s = Settings;
            var r = new Dictionary<string, string>();
            if (s == null) return r;
            string T(string key, string fallback) => strings.TryGetValue(key, out var v) ? v : fallback;

            r[s.GetSettingsLocaleID()] = T("Citymaker.Name", "citymaker");
            r[s.GetOptionTabLocaleID(CitymakerSettings.SectionMain)] = T("Citymaker.Settings.Tab.Main", "citymaker");
            foreach (var g in new[] { CitymakerSettings.GroupGeneral, CitymakerSettings.GroupBrush, CitymakerSettings.GroupBuild, CitymakerSettings.GroupNeighbourhood, CitymakerSettings.GroupKeys, CitymakerSettings.GroupMaintenance })
                r[s.GetOptionGroupLocaleID(g)] = T("Citymaker.Settings.Group." + g, g);
            // Every option of the class (a hand list left newer options showing their raw ids); one that asks for confirmation
            // gets a warning text too
            var props = typeof(CitymakerSettings).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
            foreach (var prop in props)
                if (Attribute.IsDefined(prop, typeof(global::Game.Settings.SettingsUIConfirmationAttribute)))
                    r[s.GetOptionWarningLocaleID(prop.Name)] = T("Citymaker.Settings." + prop.Name + ".Warning", "Are you sure?");
            foreach (var p in Array.ConvertAll(props, x => x.Name))
            {
                r[s.GetOptionLabelLocaleID(p)] = T("Citymaker.Settings." + p, p);
                r[s.GetOptionDescLocaleID(p)] = T("Citymaker.Settings." + p + ".Desc", "");
            }
            r[s.GetOptionWarningLocaleID(nameof(CitymakerSettings.RemoveEverything))] = T("Citymaker.Settings.RemoveEverything.Warning", "Remove everything Citymaker built?");
            r[s.GetOptionWarningLocaleID(nameof(CitymakerSettings.ResetDefaults))] = T("Citymaker.Settings.ResetDefaults.Warning", "Reset all Citymaker options and saved defaults?");
            foreach (PreviewDetail d in Enum.GetValues(typeof(PreviewDetail)))
                r[s.GetEnumValueLocaleID(d)] = T("Citymaker.Settings.PreviewDetail." + d, d.ToString());
            r[s.GetBindingMapLocaleID()] = T("Citymaker.Name", "citymaker");
            foreach (var a in new[] { CitymakerSettings.ActionToggleTool, CitymakerSettings.ActionPrimary, CitymakerSettings.ActionUndo, CitymakerSettings.ActionRedo, CitymakerSettings.ActionRedoAlt })
                r[s.GetBindingKeyLocaleID(a)] = T("Citymaker.Settings.Action." + a, a);
            return r;
        }

        /// <summary>The global hotkey (polled by the UI system, which runs while the tool is closed).</summary>
        internal static ProxyAction ToggleAction
        {
            get
            {
                try { return Settings?.GetAction(CitymakerSettings.ActionToggleTool); }
                catch (Exception) { return null; }
            }
        }

        internal static bool InGame
        {
            get
            {
                try { return GameManager.instance != null && (GameManager.instance.gameMode & GameMode.Game) != 0; }
                catch (Exception) { return false; }
            }
        }
    }
}
