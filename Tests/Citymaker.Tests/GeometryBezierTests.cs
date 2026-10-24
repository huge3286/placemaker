// Cubic Bézier helpers and curve fitting (owner: geometry).

using System.Collections.Generic;
using System.Linq;
using Citymaker.Core.Geometry;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    public class GeometryBezierTests
    {
        private static CubicBezier QuarterArc(float r)
        {
            // Standard 4/3·tan(θ/4) handles for a 90° arc.
            float k = 4f / 3f * math.tan(math.PI / 8) * r;
            return new CubicBezier(new float3(r, 0, 0), new float3(r, 0, k), new float3(k, 0, r), new float3(0, 0, r));
        }

        [Fact]
        public void Line_PositionTangentLength()
        {
            var b = CubicBezier.Line(new float3(0, 0, 0), new float3(30, 3, 40));
            Assert.Equal(new float3(15, 1.5f, 20), b.Position(0.5f));
            Assert.Equal(math.length(new float3(30, 3, 40)), b.Length(), 3);
            Assert.Equal(50f, b.LengthXZ(), 3);
            Assert.True(math.distance(b.Tangent(0.3f), math.normalize(new float3(30, 3, 40))) < 1e-5f);
        }

        [Fact]
        public void QuarterArc_LengthAndSampling()
        {
            var arc = QuarterArc(100);
            Assert.Equal(math.PI * 50, arc.Length(), 0);
            foreach (var p in arc.Sample(16)) Assert.InRange(math.length(p.xz), 99.9f, 100.03f);
            var even = arc.SampleBySpacing(10f);
            var gaps = even.Zip(even.Skip(1), (a, b) => math.distance(a, b)).ToArray();
            Assert.True(gaps.Max() - gaps.Min() < 0.2f, $"spacing {gaps.Min()}..{gaps.Max()}");
            float t = arc.ParameterAtDistance(arc.Length() * 0.5f);
            Assert.Equal(0.5f, t, 2);
        }

        [Fact]
        public void SplitAndCut_ReproduceTheCurve()
        {
            var arc = QuarterArc(50);
            arc.Split(0.3f, out var l, out var r);
            Assert.True(math.distance(l.Position(1), arc.Position(0.3f)) < 1e-4f);
            Assert.True(math.distance(r.Position(0.5f), arc.Position(0.65f)) < 1e-3f);
            Assert.Equal(arc.Length(), l.Length() + r.Length(), 2);
            var mid = arc.Cut(0.25f, 0.75f);
            Assert.True(math.distance(mid.Position(0), arc.Position(0.25f)) < 1e-4f);
            Assert.True(math.distance(mid.Position(1), arc.Position(0.75f)) < 1e-4f);
            Assert.Equal(arc.A, arc.Reversed().D);
        }

        [Fact]
        public void FromTangents_ApproximatesCircularArc()
        {
            var b = CubicBezier.FromTangents(new float3(100, 0, 0), new float3(0, 0, 1), new float3(0, 0, 100), new float3(-1, 0, 0));
            foreach (var p in b.Sample(20)) Assert.InRange(math.length(p.xz), 99.9f, 100.1f);
            var straight = CubicBezier.FromTangents(float3.zero, new float3(1, 0, 0), new float3(90, 0, 0), new float3(1, 0, 0));
            Assert.Equal(90f, straight.Length(), 3);
        }

        [Fact]
        public void ClosestParameter_FindsFoot()
        {
            var arc = QuarterArc(100);
            float t = arc.ClosestParameterXZ(new float2(200, 200));
            Assert.InRange(math.length(arc.Position(t).xz - new float2(70.71f, 70.71f)), 0, 0.2f);
        }

        [Fact]
        public void FitCubics_StaysWithinError()
        {
            var pts = new List<float3>();
            for (int i = 0; i <= 200; i++)
            {
                float x = i * 2f;
                pts.Add(new float3(x, 0, 40 * math.sin(x / 60f)));
            }
            var curves = BezierFit.FitCubics(pts, 0.5f);
            Assert.InRange(curves.Count, 1, 20);
            // Chain is continuous and every input point is near the chain.
            for (int i = 1; i < curves.Count; i++) Assert.True(math.distance(curves[i - 1].D, curves[i].A) < 1e-4f);

            foreach (var p in pts)
                Assert.True(curves.Min(c => math.distance(c.Position(c.ClosestParameterXZ(p.xz)), p)) < 0.55f, $"point {p} too far");
            Assert.Equal(pts[0], curves[0].A);
            Assert.Equal(pts[pts.Count - 1], curves[curves.Count - 1].D);
        }

        [Fact]
        public void FitCubic_SingleCurveForArcPolyline()
        {
            var arc = QuarterArc(80);
            var pts = arc.Sample(30).ToList();
            var fit = BezierFit.FitCubic(pts);
            foreach (var p in pts) Assert.True(math.distance(fit.Position(fit.ClosestParameterXZ(p.xz)), p) < 0.05f);
            Assert.Empty(BezierFit.FitCubics(new[] { float3.zero }, 1f));
            Assert.Single(BezierFit.FitCubics(new[] { float3.zero, new float3(5, 0, 0) }, 1f));
        }
    }
}
