// Citymaker — registers Lang/<locale>*.json with the game's LocalizationManager.
//
// Every workstream ships its own file per locale (en-US.core.json, en-US.tool.json, en-US.park.json…)
// so parallel work never conflicts; this merges them. Late community locales (I18N Everywhere,
// language packs) are added AFTER mods load, and AddSource on a locale the manager does not know
// yet does nothing — so we also listen to onSupportedLocalesChanged (approach from Grid Road
// Generator's GridRoadGeneratorMod.RegisterLocalizations). Every locale gets the full en-US set
// underneath its own strings, so a missing translation shows English instead of a raw key.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Colossal;
using Game.SceneFlow;
using Citymaker.Core.Tool;
using Citymaker.Game.Settings;

namespace Citymaker.Game.Localization
{
    public static class LocalizationLoader
    {
        public const string Fallback = "en-US";

        private static readonly HashSet<string> s_Registered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool s_Listening;
        private static Dictionary<string, Dictionary<string, string>> s_Files;
        private static Func<Dictionary<string, string>, Dictionary<string, string>> s_Extra;
        private static readonly List<KeyValuePair<string, MemorySource>> s_Sources = new List<KeyValuePair<string, MemorySource>>();

        /// <summary>
        /// Loads all Lang files and registers every locale the game supports.
        /// <paramref name="extraKeys"/> maps the merged strings of a locale to additional entries
        /// (the settings page ids, which CS2 derives from type/property names).
        /// </summary>
        public static void Register(Func<Dictionary<string, string>, Dictionary<string, string>> extraKeys = null)
        {
            s_Extra = extraKeys;
            s_Files = LoadFiles(ModPaths.ModFolder == null ? null : Path.Combine(ModPaths.ModFolder, "Lang"));
            var lm = GameManager.instance?.localizationManager;
            if (lm == null) { Mod.Log.Warn("Citymaker: no LocalizationManager, texts will show as keys"); return; }
            if (!s_Listening)
            {
                lm.onSupportedLocalesChanged += RegisterPending;
                s_Listening = true;
            }
            RegisterPending();
        }

        public static void Unregister()
        {
            var lm = GameManager.instance?.localizationManager;
            if (lm == null) return;
            if (s_Listening) { lm.onSupportedLocalesChanged -= RegisterPending; s_Listening = false; }
            foreach (var kv in s_Sources)
            {
                try { lm.RemoveSource(kv.Key, kv.Value); } catch (Exception) { }
            }
            s_Sources.Clear();
            s_Registered.Clear();
        }

        private static void RegisterPending()
        {
            var lm = GameManager.instance?.localizationManager;
            if (lm == null || s_Files == null) return;
            string[] supported;
            try { supported = lm.GetSupportedLocales(); }
            catch (Exception) { supported = new[] { Fallback }; }
            foreach (var locale in supported)
            {
                if (string.IsNullOrEmpty(locale) || s_Registered.Contains(locale)) continue;
                // One failing locale must neither stop the others nor bubble into the language mod that raised the event.
                try
                {
                    var entries = Merge(locale);
                    if (s_Extra != null)
                        foreach (var kv in s_Extra(entries)) if (!entries.ContainsKey(kv.Key)) entries[kv.Key] = kv.Value;
                    var source = new MemorySource(entries);
                    lm.AddSource(locale, source);
                    s_Sources.Add(new KeyValuePair<string, MemorySource>(locale, source));
                }
                catch (Exception e)
                {
                    Mod.Log.Warn("Citymaker: locale " + locale + " not registered: " + e.Message);
                }
                s_Registered.Add(locale);
            }
        }

        /// <summary>en-US first, then the locale's own files, then (for "pt-BR") nothing else — exact locale wins.</summary>
        private static Dictionary<string, string> Merge(string locale)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (s_Files.TryGetValue(Fallback, out var en)) foreach (var kv in en) result[kv.Key] = kv.Value;
            if (!string.Equals(locale, Fallback, StringComparison.OrdinalIgnoreCase))
            {
                // Language-only files ("de.json") apply to every regional variant, regional files override them.
                int dash = locale.IndexOf('-');
                if (dash > 0 && s_Files.TryGetValue(locale.Substring(0, dash), out var lang))
                    foreach (var kv in lang) result[kv.Key] = kv.Value;
                foreach (var pair in s_Files)
                    if (string.Equals(pair.Key, locale, StringComparison.OrdinalIgnoreCase))
                        foreach (var kv in pair.Value) result[kv.Key] = kv.Value;
            }
            return result;
        }

        // "<locale>.json" or "<locale>.<area>.json"; the locale part never contains a dot.
        private static readonly Regex s_FileName = new Regex(@"^(?<locale>[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*)(\.[^.]+)?\.json$", RegexOptions.CultureInvariant);

        public static Dictionary<string, Dictionary<string, string>> LoadFiles(string folder)
        {
            var files = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                Mod.Log.Warn("Citymaker: Lang folder not found (" + (folder ?? "<null>") + ")");
                return files;
            }
            var paths = Directory.GetFiles(folder, "*.json");
            Array.Sort(paths, StringComparer.OrdinalIgnoreCase);   // deterministic merge order
            foreach (var path in paths)
            {
                var m = s_FileName.Match(Path.GetFileName(path));
                if (!m.Success) continue;
                string locale = m.Groups["locale"].Value;
                try
                {
                    var doc = JsonValue.Parse(File.ReadAllText(path));
                    if (!files.TryGetValue(locale, out var dict)) files[locale] = dict = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var kv in doc.Properties)
                    {
                        if (kv.Value.Kind != JsonKind.String) continue;
                        if (dict.ContainsKey(kv.Key) && dict[kv.Key] != kv.Value.AsString())
                            Mod.Log.Warn("Citymaker: key " + kv.Key + " defined twice for " + locale + " (" + Path.GetFileName(path) + " wins)");
                        dict[kv.Key] = kv.Value.AsString();
                    }
                }
                catch (Exception e)
                {
                    Mod.Log.Warn("Citymaker: skipping " + Path.GetFileName(path) + ": " + e.Message);
                }
            }
            return files;
        }

        /// <summary>English text for a key (used where the game's localisation is not available, e.g. logs).</summary>
        public static string English(string key)
        {
            if (s_Files != null && s_Files.TryGetValue(Fallback, out var en) && en.TryGetValue(key, out var v)) return v;
            return key;
        }

        private sealed class MemorySource : IDictionarySource
        {
            private readonly Dictionary<string, string> _entries;
            public MemorySource(Dictionary<string, string> entries) { _entries = entries; }
            public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts) => _entries;
            public void Unload() { }
        }
    }
}
