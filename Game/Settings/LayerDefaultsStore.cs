using System;
using System.Collections.Generic;
using System.IO;
using Citymaker.Core;
using Citymaker.Core.Tool;

namespace Citymaker.Game.Settings
{
    /// <summary>
    /// Per-layer values the player saved as defaults ("Save as default" in the panel), stored in
    /// ModsData/Citymaker/defaults.json. Unknown/stale control ids are dropped when applied
    /// (DesignEditor.DefaultsFor), so old files never break new versions.
    /// </summary>
    public sealed class LayerDefaultsStore
    {
        private Dictionary<string, SettingsBag> _defaults = new Dictionary<string, SettingsBag>(StringComparer.Ordinal);
        private bool _loaded;

        private static string FilePath => Path.Combine(ModPaths.DataFolder, "defaults.json");

        public SettingsBag Get(string layerId)
        {
            EnsureLoaded();
            return layerId != null && _defaults.TryGetValue(layerId, out var b) ? b : null;
        }

        public void Set(string layerId, SettingsBag values)
        {
            if (string.IsNullOrEmpty(layerId) || values == null) return;
            EnsureLoaded();
            _defaults[layerId] = values.Clone();
            Save();
        }

        public void Clear()
        {
            _defaults.Clear();
            _loaded = true;
            Save();
        }

        private void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (!File.Exists(FilePath)) return;
                _defaults = RecipeJson.ReadDefaults(JsonValue.Parse(File.ReadAllText(FilePath)));
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Citymaker: ignoring unreadable defaults.json (" + e.Message + ")");
                _defaults = new Dictionary<string, SettingsBag>(StringComparer.Ordinal);
            }
        }

        private void Save()
        {
            try { ModPaths.WriteAllTextAtomic(FilePath, RecipeJson.WriteDefaults(_defaults).Write(true)); }
            catch (Exception e) { Mod.Log.Warn("Citymaker: could not save defaults.json: " + e.Message); }
        }
    }
}
