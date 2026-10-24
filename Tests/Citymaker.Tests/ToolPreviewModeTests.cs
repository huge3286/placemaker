using Citymaker.Core;
using Citymaker.Core.Tool;
using Xunit;

namespace Citymaker.Tests
{
    public class ToolPreviewModeTests
    {
        private static PreviewInput Ready(ToolStage stage, int rev = 1, int shown = -1) => new PreviewInput
        {
            Stage = stage, ToolActive = true, PlanAvailable = true, PlanRevision = rev, GhostAvailable = true, GhostShownRevision = shown,
        };

        [Fact]
        public void DefaultsDesignSketchReviewGhost()
        {
            var m = new PreviewModeMachine();
            Assert.Equal(PreviewMode.Sketch, m.Selected(ToolStage.Design));
            Assert.Equal(PreviewMode.Ghost, m.Selected(ToolStage.Review));
            Assert.Equal(PreviewMode.Sketch, m.Selected(ToolStage.Area));
            Assert.Equal(PreviewMode.Sketch, m.Selected(ToolStage.Manage));
        }

        [Fact]
        public void DesignSketchSendsNothing()
        {
            var m = new PreviewModeMachine();
            var d = m.Step(Ready(ToolStage.Design));
            Assert.Equal(GhostCommand.None, d.Command);
            Assert.Equal(PreviewMode.Sketch, d.Effective);
            Assert.Equal(SketchObjects.Full, d.Objects);
            Assert.False(d.GhostPending);
        }

        [Fact]
        public void ReviewShowsGhostOncePerRevisionAndIsPendingUntilShown()
        {
            var m = new PreviewModeMachine();
            var d = m.Step(Ready(ToolStage.Review, 3));
            Assert.Equal(GhostCommand.Show, d.Command);
            Assert.Equal(3, d.ShowRevision);
            Assert.True(d.GhostPending);
            Assert.Equal(SketchObjects.Full, d.Objects);        // nothing on the map yet: keep the sketch

            d = m.Step(Ready(ToolStage.Review, 3));
            Assert.Equal(GhostCommand.None, d.Command);          // no repeat

            d = m.Step(Ready(ToolStage.Review, 3, shown: 3));
            Assert.False(d.GhostPending);
            Assert.Equal(SketchObjects.Hidden, d.Objects);
            Assert.Equal(PreviewMode.Ghost, d.Effective);
        }

        [Fact]
        public void NewRevisionReShowsAndDimsWhileCatchingUp()
        {
            var m = new PreviewModeMachine();
            m.Step(Ready(ToolStage.Review, 1));
            var d = m.Step(Ready(ToolStage.Review, 2, shown: 1));
            Assert.Equal(GhostCommand.Show, d.Command);
            Assert.Equal(2, d.ShowRevision);
            Assert.True(d.GhostPending);
            Assert.Equal(SketchObjects.Dimmed, d.Objects);
        }

        [Fact]
        public void StalePlanWaitsForTheFreshOne()
        {
            var m = new PreviewModeMachine();
            m.Step(Ready(ToolStage.Review, 1));
            var i = Ready(ToolStage.Review, 1, shown: 1);
            i.PlanStale = true;
            var d = m.Step(i);
            Assert.Equal(GhostCommand.None, d.Command);
            Assert.True(d.GhostPending);
            Assert.Equal(SketchObjects.Dimmed, d.Objects);
        }

        [Fact]
        public void SliderDragShowsSketchThenGhostReturns()
        {
            var m = new PreviewModeMachine();
            m.Step(Ready(ToolStage.Review, 1));
            var drag = Ready(ToolStage.Review, 2, shown: 1);
            drag.ValuePreview = ValuePreviewKind.Drag;
            var d = m.Step(drag);
            Assert.Equal(GhostCommand.Hide, d.Command);
            Assert.Equal(PreviewMode.Sketch, d.Effective);
            Assert.Equal(PreviewMode.Ghost, d.Selected);         // the toggle does not flicker
            Assert.Equal(SketchObjects.Full, d.Objects);
            Assert.False(d.GhostPending);

            d = m.Step(drag);
            Assert.Equal(GhostCommand.None, d.Command);          // hide once

            d = m.Step(Ready(ToolStage.Review, 3));              // drag ended
            Assert.Equal(GhostCommand.Show, d.Command);
            Assert.Equal(3, d.ShowRevision);
            Assert.Equal(PreviewMode.Ghost, d.Effective);
        }

        [Fact]
        public void OutlineGestureAlsoShowsSketch()
        {
            var m = new PreviewModeMachine();
            m.Step(Ready(ToolStage.Review, 1));
            var i = Ready(ToolStage.Review, 1, shown: 1);
            i.Dragging = true;
            Assert.Equal(GhostCommand.Hide, m.Step(i).Command);
        }

        [Fact]
        public void AssetHoverKeepsGhostAndFollowsNewRevisions()
        {
            var m = new PreviewModeMachine();
            m.Step(Ready(ToolStage.Review, 1));
            var hover = Ready(ToolStage.Review, 2, shown: 1);
            hover.ValuePreview = ValuePreviewKind.Hover;
            var d = m.Step(hover);
            Assert.Equal(GhostCommand.Show, d.Command);
            Assert.Equal(2, d.ShowRevision);
            Assert.Equal(PreviewMode.Ghost, d.Effective);
        }

        [Fact]
        public void DesignChoiceSticksAcrossStages()
        {
            var m = new PreviewModeMachine();
            Assert.True(m.Set(ToolStage.Design, PreviewMode.Ghost));
            Assert.False(m.Set(ToolStage.Design, PreviewMode.Ghost));
            Assert.Equal(PreviewMode.Ghost, m.Step(Ready(ToolStage.Design)).Effective);
            m.Step(new PreviewInput { Stage = ToolStage.Area, ToolActive = true });
            m.Step(Ready(ToolStage.Review));
            Assert.Equal(PreviewMode.Ghost, m.Selected(ToolStage.Design));
            Assert.True(m.Set(ToolStage.Review, PreviewMode.Sketch));
            Assert.Equal(PreviewMode.Sketch, m.Selected(ToolStage.Review));
            Assert.Equal(PreviewMode.Ghost, m.Selected(ToolStage.Design));
            Assert.False(m.Set(ToolStage.Area, PreviewMode.Ghost));
        }

        [Fact]
        public void SwitchingToSketchHides()
        {
            var m = new PreviewModeMachine();
            m.Step(Ready(ToolStage.Review, 1));
            m.Set(ToolStage.Review, PreviewMode.Sketch);
            Assert.Equal(GhostCommand.Hide, m.Step(Ready(ToolStage.Review, 1, shown: 1)).Command);
        }

        [Theory]
        [InlineData("area")]
        [InlineData("manage")]
        [InlineData("closed")]
        [InlineData("building")]
        [InlineData("noPlan")]
        [InlineData("noService")]
        public void LeavingHides(string why)
        {
            var m = new PreviewModeMachine();
            m.Step(Ready(ToolStage.Review, 1));
            var i = Ready(ToolStage.Review, 1, shown: 1);
            switch (why)
            {
                case "area": i.Stage = ToolStage.Area; break;
                case "manage": i.Stage = ToolStage.Manage; break;
                case "closed": i.ToolActive = false; break;
                case "building": i.Stage = ToolStage.Building; i.BuildRunning = true; break;
                case "noPlan": i.PlanAvailable = false; break;
                case "noService": i.GhostAvailable = false; break;
            }
            var d = m.Step(i);
            Assert.Equal(GhostCommand.Hide, d.Command);
            Assert.Equal(SketchObjects.Full, d.Objects);
            Assert.Equal(-1, m.RequestedRevision);
            Assert.Equal(GhostCommand.None, m.Step(i).Command);
        }

        [Fact]
        public void ForgetShowsAgainAfterExternalHide()
        {
            var m = new PreviewModeMachine();
            m.Step(Ready(ToolStage.Review, 4));
            m.Forget();
            var d = m.Step(Ready(ToolStage.Review, 4));
            Assert.Equal(GhostCommand.Show, d.Command);
            Assert.Equal(4, d.ShowRevision);
        }

        [Fact]
        public void ParseAndIds()
        {
            Assert.True(PreviewModes.TryParse("ghost", out var g));
            Assert.Equal(PreviewMode.Ghost, g);
            Assert.True(PreviewModes.TryParse("sketch", out var s));
            Assert.Equal(PreviewMode.Sketch, s);
            Assert.False(PreviewModes.TryParse("bogus", out _));
            Assert.Equal("ghost", PreviewModes.Id(PreviewMode.Ghost));
            Assert.Equal("sketch", PreviewModes.Id(PreviewMode.Sketch));
        }

        [Fact]
        public void AssetPickersHoverOthersDrag()
        {
            Assert.Equal(ValuePreviewKind.Hover, ValuePreviews.KindFor(new ControlSchema { Kind = ControlKind.Asset }));
            Assert.Equal(ValuePreviewKind.Hover, ValuePreviews.KindFor(new ControlSchema { Kind = ControlKind.AssetMulti }));
            Assert.Equal(ValuePreviewKind.Drag, ValuePreviews.KindFor(new ControlSchema { Kind = ControlKind.Slider }));
            Assert.Equal(ValuePreviewKind.Drag, ValuePreviews.KindFor(null));
        }

        [Fact]
        public void StateJsonCarriesPreviewMode()
        {
            var json = UiModels.State(new ToolStateInput { PreviewMode = PreviewMode.Ghost, GhostPending = true }).ToString();
            Assert.Contains("\"previewMode\":\"ghost\"", json);
            Assert.Contains("\"ghostPending\":true", json);
        }
    }
}
