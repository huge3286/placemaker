// Citymaker — invisible tool that owns the apply cadence during a build (GPL-3.0).
//
// Why a tool: CS2 applies or clears ALL temporary entities according to the ACTIVE tool's
// applyMode (ToolSystem.applyMode → ToolOutputSystem → ApplyTool/ClearTool phases). Another tool
// that sets ApplyMode.Clear every frame (the usual tool cadence) would wipe our temporaries, and
// no system outside the active tool can request Apply. So BuildService activates this tool for the
// few frames of a build and restores the previous tool afterwards. While it is active no other
// tool produces definitions, so every temporary entity belongs to our build.
//
// Phase: ToolUpdate (all tools update there; ToolOutputSystem reads applyMode right after).

using Game.Prefabs;
using Game.Tools;
using Unity.Jobs;
using UnityEngine.Scripting;

namespace Citymaker.Game.Build
{
    public sealed partial class BuildToolSystem : ToolBaseSystem
    {
        public const string Id = "Citymaker Build";

        /// <summary>Apply mode requested by BuildService for the next ToolUpdate.</summary>
        internal ApplyMode Requested = ApplyMode.None;
        /// <summary>Frame in which the requested mode was actually handed to the game.</summary>
        internal int LastAppliedFrame = -1;
        internal ApplyMode LastMode = ApplyMode.None;

        public override string toolID => Id;

        public override PrefabBase GetPrefab() => null;

        public override bool TrySetPrefab(PrefabBase prefab) => false;

        /// <summary>Vanilla's own apply gate: no Error entities and no deleted originals.</summary>
        internal bool AllowApply => GetAllowApply();

        /// <summary>(s59) The non-error half of vanilla's gate: an original our temporaries modify was deleted meanwhile.</summary>
        internal bool OriginalDeleted => m_OriginalDeletedSystem != null && m_OriginalDeletedSystem.GetOriginalDeletedResult(0);

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
        }

        public override void InitializeRaycast()
        {
            base.InitializeRaycast();
            // No raycast needed: the build tool never reacts to the mouse.
            m_ToolRaycastSystem.typeMask = global::Game.Common.TypeMask.None;
        }

        [Preserve]
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            applyMode = Requested;
            LastMode = Requested;
            LastAppliedFrame = UnityEngine.Time.frameCount;
            // One-shot modes: Apply/Clear must not repeat on following frames.
            Requested = ApplyMode.None;
            return inputDeps;
        }

        [Preserve]
        protected override void OnStopRunning()
        {
            applyMode = ApplyMode.None;
            Requested = ApplyMode.None;
            base.OnStopRunning();
        }
    }
}
