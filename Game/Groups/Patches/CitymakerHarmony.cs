// Citymaker — Harmony installation (GPL-3.0). Uses Harmony 2.2.2 (MIT, Andreas Pardeike) as an
// unmodified library dependency, see Library/Harmony/README.md and LICENSE; no Harmony source is copied.
//
// Pattern from ParkingLotTool's ParkingLotRaycastPatch.Install (KruemelGames, GPL-3.0):
//  * every patch class has a [HarmonyPrepare] guard: PatchAll throws as soon as ONE target is
//    missing and would take the other patches with it — Prepare=false skips just that class;
//  * Install is wrapped: a tester once had another mod built against Harmony 2.3 while 2.2.2 was
//    loaded (MissingMethodException in PatchAll). A missing hook is a defect; a half-loaded mod is a
//    mystery. Without the patches builds still work, they are just selectable per part.

using System;
using HarmonyLib;

namespace Citymaker.Game.Groups.Patches
{
    internal static class CitymakerHarmony
    {
        private const string HarmonyId = "citymaker.groups";
        private static Harmony _harmony;

        /// <summary>True once the patches are installed (diagnostics / UI hints).</summary>
        internal static bool Installed => _harmony != null;

        internal static void Install()
        {
            if (_harmony != null) return;
            try
            {
                var h = new Harmony(HarmonyId);
                h.PatchAll(typeof(CitymakerHarmony).Assembly);
                _harmony = h;
                Mod.Log.Info("Citymaker Harmony hooks active: raycast lift (part → owner area), bulldoze confirmation.");
            }
            catch (Exception e)
            {
                Mod.Log.Error(e, "Citymaker could not install its Harmony hooks. Builds stay selectable per part and "
                    + "bulldozing a part asks no confirmation; everything else works. Most common cause: another Harmony version loaded.");
                try { new Harmony(HarmonyId).UnpatchAll(HarmonyId); } catch (Exception) { }
            }
        }

        internal static void Uninstall()
        {
            if (_harmony == null) return;
            try { _harmony.UnpatchAll(HarmonyId); }
            catch (Exception e) { Mod.Log.Warn("Citymaker could not remove its Harmony hooks: " + e.Message); }
            _harmony = null;
        }
    }
}
