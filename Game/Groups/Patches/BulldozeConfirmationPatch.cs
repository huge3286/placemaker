// Citymaker — ask the vanilla bulldoze confirmation before a whole build is removed (GPL-3.0).
// Uses Harmony 2.2.2 (MIT, Andreas Pardeike) as a library; see Library/Harmony/LICENSE.
// Adapted from ParkingLotTool's ParkingLotBulldozeConfirmationPatch (KruemelGames, GPL-3.0).
//
// Vanilla BulldozeToolSystem only asks for (non-spawnable) buildings. Our builds are lot areas, so
// right before Apply we request the unchanged vanilla confirmation dialog (EventConfirmationRequested
// → UI → ConfirmAction → State.Confirmed → Apply). The OnUpdate prefix remembers whether this update
// is the confirmed one, so a confirmation never carries over to a later click.
//
// Guarded: m_State (read and written), OnUpdate, Apply and the private applyMode setter. If any is
// missing, Prepare returns false and only the question is skipped: the bulldozer still removes the
// build (and the cascade still removes every member).

using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Game.Areas;
using Game.Common;
using Game.Tools;
using HarmonyLib;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace Citymaker.Game.Groups.Patches
{
    [HarmonyPatch]
    internal static class BulldozeConfirmationPatch
    {
        /// <summary>Setting: ask before bulldozing a whole Citymaker build (default on).</summary>
        public static bool Enabled = true;

        private sealed class Approval { internal bool Confirmed; }
        private static readonly ConditionalWeakTable<BulldozeToolSystem, Approval> Approvals = new ConditionalWeakTable<BulldozeToolSystem, Approval>();
        private static readonly FieldInfo StateField = AccessTools.Field(typeof(BulldozeToolSystem), "m_State");
        private static readonly MethodInfo ApplyModeSetter = AccessTools.PropertySetter(typeof(ToolBaseSystem), "applyMode");

        [HarmonyPrepare]
        private static bool Prepare()
        {
            var missing = new System.Collections.Generic.List<string>();
            if (StateField == null) missing.Add("BulldozeToolSystem.m_State");
            if (AccessTools.Method(typeof(BulldozeToolSystem), "OnUpdate") == null) missing.Add("BulldozeToolSystem.OnUpdate");
            if (AccessTools.Method(typeof(BulldozeToolSystem), "Apply") == null) missing.Add("BulldozeToolSystem.Apply");
            if (ApplyModeSetter == null) missing.Add("ToolBaseSystem.applyMode");
            if (missing.Count == 0) return true;
            Mod.Log.Warn("Citymaker: bulldoze confirmation disabled, missing " + string.Join(", ", missing.ToArray()));
            return false;
        }

        [HarmonyPatch(typeof(BulldozeToolSystem), "OnUpdate")]
        [HarmonyPrefix]
        private static void BeforeUpdate(BulldozeToolSystem __instance)
        {
            try { Approvals.GetOrCreateValue(__instance).Confirmed = StateField.GetValue(__instance).ToString() == "Confirmed"; }
            catch (Exception) { }
        }

        [HarmonyPatch(typeof(BulldozeToolSystem), "Apply")]
        [HarmonyPrefix]
        private static bool BeforeApply(BulldozeToolSystem __instance, JobHandle inputDeps, ref JobHandle __result)
        {
            try
            {
                var approval = Approvals.GetOrCreateValue(__instance);
                if (approval.Confirmed) { approval.Confirmed = false; return true; }
                if (!Enabled) return true;
                var registry = GroupRegistrySystem.Instance;
                if (registry == null || registry.World != __instance.World) return true;
                inputDeps.Complete();
                if (!DeletesOwnerArea(__instance.EntityManager, registry)) return true;
                __result = inputDeps;
                ApplyModeSetter.Invoke(__instance, new object[] { ApplyMode.None });
                if (__instance.EventConfirmationRequested == null)
                {
                    // Without a UI receiver never delete silently.
                    StateField.SetValue(__instance, Enum.Parse(StateField.FieldType, "Cancelled"));
                    Mod.Log.Warn("Citymaker: bulldozing a build was cancelled because the confirmation dialog is unavailable.");
                    return false;
                }
                StateField.SetValue(__instance, Enum.Parse(StateField.FieldType, "Waiting"));
                __instance.EventConfirmationRequested();
                return false;
            }
            catch (Exception e)
            {
                Enabled = false;
                Mod.Log.Error(e, "Citymaker bulldoze confirmation failed; disabled for this session");
                return true;
            }
        }

        private static bool DeletesOwnerArea(EntityManager em, GroupRegistrySystem registry)
        {
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<Area>(), ComponentType.ReadOnly<Temp>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var temp = em.GetComponentData<Temp>(entities[i]);
                if ((temp.m_Flags & TempFlags.Delete) != 0 && registry.IsOwnerArea(temp.m_Original)) return true;
            }
            // A group without owner lot: bulldozing any of its objects/areas removes the whole group, so ask too.
            using var parts = em.CreateEntityQuery(ComponentType.ReadOnly<Temp>());
            using var temps = parts.ToComponentDataArray<Temp>(Allocator.Temp);
            for (int i = 0; i < temps.Length; i++)
                if ((temps[i].m_Flags & TempFlags.Delete) != 0 && registry.IsOwnerlessGroupPart(temps[i].m_Original, out _)) return true;
            return false;
        }
    }
}
