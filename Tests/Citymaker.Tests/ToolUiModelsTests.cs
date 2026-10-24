using System.Collections.Generic;
using Citymaker.Core;
using Citymaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    public class ToolUiModelsTests
    {
        [Fact]
        public void StateHasContractFields()
        {
            var s = new ToolStateInput
            {
                Open = true, Stage = ToolStage.Design, Mode = SelectionMode.Paint, BrushSquare = true, BrushSize = 120,
                AreaValid = true, AreaClosed = true, PointCount = 4, AreaM2 = 1234.56, PerimeterM = 140,
                Primary = new PrimaryAction { Kind = PrimaryKind.ToReview, Label = new LocText("Citymaker.Primary.Review"), Enabled = true },
                CanUndo = true, UndoLabel = new LocText("Citymaker.Undo.MovePoint"),
                MarkerKind = "entrance", MarkerStepPath = "0",
                EditingGroup = 3,
            };
            s.Reachable.AddRange(new[] { ToolStage.Area, ToolStage.Design });
            s.Hints.Add(new HintInfo("Citymaker.Hint.Undo", "Ctrl", "Z"));
            var j = JsonValue.Parse(UiModels.State(s).ToString());
            Assert.True(j["open"].AsBool());
            Assert.Equal("design", j["stage"].AsString());
            Assert.Equal("paint", j["areaMode"].AsString());
            Assert.Equal("square", j["brush"]["shape"].AsString());
            Assert.Equal(120, j["brush"]["size"].AsNumber());
            Assert.Equal(1235, j["area"]["areaM2"].AsNumber());
            Assert.Equal("toReview", j["primary"]["action"].AsString());
            Assert.False(j["primary"].Has("reason"));
            Assert.Equal("Citymaker.Undo.MovePoint", j["undoLabel"]["key"].AsString());
            Assert.False(j.Has("redoLabel"));
            Assert.Equal("entrance", j["markerMode"]["kind"].AsString());
            Assert.Equal("Z", j["hints"].Items[0]["keys"].Items[1].AsString());
            Assert.Equal(3, j["editingGroup"].AsNumber());
            Assert.False(j.Has("anarchy"));
            Assert.False(j["panelCollapsed"].AsBool());
            Assert.Equal(2, j["reachable"].Count);
        }

        [Fact]
        public void CatalogDescribesLayersAndRecipes()
        {
            var j = UiModels.Catalog(new IAreaGenerator[] { new ToolFakeGenerator() },
                new[] { new RecipeListEntry { Id = "fake", Name = "Citymaker.Layer.Fake", DescriptionKey = "D" } });
            var layer = j["layers"].Items[0];
            Assert.Equal("fake", layer["id"].AsString());
            Assert.Equal("fill", layer["kind"].AsString());
            Assert.Equal("block", layer["emits"].Items[0].AsString());
            var size = layer["controls"].Items[0];
            Assert.Equal("slider", size["kind"].AsString());
            Assert.Equal(100, size["max"].AsNumber());
            Assert.Equal("m", size["unit"].AsString());
            var style = layer["controls"].Items[2];
            Assert.Equal("choice", style["kind"].AsString());
            Assert.Equal(2, style["options"].Count);
            Assert.False(style.Has("min"));
            Assert.Equal("more", layer["controls"].Items[3]["tier"].AsString());
            Assert.Equal("assetMulti", layer["controls"].Items[3]["kind"].AsString());
            Assert.Equal("entrance", layer["controls"].Items[4]["markerKinds"].Items[0].AsString());
            Assert.Equal("big", layer["presets"].Items[0]["id"].AsString());
            Assert.Equal("D", j["recipes"].Items[0]["description"].AsString());
            Assert.False(j["recipes"].Items[0]["custom"].AsBool());
        }

        [Fact]
        public void DesignTreeWithRegionsAndOverrides()
        {
            var e = new DesignEditor(id => new ToolFakeGenerator { Id = id });
            var r = e.SingleLayerRecipe(new ToolFakeGenerator(), 1);
            e.AddLayer(r, "0", "park", "block", 2);
            e.OverrideRegion(r, "0", "block:1", "plaza", 3);
            e.SetValue(r, "0", "size", 60.0, true);
            e.AddMarker(r, "0", new EdgeMarker { Kind = "entrance" });
            var plan = new ComposedPlan();
            var root = new AreaPlan { StepPath = "0@root" };
            root.Regions.Add(new PlannedRegion { Key = "block:0", Tags = new[] { "block" }, Ring = ToolTestData.Square(10) });
            root.Regions.Add(new PlannedRegion { Key = "block:1", Tags = new[] { "block" }, Ring = ToolTestData.Square(20) });
            plan.Layers.Add(root);
            var status = new Dictionary<string, UiModels.StepStatus> { ["0/0"] = new UiModels.StepStatus { Ready = false, NextStep = new LocText("N") } };
            var j = JsonValue.Parse(UiModels.Design(r, "fake", true, "0/0", status, plan).ToString());
            Assert.Equal("fake", j["recipeId"].AsString());
            Assert.True(j["modified"].AsBool());
            Assert.Equal("0/0", j["focus"].AsString());
            var rootJ = j["root"];
            Assert.Equal("0", rootJ["path"].AsString());
            Assert.Equal(60, rootJ["values"]["size"].AsNumber());
            Assert.Equal("size", rootJ["locked"].Items[0].AsString());
            Assert.Equal(1, rootJ["markers"].Items[0]["count"].AsNumber());
            Assert.True(rootJ["ready"].AsBool());
            Assert.Equal(2, rootJ["regions"].Count);
            Assert.Equal(400, rootJ["regions"].Items[1]["areaM2"].AsNumber());
            var child = rootJ["children"].Items[0];
            Assert.Equal("0/0", child["path"].AsString());
            Assert.False(child["ready"].AsBool());
            Assert.Equal("N", child["nextStep"]["key"].AsString());
            Assert.Equal("0/0@block:1", rootJ["overrides"]["block:1"]["path"].AsString());
            Assert.Equal("plaza", rootJ["overrides"]["block:1"]["layer"].AsString());
        }

        [Fact]
        public void SummaryPricesLayersAndListsPermanentEffects()
        {
            var plan = new ComposedPlan();
            var l = new AreaPlan { StepPath = "0@root", GeneratorId = "park" };
            l.Objects.Add(new PlannedObject { Asset = new AssetRef("Bench") });
            l.Objects.Add(new PlannedObject { Asset = new AssetRef("Bench") });
            l.Stats.Add(new PlanStat("Citymaker.Stat.Benches", 2, "count"));
            l.Issues.Add(new PlanIssue { Severity = IssueSeverity.Warning, Key = "W", At = new float3(1, 2, 3) });
            l.Terraform.Add(new PlannedTerraform { Permanent = true, Ring = ToolTestData.Square(10), WaterLevel = 5 });
            plan.Layers.Add(l);
            var assets = new Dictionary<string, AssetInfo> { ["Bench"] = new AssetInfo { Id = "Bench", Cost = 100, Upkeep = 2 } };
            var j = JsonValue.Parse(UiModels.Summary(new SummaryInput { Plan = plan, Assets = assets, CanAfford = false }).ToString());
            Assert.True(j["ready"].AsBool());
            Assert.Equal(200, j["cost"].AsNumber());
            Assert.Equal(4, j["upkeep"].AsNumber());
            Assert.False(j["canAfford"].AsBool());
            Assert.Equal("0", j["layers"].Items[0]["stepPath"].AsString());
            Assert.Equal("root", j["layers"].Items[0]["regionKey"].AsString());
            Assert.Equal("warning", j["issues"].Items[0]["severity"].AsString());
            Assert.Equal(3, j["issues"].Items[0]["at"].Items[2].AsNumber());
            Assert.Equal("Citymaker.Permanent.Water", j["permanent"].Items[0]["key"].AsString());
            Assert.Equal("Citymaker.Stat.Benches", j["stats"].Items[0]["key"].AsString());
        }

        [Fact]
        public void EmptySummaryIsNotReady()
        {
            var j = UiModels.Summary(new SummaryInput { Stale = true });
            Assert.False(j["ready"].AsBool());
            Assert.True(j["stale"].AsBool());
            Assert.Equal(0, j["issues"].Count);
        }

        [Fact]
        public void PlanPathOfOverridesAndTheirChildren()
        {
            Assert.Equal("0/1", UiModels.PlanPathOf("0/1@block:3"));
            Assert.Equal("0/1/0", UiModels.PlanPathOf("0/1@block:3/0"));
            Assert.Equal("0/2", UiModels.PlanPathOf("0/2"));
        }
    }
}
