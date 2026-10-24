using Citymaker.Core.Tool;
using Xunit;

namespace Citymaker.Tests
{
    public class ToolHistoryTests
    {
        private sealed class S { public int V; public S(int v) { V = v; } }

        [Fact]
        public void UndoRedoRestoresStatesWithLabels()
        {
            var h = new History<S>();
            h.Reset(new S(0));
            h.Commit(new S(1), new LocText("A"));
            h.Commit(new S(2), new LocText("B"));
            Assert.True(h.CanUndo);
            Assert.Equal("B", h.UndoLabel.Key);
            Assert.Equal(1, h.Undo().V);
            Assert.Equal("B", h.RedoLabel.Key);
            Assert.Equal("A", h.UndoLabel.Key);
            Assert.Equal(0, h.Undo().V);
            Assert.Null(h.Undo());
            Assert.Equal(1, h.Redo().V);
            Assert.Equal(2, h.Redo().V);
            Assert.Null(h.Redo());
            Assert.Equal(2, h.Current.V);
        }

        [Fact]
        public void NewCommitClearsRedo()
        {
            var h = new History<S>();
            h.Reset(new S(0));
            h.Commit(new S(1), new LocText("A"));
            h.Undo();
            Assert.True(h.CanRedo);
            h.Commit(new S(5), new LocText("C"));
            Assert.False(h.CanRedo);
            Assert.Equal(0, h.Undo().V);
        }

        [Fact]
        public void CoalescingMergesRepeatedEditsOfSameControl()
        {
            var h = new History<S>();
            h.Reset(new S(0));
            h.Commit(new S(1), new LocText("Size"), "0:size", 10.0);
            h.Commit(new S(2), new LocText("Size"), "0:size", 10.5);
            h.Commit(new S(3), new LocText("Size"), "0:size", 11.0);
            Assert.Equal(1, h.UndoDepth);
            Assert.Equal(0, h.Undo().V);
        }

        [Fact]
        public void CoalescingStopsAfterWindowOrBreakOrOtherKey()
        {
            var h = new History<S>();
            h.Reset(new S(0));
            h.Commit(new S(1), new LocText("Size"), "0:size", 0);
            h.Commit(new S(2), new LocText("Size"), "0:size", 100);
            Assert.Equal(2, h.UndoDepth);
            h.BreakCoalescing();
            h.Commit(new S(3), new LocText("Size"), "0:size", 100.1);
            Assert.Equal(3, h.UndoDepth);
            h.Commit(new S(4), new LocText("Style"), "0:style", 100.2);
            Assert.Equal(4, h.UndoDepth);
        }

        [Fact]
        public void UndoBreaksCoalescing()
        {
            var h = new History<S>();
            h.Reset(new S(0));
            h.Commit(new S(1), new LocText("Size"), "k", 0);
            h.Commit(new S(2), new LocText("Size"), "k2", 0.1);
            h.Undo();
            h.Commit(new S(3), new LocText("Size"), "k", 0.2);
            Assert.Equal(2, h.UndoDepth);
        }

        [Fact]
        public void SameStateCommitIsIgnored()
        {
            var h = new History<S>(sameState: (a, b) => a.V == b.V);
            h.Reset(new S(0));
            Assert.False(h.Commit(new S(0), new LocText("noop")));
            Assert.False(h.CanUndo);
        }

        [Fact]
        public void DepthIsBounded()
        {
            var h = new History<S>(maxDepth: 3);
            h.Reset(new S(0));
            for (int i = 1; i <= 10; i++) h.Commit(new S(i), new LocText("x"));
            Assert.Equal(3, h.UndoDepth);
            h.Undo(); h.Undo();
            Assert.Equal(7, h.Undo().V);
            Assert.Null(h.Undo());
        }

        [Fact]
        public void RevisionChangesOnEveryStateChange()
        {
            var h = new History<S>();
            h.Reset(new S(0));
            int r0 = h.Revision;
            h.Commit(new S(1), null);
            Assert.NotEqual(r0, h.Revision);
            int r1 = h.Revision;
            h.Undo();
            Assert.NotEqual(r1, h.Revision);
        }
    }
}
