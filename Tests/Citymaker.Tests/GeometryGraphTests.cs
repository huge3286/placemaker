// Network graph algorithms and cursor-aware block picking (owner: geometry).
// TraceBoundary / FindShortestPath cases adapted from Grid Road Generator's NetworkGraphAlgorithmsTests.

using System.Collections.Generic;
using System.Linq;
using Citymaker.Core.Geometry;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    public class GeometryGraphTests
    {
        private sealed class DictGraph : INetworkGraph<int>
        {
            private readonly Dictionary<int, float2> _pos = new Dictionary<int, float2>();
            private readonly Dictionary<int, List<int>> _adj = new Dictionary<int, List<int>>();
            private readonly Dictionary<(int, int), float> _cost = new Dictionary<(int, int), float>();

            public void AddNode(int id, float2 p) { _pos[id] = p; if (!_adj.ContainsKey(id)) _adj[id] = new List<int>(); }
            public void AddEdge(int a, int b) { _adj[a].Add(b); _adj[b].Add(a); }
            public void AddEdgeWithCost(int a, int b, float c) { AddEdge(a, b); _cost[(a, b)] = c; _cost[(b, a)] = c; }

            public IReadOnlyList<GraphEdge<int>> GetNeighbors(int node)
            {
                var r = new List<GraphEdge<int>>();
                foreach (int n in _adj[node])
                {
                    float2 d = _pos[n] - _pos[node];
                    float len = math.length(d);
                    r.Add(new GraphEdge<int>(n, _cost.TryGetValue((node, n), out float c) ? c : len, len > 1e-6f ? d / len : new float2(1, 0)));
                }
                return r;
            }
        }

        // ───────────── shortest path / boundary trace ─────────────

        [Fact]
        public void FindShortestPath_InsertsIntermediateNodesAndPrefersCheaperRoute()
        {
            var g = new DictGraph();
            g.AddNode(1, new float2(0, 0)); g.AddNode(2, new float2(50, 0)); g.AddNode(3, new float2(100, 0)); g.AddNode(4, new float2(50, 80));
            g.AddEdge(1, 2); g.AddEdge(2, 3); g.AddEdge(1, 4); g.AddEdge(4, 3);
            var path = new List<int>();
            Assert.True(NetworkGraphAlgorithms.FindShortestPath(g, 1, 3, path));
            Assert.Equal(new[] { 1, 2, 3 }, path);

            var w = new DictGraph();
            w.AddNode(1, new float2(0, 0)); w.AddNode(2, new float2(100, 0)); w.AddNode(3, new float2(50, 50));
            w.AddEdgeWithCost(1, 2, 500f); w.AddEdge(1, 3); w.AddEdge(3, 2);
            Assert.True(NetworkGraphAlgorithms.FindShortestPath(w, 1, 2, path));
            Assert.Equal(new[] { 1, 3, 2 }, path);
        }

        [Fact]
        public void FindShortestPath_DisconnectedOrSame()
        {
            var g = new DictGraph();
            g.AddNode(1, new float2(0, 0)); g.AddNode(2, new float2(10, 0));
            var path = new List<int>();
            Assert.False(NetworkGraphAlgorithms.FindShortestPath(g, 1, 2, path));
            Assert.Empty(path);
            Assert.True(NetworkGraphAlgorithms.FindShortestPath(g, 1, 1, path));
            Assert.Equal(new[] { 1 }, path);
        }

        [Fact]
        public void TraceBoundary_RectangleAndCleanFailures()
        {
            var g = new DictGraph();
            g.AddNode(1, new float2(0, 0)); g.AddNode(2, new float2(100, 0)); g.AddNode(3, new float2(100, 100)); g.AddNode(4, new float2(0, 100));
            g.AddEdge(1, 2); g.AddEdge(2, 3); g.AddEdge(3, 4); g.AddEdge(4, 1);
            var loop = new List<int>();
            Assert.True(NetworkGraphAlgorithms.TraceBoundary(g, 1, loop));
            Assert.Equal(4, loop.Count);
            Assert.Equal(1, loop[0]);

            var dead = new DictGraph();
            dead.AddNode(1, new float2(0, 0)); dead.AddNode(2, new float2(300, 0)); dead.AddNode(3, new float2(600, 0));
            dead.AddEdge(1, 2); dead.AddEdge(2, 3);
            Assert.False(NetworkGraphAlgorithms.TraceBoundary(dead, 1, loop));
            var lone = new DictGraph();
            lone.AddNode(1, float2.zero);
            Assert.False(NetworkGraphAlgorithms.TraceBoundary(lone, 1, loop));
            Assert.False(NetworkGraphAlgorithms.TraceBoundary(g, 1, loop, maxNodes: 2));
        }

        [Fact]
        public void TraceBoundary_IgnoresShortDeadEndSpur()
        {
            var g = new DictGraph();
            g.AddNode(1, new float2(0, 0)); g.AddNode(2, new float2(200, 0)); g.AddNode(3, new float2(200, 200)); g.AddNode(4, new float2(0, 200));
            g.AddNode(5, new float2(260, 30)); // short spur off node 2
            g.AddEdge(1, 2); g.AddEdge(2, 3); g.AddEdge(3, 4); g.AddEdge(4, 1); g.AddEdge(2, 5);
            var loop = new List<int>();
            Assert.True(NetworkGraphAlgorithms.TraceBoundary(g, 1, loop));
            Assert.DoesNotContain(5, loop);
            Assert.Equal(4, loop.Count);
        }

        // ───────────── planar graph & block picking ─────────────

        /// <summary>(n+1)×(n+1) node grid with spacing s; node id = i*100 + j, edge ids sequential.</summary>
        private static PlanarGraph Grid(int n, float s, float2 origin = default)
        {
            var g = new PlanarGraph();
            for (int i = 0; i <= n; i++) for (int j = 0; j <= n; j++) g.AddNode(i * 100 + j, origin + new float2(i * s, j * s));
            long e = 1;
            for (int i = 0; i <= n; i++)
                for (int j = 0; j <= n; j++)
                {
                    if (i < n) g.AddEdge(e++, i * 100 + j, (i + 1) * 100 + j);
                    if (j < n) g.AddEdge(e++, i * 100 + j, i * 100 + j + 1);
                }
            return g;
        }

        [Fact]
        public void PickFace_GridBlocks()
        {
            var g = Grid(3, 100);
            var f = g.PickFace(new float2(150, 150));
            Assert.True(f.Ok, f.Status.ToString());
            Assert.Equal(10000f, f.Area, 1);
            Assert.Equal(4, f.NodeIds.Count);
            Assert.Equal(4, f.EdgeIds.Count);
            Assert.True(f.Ring.All(p => p.x >= 100 && p.x <= 200 && p.y >= 100 && p.y <= 200));
            G.AssertSimpleCcw(f.Ring);
            var corner = g.PickFace(new float2(10, 290));
            Assert.True(corner.Ok);
            Assert.Contains(2L, corner.NodeIds.Select(x => x % 100)); // node at j = 2
            Assert.True(corner.Ring.All(p => p.x <= 100 && p.y >= 200));
        }

        [Fact]
        public void PickFace_XJunction_ChoosesTheQuadrantUnderTheCursor()
        {
            var g = Grid(2, 100); // node 101 is the centre X junction
            var seen = new HashSet<string>();
            foreach (var p in new[] { new float2(50, 50), new float2(150, 50), new float2(50, 150), new float2(150, 150) })
            {
                var f = g.PickFace(p);
                Assert.True(f.Ok);
                Assert.Contains(101L, f.NodeIds);
                Assert.True(Poly.Contains(f.Ring, p));
                seen.Add(string.Join(",", f.NodeIds.OrderBy(x => x)));
            }
            Assert.Equal(4, seen.Count);
        }

        [Fact]
        public void PickFace_FailsCleanly()
        {
            Assert.Equal(FacePickStatus.EmptyGraph, new PlanarGraph().PickFace(float2.zero).Status);
            var g = Grid(2, 100);
            Assert.Equal(FacePickStatus.OutsideNetwork, g.PickFace(new float2(-50, 100)).Status);
            Assert.Equal(FacePickStatus.NoRoadNearby, g.PickFace(new float2(-5000, -5000), 1000).Status);
            Assert.Equal(FacePickStatus.TooLarge, g.PickFace(new float2(50, 50), maxBoundaryEdges: 3).Status);
            Assert.Equal(FacePickStatus.NoRoadNearby, g.PickFace(new float2(float.NaN, 0)).Status);
            // Open street (no loop): the point is not enclosed.
            var line = new PlanarGraph();
            line.AddNode(1, new float2(0, 0)); line.AddNode(2, new float2(100, 0)); line.AddEdge(1, 1, 2);
            Assert.Equal(FacePickStatus.OutsideNetwork, line.PickFace(new float2(50, 20)).Status);
            Assert.False(line.AddEdge(1, 1, 2));    // duplicate id
            Assert.False(line.AddEdge(2, 1, 1));    // self loop
            Assert.False(line.AddEdge(3, 1, 99));   // unknown node
        }

        [Fact]
        public void PickFace_DeadEndInsideBlock_IsStripped()
        {
            var g = Grid(1, 100);
            g.AddNode(50, new float2(50, 40));
            g.AddEdge(900, 1, 50); // node 1 = (0,100)? ids: i*100+j → node 1 is (0,100)
            var f = g.PickFace(new float2(70, 70));
            Assert.True(f.Ok);
            Assert.Equal(4, f.EdgeIds.Count);
            Assert.Contains(900L, f.InteriorEdgeIds);
            Assert.Equal(10000f, f.Area, 1);
            var keep = g.PickFace(new float2(70, 70), stripDeadEnds: false);
            Assert.True(keep.Ok);
            Assert.Equal(6, keep.EdgeIds.Count);
        }

        [Fact]
        public void PickFace_InnerLoopBecomesHole_AndCanBePickedItself()
        {
            var g = Grid(1, 200);
            long id = 1000;
            var inner = G.Square(40, new float2(80, 80));
            for (int k = 0; k < 4; k++) g.AddNode(500 + k, inner[k]);
            for (int k = 0; k < 4; k++) g.AddEdge(id++, 500 + k, 500 + (k + 1) % 4);
            var block = g.PickFace(new float2(30, 100)); // ray towards +x hits the inner loop first
            Assert.True(block.Ok, block.Status.ToString());
            Assert.Equal(40000f, block.Area, 1);
            Assert.Single(block.Holes);
            Assert.Equal(1600f, Poly.Area(block.Holes[0]), 1);
            Assert.Equal(40000f - 1600f, block.ToRegion().Area, 1);
            var innerFace = g.PickFace(new float2(100, 100));
            Assert.True(innerFace.Ok);
            Assert.Equal(1600f, innerFace.Area, 1);
        }

        [Fact]
        public void PickFace_CurvedEdgeGeometryIsFollowed()
        {
            var g = new PlanarGraph();
            g.AddNode(1, new float2(0, 0)); g.AddNode(2, new float2(100, 0)); g.AddNode(3, new float2(100, 100)); g.AddNode(4, new float2(0, 100));
            g.AddEdge(1, 1, 2); g.AddEdge(2, 2, 3); g.AddEdge(4, 4, 1);
            // Top edge bulges outwards: half circle of radius 50 sampled from node 3 to node 4.
            var arc = new List<float2>();
            for (int k = 0; k <= 32; k++) { float a = math.PI * k / 32; arc.Add(new float2(50 + 50 * math.cos(a), 100 + 50 * math.sin(a))); }
            Assert.True(g.AddEdge(3, 3, 4, arc));
            var f = g.PickFace(new float2(50, 120));
            Assert.True(f.Ok);
            Assert.InRange(f.Area, 10000 + math.PI * 1250 - 20, 10000 + math.PI * 1250 + 1);
            Assert.True(f.Ring.Length > 30);
        }

        [Fact]
        public void PickFace_BlocksTouchingAtOneNode()
        {
            // Two triangles sharing only node 0 (bow tie of roads).
            var g = new PlanarGraph();
            g.AddNode(0, new float2(0, 0));
            g.AddNode(1, new float2(100, -50)); g.AddNode(2, new float2(100, 50));
            g.AddNode(3, new float2(-100, 50)); g.AddNode(4, new float2(-100, -50));
            g.AddEdge(1, 0, 1); g.AddEdge(2, 1, 2); g.AddEdge(3, 2, 0);
            g.AddEdge(4, 0, 3); g.AddEdge(5, 3, 4); g.AddEdge(6, 4, 0);
            var f = g.PickFace(new float2(70, 0));
            Assert.True(f.Ok);
            Assert.Equal(5000f, f.Area, 1);
            Assert.Equal(3, f.NodeIds.Count);
        }

        [Fact]
        public void PickFace_WorldCoordinatesAndLargeGraph_IsFast()
        {
            var g = Grid(30, 80, new float2(-3000, 1500));
            var p = new float2(-3000 + 80 * 15.5f, 1500 + 80 * 7.5f);
            double ms = G.BestMs(() => g.PickFace(p));
            var f = g.PickFace(p);
            Assert.True(f.Ok);
            Assert.Equal(6400f, f.Area, 1);
            Assert.True(ms < TimingBudget.Ms(20), $"{ms:F1} ms for {g.EdgeCount} edges");
        }

        [Fact]
        public void PlanarGraph_DrivesDijkstraAndTrace()
        {
            var g = Grid(3, 100);
            var path = new List<long>();
            Assert.True(NetworkGraphAlgorithms.FindShortestPath(g, 0L, 303L, path));
            Assert.Equal(7, path.Count);
            var loop = new List<long>();
            Assert.True(NetworkGraphAlgorithms.TraceBoundary(g, 0L, loop, 100));
            Assert.True(loop.Count >= 4);
        }
    }
}
