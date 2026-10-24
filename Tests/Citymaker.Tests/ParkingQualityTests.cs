// Quality and parity checks ported from ParkingLotTool's GeometryParity harness (KruemelGames,
// GPL-3.0): --lformen (1152 reflex-corner shapes), --qualitaet (uncovered < 0.05 %, min surface
// edge 0.375 m, < 5 s) and the real user polygons in Berichtsfaelle. CI runs a deterministic
// subset of the L-shape sweep; PM_PARKING_FULL_SWEEP=1 runs all 1152 shapes.

using System;
using System.Collections.Generic;
using System.Linq;
using Citymaker.Core;
using Citymaker.Core.Layers.Parking;
using Xunit;
using Xunit.Abstractions;

namespace Citymaker.Tests
{
    public class ParkingQualityTests
    {
        private readonly ITestOutputHelper _out;
        public ParkingQualityTests(ITestOutputHelper output) { _out = output; }

        // PLT --qualitaet thresholds
        private const double MaxUncovered = 0.0005;     // 0.05 %
        private const double MinEdge = 0.375;           // CS2 drops thinner features after its 0.1 m inset
        private const double MaxMillis = 5000;

        private void AssertQuality(ParkingRun r, string name)
        {
            var q = ParkingKit.Check(r);
            _out.WriteLine($"{name}: {q} | {r.Millis:F0} ms");
            Assert.True(q.Problems.Count == 0, name + ": " + string.Join("; ", q.Problems.Take(5)));
            Assert.False(r.Plan.HasErrors, name + ": " + string.Join(",", r.Plan.Issues.Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Key + " " + string.Join(" ", i.Args))));
            Assert.True(q.Stalls > 0, name + ": no stalls");
            Assert.True(q.UncoveredFraction < MaxUncovered, $"{name}: uncovered {q.UncoveredFraction:P3}");
            Assert.True(q.OverlapFraction < 0.0005, $"{name}: overlapping surfaces {q.OverlapFraction:P3}");
            Assert.True(q.MinSurfaceEdge >= MinEdge - 1e-4, $"{name}: min edge {q.MinSurfaceEdge:F3}");
            Assert.Equal(0, q.RejectedSurfaces);
            Assert.True(r.Millis < MaxMillis, $"{name}: {r.Millis:F0} ms");
        }

        [Fact]
        public void ReportCasesFromRealUsers()
        {
            foreach (var f in ParkingKit.Fixtures())
            {
                ParkingKit.Run(f.Ring, f.Settings, f.Markers); // warm-up (JIT)
                var r = ParkingKit.Run(f.Ring, f.Settings, f.Markers);
                AssertQuality(r, f.Name);
                _out.WriteLine($"   PLT {f.PltStalls} stalls in {f.PltMillis:F0} ms → {r.Stalls} stalls in {r.Millis:F0} ms");
                // the port must not lose much capacity against the shipped PLT engine
                Assert.True(r.Stalls >= 0.9 * f.PltStalls, $"{f.Name}: {r.Stalls} stalls vs PLT {f.PltStalls}");
                Assert.True(r.Millis < TimingBudget.Ms(1000), $"{f.Name}: {r.Millis:F0} ms");
            }
        }

        [Fact]
        public void LShapeSweepSubset()
        {
            var shapes = ParkingKit.LShapes();
            bool full = Environment.GetEnvironmentVariable("PM_PARKING_FULL_SWEEP") == "1";
            int step = full ? 1 : 9;
            int count = 0; double worst = 0; string worstName = "";
            for (int i = 0; i < shapes.Count; i += step)
            {
                var s = shapes[i];
                var r = ParkingKit.Run(s.Ring, null, new List<EdgeMarker> { ParkingKit.Marker(s.Ring, 0, 20) });
                AssertQuality(r, s.Name);
                if (r.Millis > worst) { worst = r.Millis; worstName = s.Name; }
                count++;
            }
            _out.WriteLine($"{count} shapes, slowest {worst:F0} ms ({worstName})");
        }

        [Theory]
        [InlineData("60", true)]
        [InlineData("45", true)]
        [InlineData("90", false)]
        [InlineData("60", false)]
        public void LShapeSweepVariants(string angle, bool ring)
        {
            var shapes = ParkingKit.LShapes();
            var bag = new ParkingLayer().Defaults();
            bag.Values["stallAngle"] = angle;
            bag.Values["perimeterRoad"] = ring;
            for (int i = 5; i < shapes.Count; i += 37)
            {
                var s = shapes[i];
                var r = ParkingKit.Run(s.Ring, bag, new List<EdgeMarker> { ParkingKit.Marker(s.Ring, 0, 20) });
                AssertQuality(r, $"{s.Name} {angle}° ring={ring}");
            }
        }

        [Fact]
        public void TwoHectaresUnderOneSecondWorstCaseUnderThree()
        {
            var rect = ParkingKit.Rect(200, 100);
            var m = new List<EdgeMarker> { ParkingKit.Marker(rect, 0, 80) };
            ParkingKit.Run(rect, null, m); // warm-up
            var r = ParkingKit.Run(rect, null, m);
            _out.WriteLine($"2 ha rectangle: {r.Stalls} stalls, {r.Millis:F0} ms");
            Assert.True(r.Millis < TimingBudget.Ms(1000), r.Millis + " ms");

            // worst case: 9 ha slanted L with angled stalls and medians
            var big = ParkingKit.LShapes().First(s => s.Name.StartsWith("U long") && s.Name.Contains("61deg")).Ring;
            var bag = new ParkingLayer().Defaults();
            bag.Values["stallAngle"] = "60";
            var w = ParkingKit.Run(big, bag, new List<EdgeMarker> { ParkingKit.Marker(big, 0, 20) });
            _out.WriteLine($"worst case: {w.Stalls} stalls, {w.Millis:F0} ms");
            Assert.True(w.Millis < TimingBudget.Ms(3000), w.Millis + " ms");

            // a 25 ha lot still finishes (budget of the whole preview)
            var huge = ParkingKit.Rect(500, 500);
            var h = ParkingKit.Run(huge, null, new List<EdgeMarker> { ParkingKit.Marker(huge, 0, 250) });
            _out.WriteLine($"25 ha: {h.Stalls} stalls, {h.Millis:F0} ms");
            Assert.True(h.Millis < TimingBudget.Ms(6000), h.Millis + " ms");
            AssertQuality(h, "25 ha");
        }
    }
}
