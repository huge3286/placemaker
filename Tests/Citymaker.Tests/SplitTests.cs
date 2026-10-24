using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Citymaker.Core;
using Citymaker.Core.Geometry;
using Citymaker.Core.Layers.Common;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    public class SplitTests
    {
        private static double AreaOf(PlannedRegion r) => Poly.Area(Poly.To2D(r.Ring));

        public static IEnumerable<object[]> Cases()
        {
            foreach (var shape in new[] { "rect", "L", "blob" })
                foreach (var ratio in new[] { 50.0, 25.0, 80.0 })
                    foreach (var angle in new[] { 0.0, 45.0, 90.0 })
                        yield return new object[] { shape, ratio, angle };
        }

        private static float3[] Shape(string name) =>
            name == "rect" ? TestKit.Rect(200, 120) : name == "L" ? TestKit.LShape(150) : TestKit.Blob(90, 64, 11);

        [Theory]
        [MemberData(nameof(Cases))]
        public void RatioSplitPartsSumToTheWhole(string shape, double ratio, double angle)
        {
            var ring = Shape(shape);
            var s = new SettingsBag(); s.Values["ratio"] = ratio; s.Values["angle"] = angle;
            var plan = TestKit.Plan("split", TestKit.Input(ring, s));
            Assert.False(plan.HasErrors);
            var parts = plan.Regions.Where(r => r.Tags.Contains(RegionTags.Part)).ToList();
            Assert.Equal(2, parts.Count);
            double total = Poly.Area(Poly.To2D(ring));
            Assert.InRange(parts.Sum(AreaOf), total * 0.99, total * 1.01);
            Assert.DoesNotContain(plan.Issues, i => i.Key == "Citymaker.Split.Issue.CannotSplit");
                Assert.InRange(AreaOf(parts[0]) / total, ratio / 100 - 0.01, ratio / 100 + 0.01);
            Assert.Equal(new[] { "part", "part:0" }, parts[0].Tags);
            Assert.Equal("part:1", parts[1].Key);
            foreach (var p in parts) Assert.True(Poly.IsSimple(Poly.To2D(p.Ring)));
        }

        [Theory]
        [InlineData(3)]
        [InlineData(4)]
        public void EqualPartsForMoreThanTwo(int count)
        {
            var s = new SettingsBag(); s.Values["parts"] = (double)count;
            var plan = TestKit.Plan("split", TestKit.Input(TestKit.Rect(300, 100), s));
            var parts = plan.Regions.Where(r => r.Tags.Contains(RegionTags.Part)).ToList();
            Assert.Equal(count, parts.Count);
            foreach (var p in parts) Assert.InRange(AreaOf(p), 30000.0 / count * 0.99, 30000.0 / count * 1.01);
        }

        [Fact]
        public void PlayerDividerDefinesTheCut()
        {
            // Diagonal cut from the bottom edge to the right edge of a 100×100 square.
            var markers = new List<EdgeMarker>
            {
                new EdgeMarker { Kind = "split", Position = new float3(50, 0, 0) },
                new EdgeMarker { Kind = "split", Position = new float3(100, 0, 50) },
            };
            var plan = TestKit.Plan("split", TestKit.Input(TestKit.Rect(100, 100), markers: markers));
            var parts = plan.Regions.Where(r => r.Tags.Contains(RegionTags.Part)).OrderBy(AreaOf).ToList();
            Assert.Equal(2, parts.Count);
            Assert.Equal(1250, AreaOf(parts[0]), 1);
            Assert.Equal(8750, AreaOf(parts[1]), 1);
        }

        [Fact]
        public void TwoDividersMakeThreeParts()
        {
            var markers = new List<EdgeMarker>
            {
                new EdgeMarker { Kind = "split", Position = new float3(30, 0, 0) },
                new EdgeMarker { Kind = "split", Position = new float3(30, 0, 100) },
                new EdgeMarker { Kind = "split", Position = new float3(70, 0, 0) },
                new EdgeMarker { Kind = "split", Position = new float3(70, 0, 100) },
            };
            var plan = TestKit.Plan("split", TestKit.Input(TestKit.Rect(100, 100), markers: markers));
            var areas = plan.Regions.Where(r => r.Tags.Contains(RegionTags.Part)).Select(AreaOf).OrderBy(a => a).ToList();
            Assert.Equal(3, areas.Count);
            Assert.Equal(3000, areas[0], 1);
            Assert.Equal(3000, areas[1], 1);
            Assert.Equal(4000, areas[2], 1);
        }

        [Fact]
        public void DividerPathIsOptional()
        {
            Assert.Empty(TestKit.Plan("split", TestKit.Input(TestKit.Rect(100, 60))).Nets);
            var s = new SettingsBag(); s.Values["divider"] = "wide";
            var plan = TestKit.Plan("split", TestKit.Input(TestKit.Rect(100, 60), s));
            Assert.Single(plan.Nets);
            Assert.Equal(NetRole.PathwayWide, plan.Nets[0].Role);
            // Edge to edge, its free ends kept inside the outline by its half width (no cap sticking out).
            Assert.Equal(60 - 2 * Composer.DefaultHalfWidth(NetRole.PathwayWide), math.distance(plan.Nets[0].A.xz, plan.Nets[0].D.xz), 1);
        }

        [Fact]
        public void SplitParkPlazaComposesEndToEnd()
        {
            var recipe = TestKit.LoadRecipe("split-park-plaza.json");
            var ring = TestKit.Rect(240, 140);
            var plan = Composer.Compose(recipe, new AreaShape { Ring = ring }, TestKit.RoadAlongBottom(240), CancellationToken.None);
            Assert.False(plan.HasErrors);
            var park = plan.Layers.Single(l => l.GeneratorId == "park");
            var plaza = plan.Layers.Single(l => l.GeneratorId == "plaza");
            Assert.EndsWith("@part:0", park.StepPath);
            Assert.EndsWith("@part:1", plaza.StepPath);
            // Area stats of the children add up to the whole selection.
            double area = plan.MergedStats().Where(s => s.Key == Keys.StatArea).Sum(s => s.Value);
            Assert.InRange(area, 240 * 140 * 0.99, 240 * 140 * 1.01);
            // Neither child builds into the other's part.
            var parkPart = Poly.To2D(plan.Layers[0].Regions.Single(r => r.Key == "part:0").Ring);
            var plazaPart = Poly.To2D(plan.Layers[0].Regions.Single(r => r.Key == "part:1").Ring);
            foreach (var o in park.Objects) Assert.False(Poly.Contains(plazaPart, o.Position.xz) && Poly.DistanceToBoundary(plazaPart, o.Position.xz) > 0.01f);
            foreach (var o in plaza.Objects) Assert.False(Poly.Contains(parkPart, o.Position.xz) && Poly.DistanceToBoundary(parkPart, o.Position.xz) > 0.01f);
        }

        [Fact]
        public void SameSeedSamePlan()
        {
            var s = new SettingsBag(); s.Values["angle"] = 30.0;
            Assert.Equal(TestKit.Fingerprint(TestKit.Plan("split", TestKit.Input(TestKit.LShape(120), s))),
                TestKit.Fingerprint(TestKit.Plan("split", TestKit.Input(TestKit.LShape(120), s))));
        }
    }
}
