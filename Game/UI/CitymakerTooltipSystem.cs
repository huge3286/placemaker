// Citymaker — cursor tooltips: the most important hints for the current phase plus live numbers
// (area, cost, first plan stat). Pattern from Grid Road Generator's GridRoadTooltipSystem (adapted
// from CS2-NetworkTools, Luca Rager, MIT). Texts are resolved through the active locale ourselves
// because our keys use positional {0} arguments.

using System;
using System.Collections.Generic;
using System.Globalization;
using Game.SceneFlow;
using Game.Tools;
using Game.UI.Localization;
using Game.UI.Tooltip;
using Citymaker.Core.Tool;
using Citymaker.Game.Tool;
using UnityEngine.Scripting;

namespace Citymaker.Game.UI
{
    public sealed partial class CitymakerTooltipSystem : TooltipSystemBase
    {
        private const int MaxHints = 3;

        private ToolSystem _toolSystem;
        private CitymakerToolSystem _tool;
        private readonly List<StringTooltip> _hints = new List<StringTooltip>();
        private StringTooltip _problem;
        private FloatTooltip _area;
        private FloatTooltip _cost;
        private FloatTooltip _stat;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _toolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            _tool = World.GetOrCreateSystemManaged<CitymakerToolSystem>();
            for (int i = 0; i < MaxHints; i++) _hints.Add(new StringTooltip { path = "citymakerHint" + i });
            _problem = new StringTooltip { path = "citymakerProblem", color = TooltipColor.Warning };
            _area = new FloatTooltip { path = "citymakerArea", unit = "area" };
            _cost = new FloatTooltip { path = "citymakerCost", unit = "money" };
            _stat = new FloatTooltip { path = "citymakerStat", unit = "integer" };
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (_toolSystem.activeTool != _tool) return;
            if (ToolRegistration.Settings != null && !ToolRegistration.Settings.ShowCursorTooltips) return;
            try
            {
                var problem = _tool.AreaProblem();
                if (problem != null)
                {
                    _problem.value = LocalizedString.Value(Text(problem));
                    AddMouseTooltip(_problem);
                }
                var hints = _tool.Hints();
                for (int i = 0; i < hints.Count && i < MaxHints; i++)
                {
                    var h = hints[i];
                    string text = Text(h.Text);
                    if (h.Keys.Length > 0) text = "[" + string.Join("+", h.Keys) + "] " + text;
                    _hints[i].value = LocalizedString.Value(text);
                    _hints[i].color = i == 0 && _tool.FlashText != null ? TooltipColor.Warning : TooltipColor.Info;
                    AddMouseTooltip(_hints[i]);
                }
                double area = _tool.AreaM2;
                if (area > 0 && _tool.Stage != ToolStage.Manage)
                {
                    _area.label = LocalizedString.Value(Text(new LocText("Citymaker.Tooltip.Area")));
                    _area.value = (float)Math.Round(area);
                    AddMouseTooltip(_area);
                }
                var plan = _tool.Plan;
                if (plan != null && (_tool.Stage == ToolStage.Design || _tool.Stage == ToolStage.Review))
                {
                    _tool.CanAfford();
                    if (_tool.PlanCost > 0)
                    {
                        _cost.label = LocalizedString.Value(Text(new LocText("Citymaker.Tooltip.Cost")));
                        _cost.value = (float)Math.Round(_tool.PlanCost);
                        _cost.color = _tool.CanAfford() ? TooltipColor.Info : TooltipColor.Error;
                        AddMouseTooltip(_cost);
                    }
                    var stats = plan.MergedStats();
                    if (stats.Count > 0)
                    {
                        _stat.label = LocalizedString.Value(Text(new LocText(stats[0].Key)));
                        _stat.value = (float)stats[0].Value;
                        _stat.unit = UnitFor(stats[0].Unit);
                        AddMouseTooltip(_stat);
                    }
                }
            }
            catch (Exception e)
            {
                if (_errors++ < 5) Mod.Log.Warn("Citymaker: tooltip update failed: " + e.Message);
            }
        }

        private int _errors;

        private static string UnitFor(string unit)
        {
            switch (unit)
            {
                case "m2": case "area": return "area";
                case "m": case "length": return "length";
                case "money": return "money";
                case "%": return "percentage";
                default: return "integer";
            }
        }

        /// <summary>Localised text for a key with positional args ({0}, {1}…); falls back to the key.</summary>
        public static string Text(LocText t)
        {
            if (t == null) return "";
            string s = t.Key;
            try
            {
                var dict = GameManager.instance?.localizationManager?.activeDictionary;
                if (dict != null && dict.TryGetValue(t.Key, out var v) && !string.IsNullOrEmpty(v)) s = v;
                if (t.Args.Length > 0) s = string.Format(CultureInfo.CurrentCulture, s, t.Args);
            }
            catch (FormatException) { }
            catch (Exception) { }
            return s;
        }
    }
}
