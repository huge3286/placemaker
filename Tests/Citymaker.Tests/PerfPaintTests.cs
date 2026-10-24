// Paint / Block paint per-frame costs (s15, playtest: "the lag is pretty bad when I'm painting — maybe 5 FPS").
// Micro-benchmarks with generous budgets (TimingBudget scales them on loaded machines). What runs per frame
// while a stroke is held: BrushMask.Stroke/Stamp and RawBoundary (overlay). The outline pipelines below run
// once on mouse-up since s15; their budgets keep that one hitch small.

using System.Collections.Generic;
using System.Diagnostics;
using Citymaker.Core.Geometry;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Citymaker.Tests
{
    [Collection(TimingCollection.Name)]
    public class PerfPaintTests
    {
        private readonly ITestOutputHelper _out;
        public PerfPaintTests(ITestOutputHelper output) { _out = output; }

        /// <summary>A zig-zag stroke covering a size×size area, as a player sweeps it (one point per frame).</summary>
        private static List<float2> Sweep(float size, float step, float lane)
        {
            var pts = new List<float2>();
            bool right = true;
            for (float z = 0; z <= size; z += lane)
            {
                for (float x = 0; x <= size; x += step) pts.Add(new float2(right ? x : size - x, z));
                right = !right;
            }
            return pts;
        }

        private static (long ms, int frames) Paint(BrushMask m, float brush, List<float2> pts, bool raw)
        {
            var sw = Stopwatch.StartNew();
            m.Stamp(pts[0], brush * 0.5f, false);
            for (int i = 1; i < pts.Count; i++)
            {
                m.Stroke(pts[i - 1], pts[i], brush * 0.5f, false);
                if (raw) m.RawBoundary();   // the overlay re-reads the boundary whenever the mask changed
            }
            return (sw.ElapsedMilliseconds, pts.Count);
        }

        [Fact]
        public void Stamps_200_on_300m_area_are_cheap()
        {
            // 200 frames of a 60 m brush sweeping a 300 m square (the playtest situation).
            var pts = Sweep(300f, 25f, 20f);
            while (pts.Count > 200) pts.RemoveAt(pts.Count - 1);
            var m = new BrushMask();
            var (ms, frames) = Paint(m, 60f, pts, raw: false);
            _out.WriteLine($"{frames} strokes, 60 m brush: {ms} ms ({(double)ms / frames:0.00} ms/frame), {m.PaintedCellCount} cells");
            Assert.True(ms <= TimingBudget.Ms(150), $"{ms} ms for {frames} strokes");
        }

        [Fact]
        public void Strokes_with_overlay_boundary_stay_within_a_frame_budget()
        {
            var pts = Sweep(300f, 25f, 20f);
            while (pts.Count > 200) pts.RemoveAt(pts.Count - 1);
            var m = new BrushMask();
            var (ms, frames) = Paint(m, 150f, pts, raw: true);
            _out.WriteLine($"{frames} strokes + RawBoundary, 150 m brush: {ms} ms ({(double)ms / frames:0.00} ms/frame), boundary {m.RawBoundary().Count} segments");
            // < 2 ms per frame on average for stamping + the overlay boundary.
            Assert.True(ms <= TimingBudget.Ms(2.0 * frames), $"{ms} ms for {frames} frames");
        }

        [Fact]
        public void Huge_brush_strokes_stay_bounded()
        {
            // The biggest brush (1000 m) dragged 40 frames: every stamp touches ~50 000 cells.
            var m = new BrushMask();
            var pts = new List<float2>();
            for (int i = 0; i < 40; i++) pts.Add(new float2(i * 30f, 0f));
            var (ms, frames) = Paint(m, 1000f, pts, raw: true);
            _out.WriteLine($"{frames} strokes of a 1000 m brush + RawBoundary: {ms} ms ({(double)ms / frames:0.0} ms/frame), {m.PaintedCellCount} cells");
            Assert.True(ms <= TimingBudget.Ms(25.0 * frames), $"{ms} ms for {frames} frames");
        }

        [Fact]
        public void Paint_outline_of_300m_area_is_one_short_hitch()
        {
            var m = new BrushMask();
            Paint(m, 60f, Sweep(300f, 25f, 20f), raw: false);
            m.Outline();   // warm-up (JIT)
            var sw = Stopwatch.StartNew();
            var o = m.Outline();
            long ms = sw.ElapsedMilliseconds;
            _out.WriteLine($"Paint outline ({m.PaintedCellCount} cells): {ms} ms, {o.Count} points");
            Assert.True(o.Count >= 3);
            Assert.True(ms <= TimingBudget.Ms(150), $"{ms} ms");
        }

        [Fact]
        public void Block_paint_outline_of_300m_block_is_one_short_hitch()
        {
            var block = new[] { new float2(-10, -10), new float2(310, -10), new float2(310, 310), new float2(-10, 310) };
            var obstacles = new List<IReadOnlyList<float2>>();
            // A row of 20 building lots along one side (the typical city block edge).
            for (int i = 0; i < 20; i++)
                obstacles.Add(new[] { new float2(i * 16f, -10), new float2(i * 16f + 14f, -10), new float2(i * 16f + 14f, 30), new float2(i * 16f, 30) });
            var bb = new BlockBrush(block, obstacles);
            var m = new BrushMask(bb.CellSize);
            bb.Attach(m);
            Paint(m, 60f, Sweep(300f, 25f, 20f), raw: false);
            bb.Outline(m);
            var sw = Stopwatch.StartNew();
            var o = bb.Outline(m);
            long ms = sw.ElapsedMilliseconds;
            _out.WriteLine($"Block paint outline ({m.PaintedCellCount} cells, {obstacles.Count} lots): {ms} ms");
            Assert.NotNull(o?.Ring);
            Assert.True(ms <= TimingBudget.Ms(400), $"{ms} ms");
        }

        /// <summary>
        /// The whole outline rebuild of a painted area as the tool ran it ~7×/s while dragging before s15:
        /// outline + keep-off-roads clip against a street grid + attachment search. Now once per mouse-up.
        /// </summary>
        [Fact]
        public void Outline_rebuild_with_road_clip_measured()
        {
            var m = new BrushMask();
            Paint(m, 60f, Sweep(300f, 25f, 20f), raw: false);
            var nets = new List<Citymaker.Core.NearbyNet>();
            for (int k = -1; k <= 4; k++)
                for (int s = -1; s < 4; s++)
                {
                    float c = k * 100f - 20f, a = s * 100f - 20f, b = a + 100f;
                    nets.Add(Net(new float2(c, a), new float2(c, b)));
                    nets.Add(Net(new float2(a, c), new float2(b, c)));
                }
            long Rebuild()
            {
                var sw = Stopwatch.StartNew();
                var o = m.Outline();
                var ring = new float3[o.Count];
                for (int i = 0; i < ring.Length; i++) ring[i] = o[i];
                var shape = new Citymaker.Core.AreaShape { Ring = ring, Source = Citymaker.Core.AreaSource.Painted };
                shape.Attachments = Citymaker.Core.Tool.AttachmentFinder.Find(shape.Ring, nets);
                Citymaker.Core.Tool.AreaClip.Apply(shape, nets);
                return sw.ElapsedMilliseconds;
            }
            Rebuild();
            long ms = Rebuild();
            _out.WriteLine($"outline + attachments + keep-off-roads ({nets.Count} street pieces): {ms} ms per rebuild; at ~7 rebuilds/s while dragging that was ~{ms * 7} ms of every second");
            Assert.True(ms <= TimingBudget.Ms(500), $"{ms} ms");
        }

        private static Citymaker.Core.NearbyNet Net(float2 a, float2 d) => new Citymaker.Core.NearbyNet
        {
            A = new float3(a.x, 0, a.y), B = new float3(math.lerp(a, d, 1f / 3f).x, 0, math.lerp(a, d, 1f / 3f).y),
            C = new float3(math.lerp(a, d, 2f / 3f).x, 0, math.lerp(a, d, 2f / 3f).y), D = new float3(d.x, 0, d.y),
            Width = 16f, Category = Citymaker.Core.NetCategory.Road, HasSidewalk = true,
        };

        [Fact]
        public void Obstacle_cut_of_painted_outline_is_cheap()
        {
            var m = new BrushMask();
            Paint(m, 60f, Sweep(300f, 25f, 20f), raw: false);
            var outline = m.Outline();
            var ring = new float2[outline.Count];
            for (int i = 0; i < ring.Length; i++) ring[i] = outline[i].xz;
            var lots = new List<float2[]>();
            for (int i = 0; i < 6; i++)
                for (int j = 0; j < 6; j++)
                    lots.Add(new[] { new float2(20 + i * 45f, 20 + j * 45f), new float2(36 + i * 45f, 20 + j * 45f), new float2(36 + i * 45f, 44 + j * 45f), new float2(20 + i * 45f, 44 + j * 45f) });
            ObstacleCut.Subtract(ring, ObstacleCut.Grow(lots, 0.5f), out _);
            var sw = Stopwatch.StartNew();
            var cut = ObstacleCut.Subtract(ring, ObstacleCut.Grow(lots, 0.5f), out int touched);
            long ms = sw.ElapsedMilliseconds;
            _out.WriteLine($"Cut {touched} lots out of the outline: {ms} ms");
            Assert.NotNull(cut);
            Assert.True(ms <= TimingBudget.Ms(300), $"{ms} ms");
        }
    }
}
