using System;
using System.Collections.Generic;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Layers.Roads;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class RoadsSpatialPruningTests
    {
        // The exhaustive pre-optimization rule is the oracle: an index must not change
        // which road goes, including curves bowed outside their endpoint bounds.
        private static int Exhaustive(RoadNetwork net, Func<NetRole, float> width, List<ExistingRoad> existing)
        {
            int count = 0;
            for (int pass = 0; pass < 8; pass++)
            {
                bool any = false;
                for (int i = 0; i < net.Edges.Count && !any; i++)
                {
                    var e = net.Edges[i];
                    if (!RoadGraph.Car(e) || e.Role == NetRole.PerimeterRoad || e.Role == NetRole.Avenue || e.Role == NetRole.Roundabout) continue;
                    if (RoadGraph.Length(e) < 20f || RoadConnectivity.Attached(net.Nodes[e.N0]) || RoadConnectivity.Attached(net.Nodes[e.N1])) continue;
                    float Gap(float2 p, float2 tan)
                    {
                        float best = float.MaxValue;
                        for (int k = 0; k < net.Edges.Count; k++)
                        {
                            var o = net.Edges[k];
                            if (k == i || !RoadGraph.Car(o) || o.N0 == e.N0 || o.N0 == e.N1 || o.N1 == e.N0 || o.N1 == e.N1) continue;
                            float d = o.Curve.DistanceXZ(p, out float t);
                            if (t < 0.01f || t > 0.99f || math.abs(math.dot(o.Curve.TangentXZ(t), tan)) < 0.85f) continue;
                            best = math.min(best, d - width(o.Role));
                        }
                        foreach (var r in existing)
                        {
                            float d = r.Curve.DistanceXZ(p, out float t);
                            if (t < 0.01f || t > 0.99f || math.abs(math.dot(r.Curve.TangentXZ(t), tan)) < 0.85f) continue;
                            best = math.min(best, d - r.HalfWidth);
                        }
                        return best;
                    }
                    if (!RoadTopology.RunsAlongside(e.Curve, width(e.Role), Gap)) continue;
                    int before = RoadTopology.Parts(net);
                    e.Removed = true;
                    if (RoadTopology.Parts(net) > before) { e.Removed = false; continue; }
                    count++; any = true;
                }
                if (!any) break;
                net.Weld();
            }
            return count;
        }

        private static RoadNetwork Network(int seed)
        {
            var net = new RoadNetwork();
            // Negative/world-offset coordinates exercise grid cell boundaries as well.
            float3 P(float x, float z) => new float3(x - 180f + seed * 7.25f, 0f, z - 120f);
            void Line(float x, float z, float xx, float zz) => net.Edges.Add(new RoadEdge(RoadCurve.Straight(P(x, z), P(xx, zz)), NetRole.Street));
            for (int z = 0; z <= 3; z++)
                for (int x = 0; x < 4; x++) Line(x * 100, z * 100, (x + 1) * 100, z * 100);
            for (int x = 0; x <= 4; x++)
                for (int z = 0; z < 3; z++) Line(x * 100, z * 100, x * 100, (z + 1) * 100);
            float gap = 8f + seed * 2f;
            Line(100, 100, 100, 100 + gap);
            Line(100, 100 + gap, 200, 100 + gap);
            Line(200, 100 + gap, 200, 100);
            net.Edges.Add(new RoadEdge(new RoadCurve(P(0, 300), P(50, 130 + seed), P(180, 130 + seed), P(250, 300)), NetRole.Street));
            net.Weld();
            return net;
        }

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(3)] [InlineData(7)] [InlineData(13)]
        public void IndexedPruningMatchesExhaustiveCurvesAndWideExistingRoads(int seed)
        {
            var expected = Network(seed);
            var actual = Network(seed);
            var existing = new List<ExistingRoad>
            {
                new ExistingRoad { Curve = new RoadCurve(new float3(-400, 0, -90), new float3(-200, 0, 60), new float3(200, 0, 60), new float3(500, 0, -90)), HalfWidth = 30 + seed },
            };
            float Width(NetRole _) => 8f + seed % 3;
            int removed = Exhaustive(expected, Width, existing);
            Assert.Equal(removed, RoadTopology.GiveUpAlongside(actual, Width, existing));
            Assert.True(removed > 0);
            Assert.Equal(expected.Edges.Count, actual.Edges.Count);
            for (int i = 0; i < expected.Edges.Count; i++)
            {
                Assert.Equal(expected.Edges[i].Removed, actual.Edges[i].Removed);
                Assert.Equal(expected.Edges[i].Curve, actual.Edges[i].Curve);
                Assert.Equal(expected.Edges[i].N0, actual.Edges[i].N0);
                Assert.Equal(expected.Edges[i].N1, actual.Edges[i].N1);
            }
        }

        [Fact]
        public void AlongsidePruningObservesCancellationBeforeExpensiveWork()
        {
            var net = Network(0);
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();
            Assert.Throws<OperationCanceledException>(() => RoadTopology.GiveUpAlongside(net, _ => 8f, new List<ExistingRoad>(), cancel.Token));
        }

        [Fact]
        public void AlongsidePruningObservesCancellationRequestedDuringThePass()
        {
            var net = Network(0);
            using var cancel = new CancellationTokenSource();
            float Width(NetRole _) { cancel.Cancel(); return 8f; }
            Assert.Throws<OperationCanceledException>(() => RoadTopology.GiveUpAlongside(net, Width, new List<ExistingRoad>(), cancel.Token));
        }

        [Fact]
        public void LaneCandidateSearchObservesCancellation()
        {
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();
            Assert.Throws<OperationCanceledException>(() => RoadWalkways.Lanes(Network(0), new RoadWalkways.LaneOptions
            {
                Density = 1f, Cancel = cancel.Token,
            }));
        }
    }
}
