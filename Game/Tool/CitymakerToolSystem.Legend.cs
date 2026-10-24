// Citymaker — preview legend state (s22): which preview layers the overlay draws.
//
// The legend window (UI) hovers a layer to isolate it and clicks to hide / show it. Hover is transient (cleared
// when the pointer leaves the legend, the stage changes or the tool closes); the show / hide choices persist for
// the game session (static: survive closing and reopening the tool, not a restart). Layer ids: Core
// Build/PreviewLegend.

using System;
using System.Collections.Generic;
using Citymaker.Core.Build;

namespace Citymaker.Game.Tool
{
    public sealed partial class CitymakerToolSystem
    {
        private static readonly Dictionary<string, bool> s_legendVisibility = new Dictionary<string, bool>(StringComparer.Ordinal);
        private string _legendHover = "";

        /// <summary>The hovered legend layer ("" = none): the overlay draws only it.</summary>
        public string LegendHover => _legendHover;

        /// <summary>The player's show / hide choices (id → visible); layers not listed use PreviewLegend.DefaultVisible.</summary>
        public IReadOnlyDictionary<string, bool> LegendVisibility => s_legendVisibility;

        /// <summary>Does the overlay draw layer <paramref name="id"/> now?</summary>
        public bool LayerVisible(string id) => PreviewLegend.Visible(id, _legendHover, s_legendVisibility);

        public void HoverLegend(string id)
        {
            id = id ?? "";
            if (id.Length > 64) return;
            if (_legendHover == id) return;
            _legendHover = id;
            Touch();
        }

        public void SetLegendVisible(string id, bool visible)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 64) return;
            if (visible == PreviewLegend.DefaultVisible(id)) s_legendVisibility.Remove(id);
            else s_legendVisibility[id] = visible;
            Touch();
        }

        public void ResetLegend()
        {
            s_legendVisibility.Clear();
            _legendHover = "";
            Touch();
        }
    }
}
