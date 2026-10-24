// Citymaker — asset survey dump (GPL-3.0).
// After every catalog scan (and when icons change) ModsData/Citymaker/catalog-dump.json is written:
// { "version", "written", "categories": { category: [ {id, displayName, tags, auto, vanilla, bounds,
// icon, iconSource}, … ] } } so the content team can design new park types from real assets.
// See docs/ASSET-SURVEY-HOWTO.md.

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Citymaker.Game.Settings;

namespace Citymaker.Game.Assets
{
    public sealed partial class AssetCatalogSystem
    {
        public const string DumpFileName = "catalog-dump.json";
        /// <summary>Setting hook: write the survey dump (default on; a few hundred KB, written at most every 10 s).</summary>
        public static bool WriteSurveyDump = true;
        private const float DumpMinInterval = 10f;
        private bool _dumpDirty;
        private float _lastDumpTime = -1000f;

        private void MaybeWriteDump(bool force)
        {
            if (!WriteSurveyDump || !_ready) return;
            if (!force && !_dumpDirty) return;
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (!force && now - _lastDumpTime < DumpMinInterval) return;
            _dumpDirty = false;
            _lastDumpTime = now;
            try
            {
                var path = Path.Combine(ModPaths.DataFolder, DumpFileName);
                ModPaths.WriteAllTextAtomic(path, BuildDump().ToString(Formatting.Indented));
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Citymaker could not write the asset survey dump: " + e.Message);
            }
        }

        private JObject BuildDump()
        {
            var categories = new JObject();
            var keys = new List<string>(_byCategory.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var c in keys)
            {
                var arr = new JArray();
                foreach (var it in _byCategory[c])
                {
                    var tags = it.Tags ?? Array.Empty<string>();
                    var o = new JObject
                    {
                        ["id"] = it.Id,
                        ["displayName"] = it.DisplayName,
                        ["tags"] = new JArray(tags),
                        ["auto"] = Array.IndexOf(tags, "auto:" + c) >= 0,
                        ["vanilla"] = Array.IndexOf(tags, "source:vanilla") >= 0,
                        ["icon"] = it.Icon ?? "",
                        ["iconSource"] = IconSourceOf(it.Id),
                    };
                    var i = it.Info;
                    if ((i.BoundsMax.x > i.BoundsMin.x || i.BoundsMax.z > i.BoundsMin.z))
                        o["bounds"] = new JObject
                        {
                            ["min"] = new JArray(Round(i.BoundsMin.x), Round(i.BoundsMin.y), Round(i.BoundsMin.z)),
                            ["max"] = new JArray(Round(i.BoundsMax.x), Round(i.BoundsMax.y), Round(i.BoundsMax.z)),
                            ["size"] = new JArray(Round(i.BoundsMax.x - i.BoundsMin.x), Round(i.BoundsMax.y - i.BoundsMin.y), Round(i.BoundsMax.z - i.BoundsMin.z)),
                        };
                    if (i.NetWidth > 0f) o["netWidth"] = Round(i.NetWidth);
                    if (i.Cost > 0f) o["cost"] = Round(i.Cost);
                    arr.Add(o);
                }
                categories[c] = arr;
            }
            var pools = new JObject();
            foreach (var kv in _autoPools) pools[kv.Key] = new JArray(kv.Value);
            return new JObject
            {
                ["version"] = _version,
                ["written"] = DateTime.UtcNow.ToString("o"),
                ["counts"] = new JObject(BuildCounts()),
                ["automaticPools"] = pools,
                ["palettes"] = new JArray(_palettes),
                ["categories"] = categories,
            };
        }

        private IEnumerable<JProperty> BuildCounts()
        {
            var keys = new List<string>(_byCategory.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var c in keys) yield return new JProperty(c, _byCategory[c].Count);
        }

        private static double Round(float v) => Math.Round(v, 2);
    }
}
