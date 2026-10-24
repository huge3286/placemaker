// s15: the brush cursor / Block paint shading only while the brush can actually paint.

using Citymaker.Core.Tool;
using Xunit;

namespace Citymaker.Tests
{
    public class ToolBrushCursorTests
    {
        [Theory]
        [InlineData(SelectionMode.Paint)]
        [InlineData(SelectionMode.BlockPaint)]
        public void Live_only_in_area_stage_over_the_world(SelectionMode mode)
        {
            Assert.True(BrushCursor.Live(true, ToolStage.Area, mode, false, true, false));
            foreach (var st in new[] { ToolStage.Design, ToolStage.Review, ToolStage.Building, ToolStage.Manage })
                Assert.False(BrushCursor.Live(true, st, mode, false, true, false));
            Assert.False(BrushCursor.Live(true, ToolStage.Area, mode, placingMarkers: true, overWorld: true, strokeRunning: false));
            Assert.False(BrushCursor.Live(true, ToolStage.Area, mode, false, overWorld: false, strokeRunning: false));   // over the panel
            Assert.False(BrushCursor.Live(false, ToolStage.Area, mode, false, true, false));                          // tool inactive
        }

        [Fact]
        public void Running_stroke_stays_live_until_mouse_up_even_over_the_panel()
        {
            Assert.True(BrushCursor.Live(true, ToolStage.Area, SelectionMode.Paint, false, overWorld: false, strokeRunning: true));
            Assert.False(BrushCursor.Live(false, ToolStage.Area, SelectionMode.Paint, false, true, true));
        }

        [Theory]
        [InlineData(SelectionMode.Draw)]
        [InlineData(SelectionMode.Block)]
        [InlineData(SelectionMode.Perimeter)]
        public void Other_modes_have_no_brush(SelectionMode mode)
        {
            Assert.False(BrushCursor.Live(true, ToolStage.Area, mode, false, true, false));
            Assert.False(BrushCursor.IsBrushMode(mode));
        }
    }
}
