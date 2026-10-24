// Citymaker — catalog thumbnails (GPL-3.0).
//
// Every CatalogItem gets an Icon, best source first:
//   1. "ui"        UIObject.m_Icon (vanilla, or supplied by Asset Icon Library — it writes m_Icon in its
//                  first update after a game preload, which can be AFTER our scan: we recheck periodically)
//   2. "swatch"    surfaces only: a PNG rendered from the surface's own albedo texture (SurfaceSwatches)
//   3. "thumbnail" ImageSystem's thumbnail camera URL (objects/nets; renders nothing for surfaces)
//   4. "group"     the icon of the item's UI group (UIObjectData.m_Group)

using System;
using System.Collections.Generic;
using Game.Prefabs;
using Game.UI;
using Game.Common;
using Unity.Entities;

namespace Citymaker.Game.Assets
{
    public sealed partial class AssetCatalogSystem
    {
        private const int SwatchesPerFrame = 2;
        private readonly Dictionary<string, string> _iconSource = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Queue<string> _swatchQueue = new Queue<string>();
        private int _iconRecheckFrames;

        /// <summary>Where an item's icon comes from: ui, swatch, thumbnail, group or none.</summary>
        public string IconSourceOf(string id) => id != null && _iconSource.TryGetValue(id, out var s) ? s : "none";

        private string ResolveIcon(PrefabBase prefab, Entity entity, bool surface, Dictionary<string, string> sources)
        {
            string url = null, source = "none";
            try
            {
                url = ImageSystem.GetIcon(prefab);
                if (!string.IsNullOrEmpty(url)) source = "ui";
                else if (surface)
                {
                    url = SurfaceSwatches.Cached(prefab.name);
                    if (url != null) source = "swatch";
                }
                if (string.IsNullOrEmpty(url) && !surface)
                {
                    url = ImageSystem.GetThumbnail(prefab);
                    if (!string.IsNullOrEmpty(url)) source = "thumbnail";
                }
                if (string.IsNullOrEmpty(url) && EntityManager.HasComponent<UIObjectData>(entity) && EntityManager.GetComponentData<UIObjectData>(entity).m_Group != Entity.Null
                    && _prefabs.TryGetPrefab<PrefabBase>(EntityManager.GetComponentData<UIObjectData>(entity).m_Group, out var group) && group != null)
                {
                    url = ImageSystem.GetIcon(group);
                    if (!string.IsNullOrEmpty(url)) source = "group";
                }
            }
            catch (Exception) { url = null; source = "none"; }
            sources[prefab.name] = source;
            return url ?? "";
        }

        /// <summary>After a scan: surfaces without a UI icon or cached swatch get one rendered over the next frames.</summary>
        private void QueueIconWork(Dictionary<string, CatalogItem> byName, Dictionary<string, string> sources)
        {
            _iconSource.Clear();
            foreach (var kv in sources) _iconSource[kv.Key] = kv.Value;
            _swatchQueue.Clear();
            foreach (var kv in byName)
                if (IsSurfaceItem(kv.Value) && IconSourceOf(kv.Key) != "ui" && IconSourceOf(kv.Key) != "swatch") _swatchQueue.Enqueue(kv.Key);
            if (_swatchQueue.Count > 0) Mod.Log.Info($"Citymaker renders texture swatches for {_swatchQueue.Count} surfaces without an icon.");
        }

        private bool IsSurfaceItem(CatalogItem it) =>
            it?.Tags != null && Array.IndexOf(it.Tags, Citymaker.Core.AssetCategories.SurfaceAny) >= 0;


        /// <summary>Per frame: a couple of swatches; every RecheckFrames: pick up icons that appeared late (AIL).</summary>
        private void UpdateIcons()
        {
            bool changed = false;
            for (int n = 0; n < SwatchesPerFrame && _swatchQueue.Count > 0; n++)
            {
                var id = _swatchQueue.Dequeue();
                if (!_byName.TryGetValue(id, out var item) || !_prefabs.TryGetPrefab<PrefabBase>(item.Prefab, out var prefab) || prefab == null) continue;
                if (!string.IsNullOrEmpty(ImageSystem.GetIcon(prefab))) continue;   // the recheck below picks it up
                var url = SurfaceSwatches.Generate(prefab);
                if (url == null) continue;
                item.Icon = url;
                _iconSource[id] = "swatch";
                changed = true;
                if (_swatchQueue.Count == 0) LogMissingIcons();
            }
            if (++_iconRecheckFrames >= RecheckFrames)
            {
                _iconRecheckFrames = 0;
                foreach (var kv in _byName)
                {
                    if (IconSourceOf(kv.Key) == "ui") continue;
                    if (!_prefabs.TryGetPrefab<PrefabBase>(kv.Value.Prefab, out var prefab) || prefab == null) continue;
                    string url;
                    try { url = ImageSystem.GetIcon(prefab); } catch (Exception) { url = null; }
                    if (string.IsNullOrEmpty(url)) continue;
                    kv.Value.Icon = url;
                    _iconSource[kv.Key] = "ui";
                    changed = true;
                }
            }
            if (changed)
            {
                _version++;
                _dumpDirty = true;
            }
        }

        private void LogMissingIcons()
        {
            var missing = new List<string>();
            foreach (var kv in _byName)
                if (IsSurfaceItem(kv.Value) && IconSourceOf(kv.Key) != "ui" && IconSourceOf(kv.Key) != "swatch") missing.Add(kv.Key);
            missing.Sort(StringComparer.Ordinal);
            Mod.Log.Info(missing.Count == 0
                ? "Citymaker: every surface has an icon or a texture swatch."
                : $"Citymaker: {missing.Count} surfaces still have no image: {string.Join(", ", missing)}");
        }
    }
}
