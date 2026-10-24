// Game runner: the scenario and step vocabulary (suite/schema.json, the same file Send-Suite and Run-Suite check
// before anything reaches the game). Unknown keys, steps, targets, views and enum values are errors, never silently
// defaulted; keys starting with '_' are comments.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Citymaker.Game.Suite
{
    internal sealed class SuiteSchema
    {
        private JObject _doc;
        public string LoadError;

        public static SuiteSchema Load(string path)
        {
            var s = new SuiteSchema();
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) s._doc = JObject.Parse(File.ReadAllText(path));
                else s.LoadError = "schema not found: " + path;
            }
            catch (Exception e) { s.LoadError = "schema unreadable: " + e.Message; }
            return s;
        }

        private static IEnumerable<string> Strings(JToken t) => t is JArray a ? a.Select(x => (string)x) : Enumerable.Empty<string>();

        /// <summary>Null when the step is valid, else what is wrong (with the valid choices).</summary>
        public string CheckStep(JObject step)
        {
            if (_doc == null) return null;
            string what = (string)step["do"];
            var steps = _doc["steps"] as JObject;
            if (string.IsNullOrEmpty(what)) return "step without \"do\"";
            if (steps == null) return null;
            if (!(steps[what] is JObject spec)) return $"unknown step '{what}' (steps: {string.Join(", ", steps.Properties().Select(p => p.Name))})";
            var allowed = new HashSet<string>(Strings(spec["keys"]).Concat(Strings(_doc["commonStepKeys"])), StringComparer.Ordinal);
            foreach (var p in step.Properties())
            {
                if (p.Name.StartsWith("_")) continue;
                if (!allowed.Contains(p.Name)) return $"step '{what}': unknown key '{p.Name}' (keys: {string.Join(", ", allowed.OrderBy(x => x))})";
            }
            var bad = Enums(spec["enums"] as JObject, step, "step '" + what + "'") ?? Enums(_doc["commonEnums"] as JObject, step, "step '" + what + "'");
            if (bad != null) return bad;
            if (step["camera"] is JObject cam)
            {
                bad = CheckCamera(cam);
                if (bad != null) return bad;
            }
            if (what == "camera")
            {
                bad = Enums(_doc["camera"]?["enums"] as JObject, step, "camera");
                if (bad != null) return bad;
            }
            return null;
        }

        public string CheckCamera(JObject cam)
        {
            var spec = _doc?["camera"] as JObject;
            if (spec == null) return null;
            var allowed = new HashSet<string>(Strings(spec["keys"]), StringComparer.Ordinal);
            foreach (var p in cam.Properties())
                if (!p.Name.StartsWith("_") && !allowed.Contains(p.Name)) return $"camera: unknown key '{p.Name}' (keys: {string.Join(", ", allowed.OrderBy(x => x))})";
            return Enums(spec["enums"] as JObject, cam, "camera");
        }

        private static string Enums(JObject enums, JObject obj, string where)
        {
            if (enums == null) return null;
            foreach (var e in enums.Properties())
            {
                var t = obj[e.Name];
                if (t == null || t.Type != JTokenType.String) continue;
                var ok = Strings(e.Value).ToList();
                if (!ok.Any(x => string.Equals(x, (string)t, StringComparison.OrdinalIgnoreCase))) return $"{where}: '{e.Name}' must be one of {string.Join(", ", ok)}, not '{(string)t}'";
            }
            return null;
        }

        /// <summary>Unknown scenario keys (not steps), and the "land" keys and values; null when valid.</summary>
        public string CheckScenario(JObject sc)
        {
            if (_doc == null) return null;
            var allowed = new HashSet<string>(Strings(_doc["scenarioKeys"]), StringComparer.Ordinal);
            foreach (var p in sc.Properties())
                if (!p.Name.StartsWith("_") && !allowed.Contains(p.Name)) return $"scenario: unknown key '{p.Name}' (keys: {string.Join(", ", allowed.OrderBy(x => x))})";
            if (sc["land"] is JObject land && _doc["land"] is JObject spec)
            {
                var keys = new HashSet<string>(Strings(spec["keys"]), StringComparer.Ordinal);
                foreach (var p in land.Properties())
                    if (!p.Name.StartsWith("_") && !keys.Contains(p.Name)) return $"land: unknown key '{p.Name}' (keys: {string.Join(", ", keys.OrderBy(x => x))})";
                var bad = Enums(spec["enums"] as JObject, land, "land");
                if (bad != null) return bad;
            }
            return Enums(_doc["scenarioEnums"] as JObject, sc, "scenario");
        }
    }
}
