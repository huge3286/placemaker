using System;
using System.IO;
using Citymaker.Core.Tool;

namespace Citymaker.Game.Settings
{
    /// <summary>
    /// ModsData/Citymaker/presets.json — the player's presets (customs + overrides of built-ins),
    /// pretty-printed so they can be read and baked into the shipped defaults. Loaded at startup,
    /// rewritten (atomically) after every change. A corrupt file is moved to presets.bad.json and
    /// the store starts empty; a file from a newer version is copied to presets.v{N}.json before
    /// it is first overwritten.
    /// </summary>
    public static class PresetFile
    {
        public static string FilePath => Path.Combine(ModPaths.DataFolder, "presets.json");

        public static void Load(PresetStore store)
        {
            if (store == null) return;
            string path = FilePath;
            try
            {
                if (!File.Exists(path)) return;
                store.Load(File.ReadAllText(path));
                if (store.LoadedFormatVersion > PresetStore.CurrentFormatVersion)
                {
                    var keep = Path.Combine(ModPaths.DataFolder, "presets.v" + store.LoadedFormatVersion + ".json");
                    if (!File.Exists(keep)) File.Copy(path, keep);
                    Mod.Log.Warn("Citymaker: presets.json is format " + store.LoadedFormatVersion + " (newer); a copy was kept as " + Path.GetFileName(keep));
                }
                Mod.Log.Info("Citymaker: loaded " + store.All.Count + " player presets");
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Citymaker: presets.json is unreadable (" + e.Message + "); moved to presets.bad.json, starting empty");
                try
                {
                    var bad = Path.Combine(ModPaths.DataFolder, "presets.bad.json");
                    if (File.Exists(bad)) File.Delete(bad);
                    File.Move(path, bad);
                }
                catch (Exception e2) { Mod.Log.Warn("Citymaker: could not back up presets.json: " + e2.Message); }
                store.Clear();
            }
        }

        /// <summary>Writes the store. Returns the absolute path, or null on failure.</summary>
        public static string Save(PresetStore store)
        {
            if (store == null) return null;
            string path = FilePath;
            try
            {
                ModPaths.WriteAllTextAtomic(path, store.Write().Write(true));
                return Path.GetFullPath(path);
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Citymaker: could not save presets.json: " + e.Message);
                return null;
            }
        }
    }
}
