using System;
using System.IO;
using Colossal.PSI.Environment;
using Game.Modding;
using Game.SceneFlow;

namespace Citymaker.Game.Settings
{
    /// <summary>
    /// Where the mod's files live. Assembly.Location is empty for CS2 code mods (RESEARCH-NOTES rule 21),
    /// so the folder comes from the mod's ExecutableAsset.
    /// </summary>
    public static class ModPaths
    {
        private static string _modFolder;

        /// <summary>Folder containing Citymaker.dll, Lang/ and Recipes/. Null when it cannot be determined.</summary>
        public static string ModFolder => _modFolder;

        public static void Initialize(IMod mod)
        {
            try
            {
                if (GameManager.instance?.modManager != null
                    && GameManager.instance.modManager.TryGetExecutableAsset(mod, out var asset)
                    && !string.IsNullOrEmpty(asset.path))
                    _modFolder = Path.GetDirectoryName(asset.path);
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Citymaker: could not resolve the mod folder: " + e.Message);
            }
            if (string.IsNullOrEmpty(_modFolder))
            {
                // Standalone deploy fallback (Mods/Citymaker next to the user data).
                try
                {
                    var guess = Path.Combine(EnvPath.kUserDataPath, "Mods", "citymaker");
                    if (Directory.Exists(guess)) _modFolder = guess;
                }
                catch (Exception) { }
            }
            Mod.Log.Info("Citymaker mod folder: " + (_modFolder ?? "<unknown>"));
        }

        /// <summary>ModsData/Citymaker — player recipes and saved defaults. Created on demand.</summary>
        public static string DataFolder
        {
            get
            {
                var dir = Path.Combine(EnvPath.kUserDataPath, "ModsData", "citymaker");
                try { Directory.CreateDirectory(dir); } catch (Exception) { }
                return dir;
            }
        }

        public static string PlayerRecipeFolder
        {
            get
            {
                var dir = Path.Combine(DataFolder, "Recipes");
                try { Directory.CreateDirectory(dir); } catch (Exception) { }
                return dir;
            }
        }

        /// <summary>Writes atomically (temp file + replace) so a crash mid-write never leaves a half file.</summary>
        public static void WriteAllTextAtomic(string path, string text)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, text, new System.Text.UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
    }
}
