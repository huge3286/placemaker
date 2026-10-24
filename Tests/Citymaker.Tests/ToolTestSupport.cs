using System.Collections.Generic;
using System.Threading;
using Citymaker.Core;
using Unity.Mathematics;

namespace Citymaker.Tests
{
    /// <summary>
    /// Test generator for the tool's design tests. Deliberately WITHOUT [AreaGenerator] so it never
    /// appears in GeneratorRegistry.All for other workstreams' tests; tool code receives it through
    /// an injected resolver.
    /// </summary>
    internal sealed class ToolFakeGenerator : IAreaGenerator
    {
        public string Id { get; set; } = "fake";
        public string NameKey => "Citymaker.Layer.Fake";
        public string Icon => "";
        public LayerKind Kind { get; set; } = LayerKind.Fill;
        public string[] Accepts => new string[0];
        public string[] Emits => new[] { "block" };
        public int Version => 1;
        public bool NeedsEntrance;

        public IReadOnlyList<ControlSchema> Controls => new List<ControlSchema>
        {
            new ControlSchema { Id = "size", Kind = ControlKind.Slider, Min = 10, Max = 100, Step = 1, Default = 50.0, LabelKey = "L.Size", Unit = "m" },
            new ControlSchema { Id = "trees", Kind = ControlKind.Toggle, Default = true, LabelKey = "L.Trees" },
            new ControlSchema
            {
                Id = "style", Kind = ControlKind.Choice, Default = "formal", LabelKey = "L.Style",
                Options = new List<ControlOption> { new ControlOption { Value = "formal" }, new ControlOption { Value = "wild" } },
            },
            new ControlSchema { Id = "benches", Kind = ControlKind.AssetMulti, AssetCategory = AssetCategories.Bench, LabelKey = "L.Benches", Tier = ControlTier.More },
            new ControlSchema { Id = "entrances", Kind = ControlKind.Markers, MarkerKinds = new[] { "entrance" }, LabelKey = "L.Entrances" },
        };

        public IReadOnlyList<PresetInfo> Presets
        {
            get
            {
                var p = new PresetInfo { Id = "big", LabelKey = "P.Big" };
                p.Values.Values["size"] = 90.0;
                p.Values.Values["style"] = "wild";
                return new List<PresetInfo> { p };
            }
        }

        public SettingsBag Defaults()
        {
            var b = new SettingsBag();
            b.Values["size"] = 50.0;
            b.Values["trees"] = true;
            b.Values["style"] = "formal";
            return b;
        }

        public float MinArea => 10;
        public float MaxArea => 1e7f;

        public Readiness CheckReady(AreaInput input)
        {
            if (NeedsEntrance && input.Markers.Count == 0)
                return new Readiness { Ready = false, NextStepKey = "Citymaker.Step.PlaceEntrance", ReasonKey = "Citymaker.Reason.NeedEntrance" };
            return new Readiness();
        }

        public List<EdgeMarker> Suggestions = new List<EdgeMarker>();
        public List<EdgeMarker> SuggestMarkers(AreaInput input) => new List<EdgeMarker>(Suggestions);

        public SettingsBag Randomize(SettingsBag current, uint seed, ICollection<string> locked)
        {
            var r = current.Clone();
            var rng = new Unity.Mathematics.Random(seed == 0 ? 1u : seed);
            // Deliberately ignores `locked` to prove the editor enforces it anyway.
            r.Values["size"] = (double)rng.NextInt(10, 101);
            r.Values["style"] = rng.NextBool() ? "formal" : "wild";
            return r;
        }

        public AreaPlan Plan(AreaInput input, CancellationToken cancel) => new AreaPlan();
    }

    internal static class ToolTestData
    {
        public static float3[] Square(float size, float x0 = 0, float z0 = 0) => new[]
        {
            new float3(x0, 0, z0), new float3(x0 + size, 0, z0), new float3(x0 + size, 0, z0 + size), new float3(x0, 0, z0 + size),
        };
    }
}
