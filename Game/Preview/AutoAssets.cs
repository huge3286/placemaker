// Citymaker — resolve "automatic" assets (empty AssetRef.Id, or ids missing from this playset)
// to concrete prefab names BEFORE pricing and Build, so the price shown is what Build places
// (FEEDBACK-1: "construction cost shows $0" — Pricing only prices explicit ids).
//
// ONE resolver (docs/proposals/gametool.md §5, gamebuild.md §6): this shim forwards to
// IAssetCatalog.ResolveAutomatic, implemented by AssetCatalogSystem on top of the pure
// Core/Build/AutoAssets (unit-tested). BuildService.Start calls the same method again (idempotent),
// so preview = price = build.
//
// Rules (deterministic — same plan + same catalog → same ids):
// • Nets, surfaces, areas and functional objects: the category default (an explicit id that is not
//   loaded falls back to its first loaded Fallback, else the default).
// • Natural-variety objects (trees, bushes, flowerbeds): one of the category's best vanilla items,
//   picked by a hash of PlannedObject.Variation, so a park is not one cloned tree.

using Citymaker.Core;

namespace Citymaker.Game.Preview
{
    public static class AutoAssets
    {
        /// <summary>Resolves every asset of <paramref name="plan"/> in place. Returns how many refs changed.</summary>
        public static int Resolve(ComposedPlan plan, IAssetCatalog catalog)
        {
            if (plan == null || catalog == null || !catalog.Ready) return 0;
            return catalog.ResolveAutomatic(plan);
        }
    }
}
