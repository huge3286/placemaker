// Citymaker — icon paths the panel receives from C# (layers, recipes, options, presets, marker kinds).
//
// Vanilla icons are addressed like vanilla does: "Media/Game/Icons/X.svg" (relative to the game UI
// host; the UI maps it to coui://GameUI/Media/...). Playtest 1 logged "Invalid host locations map"
// for coui://gameui/Media/Game/Icons/ParkingFacility.svg — that file does not exist in 1.6.2f1.
// This filter normalises the forms we accept, fixes known renames, and drops (with one log line)
// any vanilla path whose file is missing, so the UI falls back to its text/placeholder instead of
// spamming Gameface errors. Non-vanilla hosts (coui://ui-mods/…, a mod's own host) pass unchanged.

using System;
using System.Collections.Generic;
using System.IO;

namespace Citymaker.Game.UI
{
    public static class IconPaths
    {
        private const string GameHost = "coui://GameUI/";

        private static readonly Dictionary<string, string> Renames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Media/Game/Icons/ParkingFacility.svg", "Media/Game/Icons/Parking.svg" },
        };

        private static readonly Dictionary<string, string> Cache = new Dictionary<string, string>(StringComparer.Ordinal);
        private static string _uiRoot;
        private static bool _rootChecked;

        public static string Fix(string icon)
        {
            if (string.IsNullOrEmpty(icon)) return "";
            if (Cache.TryGetValue(icon, out var done)) return done;
            string result = icon;
            try { result = Resolve(icon); }
            catch (Exception) { result = icon; }
            Cache[icon] = result;
            return result;
        }

        private static string Resolve(string icon)
        {
            string rel = null;
            if (icon.StartsWith("Media/", StringComparison.Ordinal)) rel = icon;
            else if (icon.StartsWith(GameHost, StringComparison.OrdinalIgnoreCase)) rel = icon.Substring(GameHost.Length);
            if (rel == null) return icon;   // another host (ui-mods, a mod's own) or a data url: not ours to check
            if (Renames.TryGetValue(rel, out var renamed)) rel = renamed;
            var root = UiRoot();
            if (root != null && !File.Exists(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar))))
            {
                Mod.Log.Warn("Citymaker: icon '" + icon + "' does not exist in this game version; showing none.");
                return "";
            }
            return rel;
        }

        /// <summary>…/Cities2_Data/Content/Game/UI, or null when it cannot be found (then nothing is dropped).</summary>
        private static string UiRoot()
        {
            if (_rootChecked) return _uiRoot;
            _rootChecked = true;
            try
            {
                var dir = Path.Combine(UnityEngine.Application.dataPath, "Content", "Game", "UI");
                if (Directory.Exists(Path.Combine(dir, "Media"))) _uiRoot = dir;
            }
            catch (Exception) { _uiRoot = null; }
            return _uiRoot;
        }
    }
}
