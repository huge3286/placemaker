// Ghost preview ("see it before you buy it"): real translucent models and surface textures for the
// current plan, built by the build workstream's ghost service (IGhostPreview, proposal in
// docs/proposals/gamebuild.md: Show(plan, revision) / Hide() / status, debounced and batched,
// never applies). Until that service lands in master the interface is defined here; the build
// workstream registers its implementation with GhostPreview.Register (or an adapter forwarding to
// its own type), and the tool talks only to GhostPreviewClient.
//
// Apply-mode ownership (vanilla, decompiled): ToolOutputSystem runs the ClearTool phase (deletes
// every Temp) when the ACTIVE tool's applyMode is Clear, the ApplyTool phase when it is Apply, and
// nothing when it is None. So the ghost's temporaries survive exactly as long as the active tool
// says None; a refresh needs one Clear frame in which the new definitions are created (vanilla
// tools: applyMode = Clear + DestroyDefinitions + new definitions in the same update). The service
// therefore cannot set the mode itself: the Citymaker tool asks Pump() once per frame from its
// OnUpdate and sets applyMode = Clear only on the frames the service asks for, None otherwise.
// Never Apply — the build applies through its own BuildToolSystem.

using System;
using Citymaker.Core;

namespace Citymaker.Game.Preview
{
    public enum GhostPhase { Hidden = 0, Pending = 1, Shown = 2, Failed = 3 }

    public struct GhostStatus
    {
        public GhostPhase Phase;
        /// <summary>Revision whose temporaries are on the map now, -1 = none.</summary>
        public int ShownRevision;
        /// <summary>Last revision passed to Show, -1 = hidden.</summary>
        public int RequestedRevision;
        /// <summary>Localisation key when the ghost could not be built (Failed), else null.</summary>
        public string MessageKey;
        /// <summary>
        /// Objects of the shown ghost the game cancelled (they collide; optional temporaries vanish without an
        /// icon). The tool lists the count in the Review summary — a ghost never hides parts silently.
        /// </summary>
        public int Dropped;
    }

    public interface IGhostPreview
    {
        /// <summary>Show the plan as temporaries (debounced; replaces the previous ghost). Never applies.</summary>
        void Show(ComposedPlan plan, int revision);
        /// <summary>Remove the ghost (on the next Clear frame).</summary>
        void Hide();
        GhostStatus Status { get; }
        /// <summary>
        /// Called once per frame by the active Citymaker tool from its OnUpdate (ToolUpdate phase). The
        /// service emits/destroys its creation definitions here and returns true when this frame must be a
        /// Clear frame (old temporaries removed, new ones generated); false = keep the temporaries (None).
        /// </summary>
        bool Pump();
        /// <summary>
        /// Optional instant Build: when the ghost of <paramref name="revision"/> is complete, start the build
        /// from it (the build service applies the existing temporaries). False = not possible, the caller
        /// starts a normal build. Progress and the result are reported through Services.Build.Status as for Start.
        /// </summary>
        bool TryBuildFromGhost(BuildRequest request, int revision);
    }

    /// <summary>Registration point for the ghost service (set by the implementing system's OnCreate, cleared in OnDestroy).</summary>
    public static class GhostPreview
    {
        public static IGhostPreview Service { get; private set; }

        public static void Register(IGhostPreview service) => Service = service;

        public static void Unregister(IGhostPreview service)
        {
            if (ReferenceEquals(Service, service)) Service = null;
        }
    }

    /// <summary>
    /// The tool's side: null-safe, exception-safe wrapper. A broken or missing service degrades to the
    /// sketch (Available = false) instead of breaking the tool.
    /// </summary>
    public sealed class GhostPreviewClient
    {
        private const int MaxErrors = 5;
        private int _errors;
        private bool _hideSent = true;

        private IGhostPreview Svc => _errors < MaxErrors ? GhostPreview.Service : null;

        public bool Available => Svc != null;

        public GhostStatus Status
        {
            get
            {
                var s = Svc;
                if (s == null) return new GhostStatus { Phase = GhostPhase.Hidden, ShownRevision = -1, RequestedRevision = -1 };
                try { return s.Status; }
                catch (Exception e) { Fail("status", e); return new GhostStatus { Phase = GhostPhase.Failed, ShownRevision = -1, RequestedRevision = -1 }; }
            }
        }

        public void Show(ComposedPlan plan, int revision)
        {
            var s = Svc;
            if (s == null || plan == null) return;
            try { s.Show(plan, revision); _hideSent = false; }
            catch (Exception e) { Fail("show", e); }
        }

        public void Hide()
        {
            var s = GhostPreview.Service;   // hide even after errors: leave nothing on the map
            if (s == null || _hideSent) return;
            _hideSent = true;
            try { s.Hide(); }
            catch (Exception e) { Fail("hide", e); }
        }

        /// <summary>True = this frame must be a Clear frame.</summary>
        public bool Pump()
        {
            var s = GhostPreview.Service;
            if (s == null) return false;
            try { return s.Pump(); }
            catch (Exception e) { Fail("pump", e); return false; }
        }

        public bool TryBuildFromGhost(BuildRequest request, int revision)
        {
            var s = Svc;
            if (s == null) return false;
            try { return s.TryBuildFromGhost(request, revision); }
            catch (Exception e) { Fail("build", e); return false; }
        }

        /// <summary>True when Hide was sent and nothing has been shown since.</summary>
        public bool Hidden => _hideSent;

        private void Fail(string what, Exception e)
        {
            _errors++;
            Mod.Log.Warn("Citymaker: ghost preview " + what + " failed: " + e.Message + (_errors >= MaxErrors ? " — ghost preview disabled for this session" : ""));
        }
    }
}
