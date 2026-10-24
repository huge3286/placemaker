// CS2 surface acceptance model, rescue, hole cutting, triangulation (owner: geometry).

using System.Collections.Generic;
using System.Linq;
using Citymaker.Core.Geometry;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    public class GeometrySurfaceTests
    {
        private static readonly float2 World = new float2(-1534.37f, 2211.81f);

        private static void AssertAllAccepted(IEnumerable<float2[]> rings)
        {
            foreach (var r in rings)
            {
                Assert.True(SurfaceAcceptance.IsAccepted(r), $"returned a ring CS2 would drop ({SurfaceAcceptance.Diagnose(r)}): {G.Dump(r)}");
                Assert.True(Poly.SignedArea(r) > 0);
            }
        }

        // ───────────── the model ─────────────

        [Fact]
        public void GameInsetRing_ShrinksBy10cm()
        {
            var inset = SurfaceAcceptance.GameInsetRing(G.Square(10));
            Assert.Equal(9.8f * 9.8f, Poly.Area(inset), 3);
            Assert.Equal(new float2(0.1f, 0.1f), inset[0]);
            // Reflex corner of an L: the miter point lies on both shifted edges.
            var l = SurfaceAcceptance.GameInsetRing(G.L());
            Assert.True(math.distance(l[3], new float2(49.9f, 49.9f)) < 1e-4f);
        }

        [Fact]
        public void GameTriangulate_ConvexAndReflexRings()
        {
            var circle = G.Circle(50, 500, World);
            var tris = new List<int3>();
            Assert.Equal(498, EarClip.GameTriangulate(circle, tris));
            Assert.Equal(498, tris.Count);
            Assert.Equal(4, EarClip.GameTriangulate(G.L()));
            Assert.Equal(G.Comb(5).Length - 2, EarClip.GameTriangulate(G.Comb(5)));
            // Clockwise input has no ears in the game's test: dropped.
            Assert.Equal(0, EarClip.GameTriangulate(Poly.Reversed(G.Square(10))));
        }

        [Fact]
        public void IsAccepted_OrdinaryShapes()
        {
            Assert.True(SurfaceAcceptance.IsAccepted(G.Square(10)));
            Assert.True(SurfaceAcceptance.IsAccepted(G.Square(10, World)));
            Assert.True(SurfaceAcceptance.IsAccepted(Poly.Reversed(G.Square(10)))); // CCW-able
            Assert.True(SurfaceAcceptance.IsAccepted(G.L(100, 50, World)));
            Assert.True(SurfaceAcceptance.IsAccepted(G.U(100, 80, 20, 20, World)));
            Assert.True(SurfaceAcceptance.IsAccepted(G.Circle(30, 120, World)));
            Assert.True(SurfaceAcceptance.IsAccepted(G.Rect(-2500, -2500, 2500, 2500)));
            Assert.True(SurfaceAcceptance.IsAccepted(G.Square(1f, World)));
            var r3 = G.Square(10).Select(p => new float3(p.x, 5, p.y)).ToArray();
            Assert.True(SurfaceAcceptance.IsAccepted(r3));
        }

        [Fact]
        public void IsAccepted_RejectsWhatCs2Drops()
        {
            Assert.Equal(SurfaceVerdict.TooFewPoints, SurfaceAcceptance.Diagnose(new[] { new float2(0, 0), new float2(1, 0) }));
            Assert.Equal(SurfaceVerdict.NotFinite, SurfaceAcceptance.Diagnose(new[] { new float2(0, 0), new float2(float.NaN, 0), new float2(0, 1) }));
            Assert.Equal(SurfaceVerdict.ShortEdge, SurfaceAcceptance.Diagnose(new[] { new float2(0, 0), new float2(10, 0), new float2(10, 10), new float2(9.86f, 10) , new float2(0, 10) }));
            Assert.Equal(SurfaceVerdict.NotSimple, SurfaceAcceptance.Diagnose(new[] { new float2(0, 0), new float2(10, 10), new float2(10, 0), new float2(0, 10) }));
            // 15 cm wide strip: the 0.1 m inset from both sides crosses over.
            var strip = new[] { new float2(0, 0), new float2(50, 0), new float2(50.5f, 0.075f), new float2(50, 0.15f), new float2(0, 0.15f), new float2(-0.5f, 0.075f) };
            Assert.Equal(SurfaceVerdict.InsetCollapses, SurfaceAcceptance.Diagnose(strip));
            Assert.Equal(SurfaceVerdict.InsetCollapses, SurfaceAcceptance.Diagnose(G.Dumbbell(20, 5, 0.15f, World)));
            // Small triangle (inradius 9 cm): the 0.1 m inset flips it.
            Assert.False(SurfaceAcceptance.IsAccepted(new[] { new float2(0, 0), new float2(0.32f, 0), new float2(0.16f, 0.28f) }));
            // Hairline: 2A/P below 5 cm.
            Assert.Equal(SurfaceVerdict.Hairline, SurfaceAcceptance.Diagnose(new[] { new float2(0, 0), new float2(100, 0), new float2(50, 0.05f) }));
            // Needle: a long spike at a sharp angle — its miter runs metres out.
            var needle = new[] { new float2(0, 0), new float2(20, 0), new float2(20, 10), new float2(10.05f, 10), new float2(10, 60), new float2(9.95f, 10), new float2(0, 10) };
            Assert.False(SurfaceAcceptance.IsAccepted(needle));
        }

        [Fact]
        public void GameWouldKeep_IgnoresOurMargins()
        {
            // 0.2 m edge: our policy refuses (< 0.4 m), the plain model keeps it.
            var ring = new[] { new float2(0, 0), new float2(10, 0), new float2(10, 10), new float2(9.8f, 10), new float2(0, 10) };
            Assert.False(SurfaceAcceptance.IsAccepted(ring));
            Assert.True(SurfaceAcceptance.GameWouldKeep(ring));
        }

        // ───────────── rescue ─────────────

        [Fact]
        public void MakeAcceptable_AcceptedRingComesBackUnchanged()
        {
            var sq = G.Square(10, World);
            var r = SurfaceAcceptance.MakeAcceptable2D(sq, out float lost);
            Assert.Single(r);
            Assert.Equal(0f, lost, 3);
            Assert.Equal(Poly.Area(sq), Poly.Area(r[0]), 2);
        }

        [Fact]
        public void MakeAcceptable_LiftsWithHeightSampler()
        {
            var r = SurfaceAcceptance.MakeAcceptable(G.Square(10), p => p.x * 0.1f);
            Assert.Single(r);
            Assert.All(r[0], p => Assert.Equal(p.x * 0.1f, p.y, 4));
            var flat = SurfaceAcceptance.MakeAcceptable(G.Square(10), null);
            Assert.All(flat[0], p => Assert.Equal(0f, p.y));
        }

        [Fact]
        public void MakeAcceptable_CleansSpikesShortEdgesAndCollinear()
        {
            var ring = new[]
            {
                new float2(0, 0), new float2(0.1f, 0), new float2(10, 0), new float2(20, 0), new float2(20, 10),
                new float2(10.01f, 10), new float2(10, 40), new float2(9.99f, 10), new float2(0, 10), new float2(0, 9.9f),
            };
            var r = SurfaceAcceptance.MakeAcceptable2D(G.Offset(ring, World), out float lost);
            Assert.Single(r);
            AssertAllAccepted(r);
            Assert.Equal(200f, Poly.Area(r[0]), 0);
            Assert.True(lost < 1f);
        }

        [Fact]
        public void MakeAcceptable_NeckSplitsIntoLobes()
        {
            var db = G.Dumbbell(20, 5, 0.15f, World);
            Assert.False(SurfaceAcceptance.IsAccepted(db));
            var r = SurfaceAcceptance.MakeAcceptable2D(db, out float lost);
            AssertAllAccepted(r);
            Assert.True(r.Count >= 2);
            Assert.True(G.TotalArea(r) >= 2 * 400f - 1f, $"kept {G.TotalArea(r)}");
            Assert.True(lost <= 0.15f * 5 + 1f);
        }

        [Fact]
        public void MakeAcceptable_HairlineAndThinStripVanish()
        {
            Assert.Empty(SurfaceAcceptance.MakeAcceptable2D(G.Rect(0, 0, 100, 0.03f)));
            Assert.Empty(SurfaceAcceptance.MakeAcceptable2D(G.Rect(0, 0, 50, 0.15f)));
            Assert.Empty(SurfaceAcceptance.MakeAcceptable2D(new float2[0]));
            Assert.Empty(SurfaceAcceptance.MakeAcceptable2D((IReadOnlyList<float2>)null));
        }

        [Fact]
        public void MakeAcceptable_SelfIntersectingRingGivesBothLobes()
        {
            var bow = G.Offset(new[] { new float2(0, 0), new float2(20, 20), new float2(20, 0), new float2(0, 20) }, World);
            var r = SurfaceAcceptance.MakeAcceptable2D(bow, out _);
            Assert.Equal(2, r.Count);
            AssertAllAccepted(r);
            Assert.Equal(200f, G.TotalArea(r), 0);
        }

        [Fact]
        public void MakeAcceptable_RegionWithHoles_HoleFreePieces()
        {
            var reg = Poly.Difference(G.Square(60, World), G.Square(20, World + new float2(20, 20)))[0];
            var r = SurfaceAcceptance.MakeAcceptable2D(reg, out float lost);
            Assert.True(r.Count >= 2);
            AssertAllAccepted(r);
            Assert.Equal(3200f, G.TotalArea(r), 0);
            Assert.True(lost < 1f);
            var cut = SurfaceAcceptance.CutHoles(G.Square(60, World), new[] { G.Square(20, World + new float2(20, 20)) });
            AssertAllAccepted(cut);
            Assert.Equal(3200f, G.TotalArea(cut), 0);
            var corridor = SurfaceAcceptance.CutHoles(G.Square(60, World), new[] { G.Square(20, World + new float2(20, 20)) }, 1f);
            AssertAllAccepted(corridor);
            Assert.InRange(G.TotalArea(corridor), 3150f, 3200f);
        }

        [Fact]
        public void MakeAcceptable_HoleTouchingTheOuterRing()
        {
            // Hole sharing a corner with the outer boundary: weakly simple input.
            var reg = new Region(G.Square(40, World), new[] { Poly.Reversed(G.Square(10, World)) });
            var r = SurfaceAcceptance.MakeAcceptable2D(reg, out _);
            AssertAllAccepted(r);
            Assert.Equal(1500f, G.TotalArea(r), 0);
        }

        [Fact]
        public void MakeAcceptable_RandomMessyRings_NeverReturnRejected()
        {
            var rng = new G.Rng(42);
            for (int k = 0; k < 60; k++)
            {
                int n = 4 + (int)(rng.Next() * 40);
                var pts = new float2[n];
                float scale = k % 3 == 0 ? 2f : k % 3 == 1 ? 30f : 400f;
                for (int i = 0; i < n; i++)
                {
                    float a = 2 * math.PI * i / n + rng.Range(-0.3f, 0.3f), rr = scale * rng.Range(0.05f, 1f);
                    pts[i] = World + rr * new float2(math.cos(a), math.sin(a));
                }
                // Inject spikes, duplicates and crossings.
                if (k % 4 == 0) pts[n / 2] = pts[n / 2 - 1] + new float2(0.01f, 0);
                if (k % 5 == 0) { var t = pts[1]; pts[1] = pts[n - 2]; pts[n - 2] = t; }
                var r = SurfaceAcceptance.MakeAcceptable2D(pts, out float lost);
                AssertAllAccepted(r);
                Assert.True(lost >= 0);
            }
        }

        [Fact]
        public void MakeAcceptable_RealFreeAreaFixture()
        {
            // Ring drawn in game (Grid Road Generator fixture, world coordinates around x = -2500).
            var ring = G.LoadRing("free-area-ring.txt");
            Assert.True(SurfaceAcceptance.IsAccepted(Poly.Clean(ring, SurfaceAcceptance.MinEdge)));
            var r = SurfaceAcceptance.MakeAcceptable2D(ring, out float lost);
            Assert.Single(r);
            Assert.True(lost < 1f);
            var back = G.LoadRing("real-perimeter-backtrack.txt");
            var rb = SurfaceAcceptance.MakeAcceptable2D(back, out _);
            Assert.NotEmpty(rb);
            AssertAllAccepted(rb);
            Assert.True(G.TotalArea(rb) > 0.95f * Poly.Area(Poly.Clean(back)));
        }

        [Fact]
        public void Acceptance_500Vertices_Under50ms()
        {
            var blob = G.Blob(150, 500, 0.3f, 9, World);
            Assert.True(SurfaceAcceptance.IsAccepted(blob));
            double ms = G.BestMs(() => SurfaceAcceptance.IsAccepted(blob));
            Assert.True(ms < TimingBudget.Ms(50), $"IsAccepted {ms:F1} ms");
            double ms2 = G.BestMs(() => SurfaceAcceptance.MakeAcceptable2D(blob));
            Assert.True(ms2 < 50, $"MakeAcceptable {ms2:F1} ms");
            // Rejected 500-vertex ring (a needle injected) is rescued within budget too.
            var bad = (float2[])blob.Clone();
            bad[250] = bad[250] * 1.0f + math.normalize(bad[250] - World) * 40f;
            double ms3 = G.BestMs(() => SurfaceAcceptance.MakeAcceptable2D(bad), 1, 3);
            Assert.True(ms3 < 250, $"rescue {ms3:F1} ms");
            AssertAllAccepted(SurfaceAcceptance.MakeAcceptable2D(bad));
        }

        // ───────────── robust triangulation ─────────────

        [Fact]
        public void EarClip_Triangulate_CoversAreaForHardShapes()
        {
            foreach (var ring in new[] { G.Comb(30), G.Star(50, 10, 20), G.U(), G.Dumbbell(), Poly.Reversed(G.L()), G.Blob(100, 500, 0.4f, 13, World) })
            {
                Assert.True(EarClip.Triangulate(ring, out var tris));
                Assert.Equal(ring.Length - 2, tris.Count);
                double sum = tris.Sum(t => (double)Poly.SignedArea(new[] { ring[t.x], ring[t.y], ring[t.z] }));
                Assert.Equal(Poly.Area(ring), (float)sum, 0);
                Assert.All(tris, t => Assert.True(Poly.SignedArea(new[] { ring[t.x], ring[t.y], ring[t.z] }) >= 0));
            }
        }

        [Fact]
        public void EarClip_CollinearAndDuplicateVertices()
        {
            var ring = new[] { new float2(0, 0), new float2(5, 0), new float2(10, 0), new float2(10, 5), new float2(10, 10), new float2(0, 10), new float2(0, 5) };
            Assert.True(EarClip.Triangulate(ring, out var tris));
            Assert.Equal(5, tris.Count);
            Assert.False(EarClip.Triangulate(new[] { new float2(0, 0), new float2(1, 1), new float2(2, 2) }, out _));
        }

        [Fact]
        public void EarClip_RegionWithHoles()
        {
            var reg = Poly.Difference(G.Square(30), G.Square(10, new float2(10, 10)))[0];
            Assert.True(EarClip.Triangulate(reg, out var verts, out var tris));
            double sum = tris.Sum(t => (double)Poly.Area(new[] { verts[t.x], verts[t.y], verts[t.z] }));
            Assert.Equal(800.0, sum, 1);
        }
    }
}
