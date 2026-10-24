// Citymaker — seam for gameplay effects of built groups (GPL-3.0).
// FUTURE: parks giving leisure/wellbeing (e.g. via a hidden companion building like the parking
// economy, or via the Park Adventures DLC's own park areas once they exist). Nothing implements
// this yet on purpose; the build service and registry never depend on it.

namespace Citymaker.Game.Economy
{
    public interface IEffectProvider
    {
        /// <summary>Stable id ("parking", "park-leisure"…). Stored nowhere yet.</summary>
        string Id { get; }

        /// <summary>True when this provider wants to attach effects to the group (by recipe/generator facts).</summary>
        bool AppliesTo(int groupId);

        /// <summary>Called once after a build finished (and after load for existing groups). Must never throw.</summary>
        void Attach(int groupId);

        /// <summary>Called before the group's members are removed. Must never throw.</summary>
        void Detach(int groupId);
    }
}
