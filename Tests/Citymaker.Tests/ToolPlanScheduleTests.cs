using Citymaker.Core.Tool;
using Xunit;

namespace Citymaker.Tests
{
    public class ToolPlanScheduleTests
    {
        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public void CommittedEditsPlanImmediately(bool simple, bool detailed)
        {
            Assert.Equal(0f, PlanSchedule.Debounce(false, ValuePreviewKind.None, simple, detailed));
        }

        [Fact]
        public void DragsAreDebouncedOrWaitForTheEnd()
        {
            Assert.Equal(PlanSchedule.DragDebounce, PlanSchedule.Debounce(true, ValuePreviewKind.None, false, false));
            Assert.Equal(PlanSchedule.DragDebounce, PlanSchedule.Debounce(false, ValuePreviewKind.Drag, false, false));
            Assert.Equal(PlanSchedule.DragDebounceDetailed, PlanSchedule.Debounce(false, ValuePreviewKind.Drag, false, true));
            Assert.True(PlanSchedule.Debounce(true, ValuePreviewKind.None, true, false) < 0f);
        }

        [Fact]
        public void HoverReplansQuicklyEvenWithSimpleDetail()
        {
            Assert.Equal(PlanSchedule.HoverDebounce, PlanSchedule.Debounce(false, ValuePreviewKind.Hover, true, false));
        }

        [Fact]
        public void ProgressQueuedThenRisesButNeverCompletes()
        {
            Assert.Equal(0, PlanSchedule.Progress(-1, 200));
            double a = PlanSchedule.Progress(10, 200), b = PlanSchedule.Progress(200, 200), c = PlanSchedule.Progress(100000, 200);
            Assert.True(a > 0 && a < b && b < c);
            Assert.True(c <= 0.95);
            Assert.InRange(PlanSchedule.Progress(100, 0), 0.05, 0.95);   // unknown expected duration
        }
    }
}
