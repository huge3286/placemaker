using System.Collections.Generic;
using Citymaker.Core.Geometry;
using Citymaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    public class ToolRoadGraphTests
    {
        /// <summary>Grid of n×n nodes spaced 100 m, node id = x + 10*z + 1, edge ids unique.</summary>
        private sealed class GridGraph : IRoadGraph
        {
            public readonly Dictionary<long, float2> Pos = new Dictionary<long, float2>();
            private readonly Dictionary<long, List<RoadLink>> _links = new Dictionary<long, List<RoadLink>>();
            private long _nextEdge = 1000;
            public readonly Dictionary<(long, long), long> EdgeOf = new Dictionary<(long, long), long>();

            public GridGraph(int n)
            {
                for (int z = 0; z < n; z++)
                    for (int x = 0; x < n; x++)
                        Pos[Id(x, z)] = new float2(x * 100, z * 100);
                for (int z = 0; z < n; z++)
                    for (int x = 0; x < n; x++)
                    {
                        if (x + 1 < n) Connect(Id(x, z), Id(x + 1, z));
                        if (z + 1 < n) Connect(Id(x, z), Id(x, z + 1));
                    }
            }

            public static long Id(int x, int z) => x + 10 * z + 1;

            private void Connect(long a, long b)
            {
                long e = _nextEdge++;
                Add(a, b, e);
                Add(b, a, e);
                EdgeOf[(a, b)] = e; EdgeOf[(b, a)] = e;
            }

            private void Add(long from, long to, long e)
            {
                if (!_links.TryGetValue(from, out var l)) _links[from] = l = new List<RoadLink>();
                var d = Pos[to] - Pos[from];
                l.Add(new RoadLink { Edge = e, Other = to, Direction = math.normalize(d), Length = math.length(d) });
            }

            public IReadOnlyList<RoadLink> Links(long node) => _links.TryGetValue(node, out var l) ? l : new List<RoadLink>();

            /// <summary>CCW loop around grid cell (x, z) — what PlanarGraph.PickFace hands the tool.</summary>
            public List<HalfEdge> Cell(int x, int z)
            {
                var c = new[] { Id(x, z), Id(x + 1, z), Id(x + 1, z + 1), Id(x, z + 1) };
                var loop = new List<HalfEdge>();
                for (int i = 0; i < 4; i++) loop.Add(new HalfEdge(EdgeOf[(c[i], c[(i + 1) % 4])], c[i], c[(i + 1) % 4]));
                return loop;
            }

            public float2[] Ring(List<HalfEdge> loop)
            {
                var r = new float2[loop.Count];
                for (int i = 0; i < loop.Count; i++) r[i] = Pos[loop[i].From];
                return r;
            }
        }

        [Fact]
        public void ShortestPathFollowsGrid()
        {
            var g = new GridGraph(4);
            var path = new List<HalfEdge>();
            Assert.True(RoadGraphAlgorithms.ShortestPath(g, GridGraph.Id(0, 0), GridGraph.Id(3, 2), 1000, path));
            Assert.Equal(5, path.Count);
            Assert.Equal(GridGraph.Id(0, 0), path[0].From);
            Assert.Equal(GridGraph.Id(3, 2), path[path.Count - 1].To);
            for (int i = 1; i < path.Count; i++) Assert.Equal(path[i - 1].To, path[i].From);
            Assert.False(RoadGraphAlgorithms.ShortestPath(g, GridGraph.Id(0, 0), 12345, 1000, path));
            Assert.True(RoadGraphAlgorithms.ShortestPath(g, GridGraph.Id(1, 1), GridGraph.Id(1, 1), 1000, path));
            Assert.Empty(path);
        }

        [Fact]
        public void UnionOfAdjacentBlocksIsOneCycle()
        {
            var g = new GridGraph(3);
            var f1 = g.Cell(0, 0);
            var f2 = g.Cell(1, 0);
            Assert.True(RoadGraphAlgorithms.SharesEdge(f1, f2));
            var cycle = new List<HalfEdge>();
            Assert.True(RoadGraphAlgorithms.UnionBoundary(new[] { f1, f2 }, cycle));
            Assert.Equal(6, cycle.Count);
            var ring = g.Ring(cycle);
            Assert.Equal(20000f, Poly.Area(ring), 1);
            Assert.True(Poly.SignedArea(ring) > 0);
        }

        [Fact]
        public void UnionTouchingAtCornerOnlyIsRefused()
        {
            var g = new GridGraph(3);
            var f1 = g.Cell(0, 0);
            var f2 = g.Cell(1, 1);
            Assert.False(RoadGraphAlgorithms.SharesEdge(f1, f2));
            Assert.False(RoadGraphAlgorithms.UnionBoundary(new[] { f1, f2 }, new List<HalfEdge>()));
        }

        [Fact]
        public void UnionEnclosingAHoleIsRefused()
        {
            var g = new GridGraph(4);
            var faces = new List<List<HalfEdge>>();
            for (int z = 0; z < 3; z++)
                for (int x = 0; x < 3; x++)
                    if (!(x == 1 && z == 1)) faces.Add(g.Cell(x, z));   // leave the centre block out
            Assert.False(RoadGraphAlgorithms.UnionBoundary(faces, new List<HalfEdge>()));
            faces.Add(g.Cell(1, 1));
            var cycle = new List<HalfEdge>();
            Assert.True(RoadGraphAlgorithms.UnionBoundary(faces, cycle));
            Assert.Equal(12, cycle.Count);
        }

        [Fact]
        public void StitchDropsDuplicateJoints()
        {
            var ring = RoadGraphAlgorithms.Stitch(new IReadOnlyList<float3>[]
            {
                new[] { new float3(0, 0, 0), new float3(10, 0, 0) },
                new[] { new float3(10, 0, 0), new float3(10, 0, 10) },
                new[] { new float3(10, 0, 10), new float3(0, 0, 0) },
            });
            Assert.Equal(3, ring.Count);
        }

        [Fact]
        public void HalfEdgeReversal()
        {
            var h = new HalfEdge(7, 1, 2);
            Assert.Equal(new HalfEdge(7, 2, 1), h.Reversed);
            Assert.Equal(h, h.Reversed.Reversed);
        }
    }
}
