// Citymaker — read-only view of the game's road network for block / perimeter selection.
//
// Adapter from ECS to Core's IRoadGraph (pure face tracing / Dijkstra in Core/Tool/RoadGraph.cs),
// plus the geometry the selection needs: nearest road to the cursor (net search tree, never a
// scan over all nodes — GRG's overlay walked every node each frame), curb-side curves to turn a
// loop of road edges into the block's inner outline, and the attachments for the area.

using System;
using System.Collections.Generic;
using Colossal.Collections;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Citymaker.Core;
using Citymaker.Core.Geometry;
using Citymaker.Core.Tool;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Citymaker.Game.Selection
{
    public sealed class RoadNetwork : IRoadGraph
    {
        private readonly EntityManager _em;
        private readonly global::Game.Net.SearchSystem _search;
        private readonly Dictionary<long, List<RoadLink>> _linkCache = new Dictionary<long, List<RoadLink>>();
        private readonly Dictionary<Entity, bool> _boundaryCache = new Dictionary<Entity, bool>();

        public RoadNetwork(EntityManager em, global::Game.Net.SearchSystem search)
        {
            _em = em;
            _search = search;
        }

        /// <summary>Call once per frame (or after the network changed): link lists are cached per frame.</summary>
        public void BeginFrame()
        {
            _linkCache.Clear();
            _boundaryCache.Clear();
        }

        /// <summary>Ground-level road edges bound blocks; tunnels, bridges and building-internal nets do not (Road nodes).</summary>
        public bool IsBoundaryEdge(Entity e)
        {
            if (_boundaryCache.TryGetValue(e, out bool cached)) return cached;
            bool ok = KindOf(e) == FillWall.Roads;
            _boundaryCache[e] = ok;
            return ok;
        }

        private readonly Dictionary<Entity, FillWall> _prefabKind = new Dictionary<Entity, FillWall>();

        /// <summary>
        /// (s43) What a net edge is for the Paint bucket: Roads (every car road: alleys, gravel, one-way, pedestrian
        /// streets…), Paths, Bikes (bicycle-only), Tracks (train / tram / subway at ground level); None for anything
        /// else (pipes, power lines, fences, waterways, taxiways), elevated / tunnelled pieces, temp / deleted edges
        /// and nets inside a building's lot. Before s43 only edges with a Road component counted (paths, bike paths
        /// and tracks were invisible to the bucket) and every net owned by any object was dropped.
        /// </summary>
        public FillWall KindOf(Entity e)
        {
            if (e == Entity.Null || !_em.Exists(e)) return FillWall.None;
            if (!_em.HasComponent<Edge>(e) || !_em.HasComponent<Curve>(e)) return FillWall.None;
            if (_em.HasComponent<Temp>(e) || _em.HasComponent<Deleted>(e)) return FillWall.None;
            if (OwnedByBuilding(e)) return FillWall.None;
            if (_em.HasComponent<Composition>(e))
            {
                var comp = _em.GetComponentData<Composition>(e).m_Edge;
                if (comp != Entity.Null && _em.HasComponent<NetCompositionData>(comp))
                {
                    var flags = _em.GetComponentData<NetCompositionData>(comp).m_Flags.m_General;
                    if ((flags & (CompositionFlags.General.Elevated | CompositionFlags.General.Tunnel)) != 0) return FillWall.None;
                }
            }
            var prefab = _em.HasComponent<PrefabRef>(e) ? _em.GetComponentData<PrefabRef>(e).m_Prefab : Entity.Null;
            if (prefab != Entity.Null && _prefabKind.TryGetValue(prefab, out var known)) return known;
            FillWall kind = FillWall.None;
            if (_em.HasComponent<Road>(e)) kind = BicycleOnly(e, out _) ? FillWall.Bikes : FillWall.Roads;
            else if (_em.HasComponent<TrainTrack>(e) || _em.HasComponent<TramTrack>(e) || _em.HasComponent<SubwayTrack>(e)) kind = FillWall.Tracks;
            else if (prefab != Entity.Null && _em.HasComponent<PathwayData>(prefab)) kind = BicycleOnly(e, out _) ? FillWall.Bikes : FillWall.Paths;
            if (prefab != Entity.Null) _prefabKind[prefab] = kind;
            return kind;
        }

        /// <summary>Lanes say bicycles only: a bike lane and no car or pedestrian lane.</summary>
        private bool BicycleOnly(Entity edge, out bool hasBike)
        {
            hasBike = false;
            if (!_em.HasBuffer<global::Game.Net.SubLane>(edge)) return false;
            bool other = false;
            var lanes = _em.GetBuffer<global::Game.Net.SubLane>(edge, true);
            for (int i = 0; i < lanes.Length; i++)
            {
                var lane = lanes[i].m_SubLane;
                if (lane == Entity.Null || !_em.Exists(lane)) continue;
                if (_em.HasComponent<global::Game.Net.CarLane>(lane) || _em.HasComponent<global::Game.Net.TrackLane>(lane)) { other = true; continue; }
                if (_em.HasComponent<global::Game.Net.PedestrianLane>(lane))
                {
                    if ((_em.GetComponentData<global::Game.Net.PedestrianLane>(lane).m_Flags & global::Game.Net.PedestrianLaneFlags.AllowBicycle) != 0) hasBike = true;
                    other = true;
                    continue;
                }
                if (!_em.HasComponent<PrefabRef>(lane)) continue;
                var lp = _em.GetComponentData<PrefabRef>(lane).m_Prefab;
                if (lp != Entity.Null && _em.HasComponent<NetLaneData>(lp) && (_em.GetComponentData<NetLaneData>(lp).m_Flags & global::Game.Prefabs.LaneFlags.BicyclesOnly) != 0) hasBike = true;
            }
            return hasBike && !other;
        }

        private bool OwnedByBuilding(Entity e)
        {
            var cur = e;
            for (int depth = 0; depth < 4 && _em.HasComponent<Owner>(cur); depth++)
            {
                var o = _em.GetComponentData<Owner>(cur).m_Owner;
                if (o == Entity.Null || !_em.Exists(o)) return false;
                if (_em.HasComponent<global::Game.Buildings.Building>(o) || _em.HasComponent<global::Game.Buildings.Extension>(o)) return true;
                cur = o;
            }
            return false;
        }

        public IReadOnlyList<RoadLink> Links(long nodeId)
        {
            if (_linkCache.TryGetValue(nodeId, out var cached)) return cached;
            var list = new List<RoadLink>(4);
            _linkCache[nodeId] = list;
            var node = EntityIds.Resolve(_em, nodeId);
            if (node == Entity.Null || !_em.HasComponent<Node>(node) || !_em.HasBuffer<ConnectedEdge>(node)) return list;
            var edges = _em.GetBuffer<ConnectedEdge>(node, true);
            for (int i = 0; i < edges.Length; i++)
            {
                var e = edges[i].m_Edge;
                if (!IsBoundaryEdge(e)) continue;
                var edge = _em.GetComponentData<Edge>(e);
                bool atStart = edge.m_Start == node, atEnd = edge.m_End == node;
                if (!atStart && !atEnd) continue;            // node sits in the middle of this edge (side connection)
                if (atStart && atEnd) continue;              // degenerate loop edge
                var curve = _em.GetComponentData<Curve>(e);
                float2 dir = atStart
                    ? math.normalizesafe(MathUtils.StartTangent(curve.m_Bezier).xz)
                    : math.normalizesafe(-MathUtils.EndTangent(curve.m_Bezier).xz);
                if (math.lengthsq(dir) < 0.5f)
                    dir = math.normalizesafe((atStart ? curve.m_Bezier.d - curve.m_Bezier.a : curve.m_Bezier.a - curve.m_Bezier.d).xz, new float2(1, 0));
                list.Add(new RoadLink
                {
                    Edge = EntityIds.Pack(e),
                    Other = EntityIds.Pack(atStart ? edge.m_End : edge.m_Start),
                    Direction = dir,
                    Length = math.max(0.1f, curve.m_Length),
                });
            }
            return list;
        }

        // ───────────── spatial queries ─────────────

        private struct EdgeIterator : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
        {
            public Bounds2 Bounds;
            public NativeList<Entity> Results;
            public bool Intersect(QuadTreeBoundsXZ bounds) => MathUtils.Intersect(bounds.m_Bounds.xz, Bounds);
            public void Iterate(QuadTreeBoundsXZ bounds, Entity item)
            {
                if (MathUtils.Intersect(bounds.m_Bounds.xz, Bounds)) Results.Add(item);
            }
        }

        /// <summary>Net entities (edges and nodes) whose bounds touch the rectangle.</summary>
        public void Query(float2 min, float2 max, List<Entity> into)
        {
            into.Clear();
            if (_search == null) return;
            var tree = _search.GetNetSearchTree(true, out var deps);
            deps.Complete();
            var results = new NativeList<Entity>(32, Allocator.Temp);
            try
            {
                var it = new EdgeIterator { Bounds = new Bounds2(min, max), Results = results };
                tree.Iterate(ref it);
                for (int i = 0; i < results.Length; i++) into.Add(results[i]);
            }
            finally { results.Dispose(); }
        }

        private readonly List<Entity> _scratch = new List<Entity>();

        // ───────────── block picking (Core FillGraph / PlanarGraph) ─────────────

        private const float GraphRadius = 900f;
        private const float GraphRebuildDistance = 350f;
        private const float GraphMaxAge = 2f;
        private FillGraph _fill;
        private float2 _planarCenter;
        private float _planarBuilt = -100f;
        private FillWall _planarWalls = FillWall.None;
        private readonly List<float2> _geomScratch = new List<float2>();
        /// <summary>Real edges in the current graph with their wall kind (overlay: what the fill sees).</summary>
        private readonly Dictionary<Entity, FillWall> _graphEdges = new Dictionary<Entity, FillWall>();
        /// <summary>
        /// Sub-edges of split hosts ever built (id → geometry): a selection keeps its loop of half-edges while the
        /// graph is rebuilt around other places; the outline of a loop with sub-edges is built from these.
        /// </summary>
        private readonly Dictionary<long, FillCurve> _subEdges = new Dictionary<long, FillCurve>();
        private int _graphVersion;

        public FillGraph CurrentGraph => _fill;
        public int GraphVersion => _graphVersion;
        public IReadOnlyDictionary<Entity, FillWall> GraphEdges => _graphEdges;

        /// <summary>
        /// Planar graph of the walls around <paramref name="p"/>. Rebuilt when the cursor leaves the covered area,
        /// the walls change, or every couple of seconds (the network may have changed) — a bounded query, never the
        /// whole map. Side connections (a path ending on a road's sidewalk without a node) split their host edge.
        /// </summary>
        private FillGraph GraphAround(float2 p, float now, FillWall walls)
        {
            if (_fill != null && walls == _planarWalls && math.distance(p, _planarCenter) < GraphRebuildDistance && now - _planarBuilt < GraphMaxAge) return _fill;
            Query(p - GraphRadius, p + GraphRadius, _scratch);
            var seen = new HashSet<Entity>();
            var curves = new List<FillCurve>();
            var nodes = new Dictionary<long, float2>();
            var links = new List<FillSideLink>();
            _graphEdges.Clear();
            foreach (var e in _scratch)
            {
                if (!seen.Add(e)) continue;
                var kind = KindOf(e);
                if ((kind & walls) == 0) continue;
                var edge = _em.GetComponentData<Edge>(e);
                if (!_em.HasComponent<Node>(edge.m_Start) || !_em.HasComponent<Node>(edge.m_End)) continue;
                long a = EntityIds.Pack(edge.m_Start), b = EntityIds.Pack(edge.m_End);
                nodes[a] = _em.GetComponentData<Node>(edge.m_Start).m_Position.xz;
                nodes[b] = _em.GetComponentData<Node>(edge.m_End).m_Position.xz;
                _geomScratch.Clear();
                foreach (var q in SampleCurve(_em.GetComponentData<Curve>(e).m_Bezier, true, 6)) _geomScratch.Add(q.xz);
                long id = EntityIds.Pack(e);
                curves.Add(new FillCurve { Id = id, Host = id, A = a, B = b, Geom = _geomScratch.ToArray(), HalfWidth = RoadWidth(e) * 0.5f, Kind = kind });
                _graphEdges[e] = kind;
                if (_em.HasBuffer<ConnectedNode>(e))
                {
                    var cn = _em.GetBuffer<ConnectedNode>(e, true);
                    for (int i = 0; i < cn.Length; i++)
                    {
                        var n = cn[i].m_Node;
                        if (n == Entity.Null || !_em.HasComponent<Node>(n)) continue;
                        long nid = EntityIds.Pack(n);
                        nodes[nid] = _em.GetComponentData<Node>(n).m_Position.xz;
                        links.Add(new FillSideLink(id, nid));
                    }
                }
            }
            var g = FillGraph.Build(curves, nodes, links, walls);
            if (_subEdges.Count > 20000) _subEdges.Clear();
            foreach (var c in g.Edges) if (c.IsSub) _subEdges[c.Id] = c;
            _fill = g;
            _planarCenter = p;
            _planarBuilt = now;
            _planarWalls = walls;
            _graphVersion++;
            return g;
        }

        /// <summary>Forget the cached graph (after a build changed the roads).</summary>
        public void InvalidateGraph() => _fill = null;

        /// <summary>The block (roads only) containing <paramref name="p"/> — Block paint.</summary>
        public FacePickStatus PickBlock(float2 p, float now, List<HalfEdge> loop) => PickBlock(p, now, loop, FillWall.Roads);

        /// <summary>The face of the walls containing <paramref name="p"/> as a CCW loop of half-edges (face on the left).</summary>
        public FacePickStatus PickBlock(float2 p, float now, List<HalfEdge> loop, FillWall walls)
        {
            loop.Clear();
            walls &= FillWalls.Curves;
            if (walls == FillWall.None) return FacePickStatus.EmptyGraph;
            var g = GraphAround(p, now, walls);
            var pick = g.Pick(p, GraphRadius, 4000);
            if (!pick.Ok) return pick.Status;
            int n = pick.EdgeIds.Count;
            for (int i = 0; i < n; i++)
                loop.Add(new HalfEdge(pick.EdgeIds[i], pick.NodeIds[i], pick.NodeIds[(i + 1) % n]));
            return loop.Count >= 2 ? FacePickStatus.Ok : FacePickStatus.Degenerate;
        }

        /// <summary>
        /// (s43) Outline of a face loop: the entity curbs (BuildArea) when every edge is a real net; otherwise (split
        /// sub-edges from side connections) the centre ring minus each wall's corridor at its own half-width, the
        /// piece under <paramref name="seed"/>. Attachments are left to the caller in the second case.
        /// </summary>
        public AreaShape BuildFaceArea(IReadOnlyList<HalfEdge> loop, float2 seed, AreaSource source, out bool needsAttachments)
        {
            needsAttachments = false;
            bool allReal = true;
            foreach (var h in loop) if (EntityIds.Resolve(_em, h.Edge) == Entity.Null) { allReal = false; break; }
            if (allReal)
            {
                var a = BuildArea(loop, source);
                if (a != null) return a;
            }
            var centre = new List<float2>();
            var heights = new List<float3>();
            foreach (var h in loop)
            {
                if (!TryGeometry(h.Edge, out var geom, out _, out long ga)) return null;
                bool fwd = ga == h.From;
                for (int k = 0; k < geom.Length - 1; k++)
                {
                    var q = fwd ? geom[k] : geom[geom.Length - 1 - k];
                    if (centre.Count == 0 || math.distancesq(centre[centre.Count - 1], q) > 1e-8f) centre.Add(q);
                }
                heights.Add(NodePosition(h.From));
            }
            if (centre.Count < 3) return null;
            var ring2 = Poly.EnsureCcw(centre.ToArray());
            float2[] outline = null;
            if (_fill != null)
            {
                bool covered = true;
                foreach (var h in loop) if (!_fill.TryEdge(h.Edge, out _)) { covered = false; break; }
                if (covered) outline = _fill.CurbOutline(ring2, seed);
            }
            if (outline == null)
            {
                var walls = new List<FillCurveGeom>();
                foreach (var h in loop) if (TryGeometry(h.Edge, out var g, out float w, out _)) walls.Add(new FillCurveGeom(g, w));
                outline = FillWalls.CurbOutline(ring2, walls, seed);
            }
            if (outline == null) return null;
            needsAttachments = true;
            return new AreaShape { Ring = Lift(outline, heights), Source = source };
        }

        /// <summary>Centre polyline (A→B), half-width and start node of a real edge or a stored sub-edge.</summary>
        private bool TryGeometry(long id, out float2[] geom, out float halfWidth, out long a)
        {
            geom = null; halfWidth = 0; a = 0;
            if (_fill != null && _fill.TryEdge(id, out var fe)) { geom = fe.Geom; halfWidth = fe.HalfWidth; a = fe.A; return true; }
            if (_subEdges.TryGetValue(id, out var se)) { geom = se.Geom; halfWidth = se.HalfWidth; a = se.A; return true; }
            var e = EntityIds.Resolve(_em, id);
            if (e == Entity.Null || !_em.HasComponent<Curve>(e) || !_em.HasComponent<Edge>(e)) return false;
            var pts = SampleCurve(_em.GetComponentData<Curve>(e).m_Bezier, true, 6);
            geom = new float2[pts.Count];
            for (int i = 0; i < pts.Count; i++) geom[i] = pts[i].xz;
            halfWidth = RoadWidth(e) * 0.5f;
            a = EntityIds.Pack(_em.GetComponentData<Edge>(e).m_Start);
            return true;
        }

        // ───────────── loop → area ─────────────

        public float3 NodePosition(long nodeId)
        {
            var n = EntityIds.Resolve(_em, nodeId);
            return n != Entity.Null && _em.HasComponent<Node>(n) ? _em.GetComponentData<Node>(n).m_Position : float3.zero;
        }

        public bool TryCurve(long edgeId, out Bezier4x3 curve, out Entity edge)
        {
            edge = EntityIds.Resolve(_em, edgeId);
            curve = default;
            if (edge == Entity.Null || !_em.HasComponent<Curve>(edge)) return false;
            curve = _em.GetComponentData<Curve>(edge).m_Bezier;
            return true;
        }

        /// <summary>Centerline polygon of a loop (node to node along the curves), for containment and orientation tests.</summary>
        public List<float3> CenterRing(IReadOnlyList<HalfEdge> loop, int samplesPerEdge = 6)
        {
            var pieces = new List<IReadOnlyList<float3>>(loop.Count);
            foreach (var h in loop)
            {
                if (!TryCurve(h.Edge, out var c, out var edge)) { pieces.Add(new[] { NodePosition(h.From), NodePosition(h.To) }); continue; }
                bool forward = _em.GetComponentData<Edge>(edge).m_Start == EntityIds.Unpack(h.From);
                pieces.Add(SampleCurve(c, forward, samplesPerEdge));
            }
            return RoadGraphAlgorithms.Stitch(pieces);
        }

        public float RoadWidth(Entity edge)
        {
            if (_em.HasComponent<Composition>(edge))
            {
                var comp = _em.GetComponentData<Composition>(edge).m_Edge;
                if (comp != Entity.Null && _em.HasComponent<NetCompositionData>(comp)) return _em.GetComponentData<NetCompositionData>(comp).m_Width;
            }
            return 8f;
        }

        /// <summary>
        /// The area enclosed by a CCW loop of road half-edges: the curb lines facing the inside
        /// (so a park never overlaps the road), joined straight across intersections. Falls back
        /// to the centerline polygon when curb geometry is missing or would self-intersect.
        /// Attachments: one RoadAlongEdge per road (at its middle) and one RoadNode per corner node.
        /// </summary>
        public AreaShape BuildArea(IReadOnlyList<HalfEdge> loop, AreaSource source)
        {
            var center = CenterRing(loop);
            var center2 = Poly.To2D(center);
            var curbPieces = new List<IReadOnlyList<float3>>(loop.Count);
            bool curbsOk = true;
            foreach (var h in loop)
            {
                // (s43) Road nodes closed across open land: no curb, the stitch joins the neighbouring curbs straight.
                if (RoadGraphAlgorithms.IsStraight(h)) continue;
                var piece = CurbPiece(h, center2);
                if (piece == null) { curbsOk = false; break; }
                curbPieces.Add(piece);
            }
            float3[] ring = null;
            if (curbsOk)
            {
                var curb = RoadGraphAlgorithms.Stitch(curbPieces, 0.3f);
                var clean = Poly.Clean(Poly.To2D(curb), 0.3f);
                if (clean.Length >= 3 && Poly.IsSimple(clean) && Poly.SignedArea(clean) > 1f)
                    ring = Lift(clean, curb);
            }
            if (ring == null)
            {
                var clean = Poly.Clean(center2, 0.3f);
                if (clean.Length < 3 || !Poly.IsSimple(clean)) return null;
                ring = Lift(clean, center);
            }

            var shape = new AreaShape { Ring = ring, Source = source };
            foreach (var h in loop)
            {
                if (!TryCurve(h.Edge, out var c, out var edge)) continue;
                if (KindOf(edge) == FillWall.Tracks) continue;   // s43: a block bounded by rail has no entrance on the tracks
                bool forward = _em.GetComponentData<Edge>(edge).m_Start == EntityIds.Unpack(h.From);
                var mid = MathUtils.Position(c, 0.5f);
                var tangent = math.normalizesafe(MathUtils.Tangent(c, 0.5f).xz) * (forward ? 1f : -1f);
                Composer.ClosestSegment(ring, mid.xz, out int seg, out float st, out _);
                var onRing = seg >= 0 ? math.lerp(ring[seg], ring[(seg + 1) % ring.Length], st) : mid;
                shape.Attachments.Add(new BoundaryAttachment
                {
                    Kind = AttachmentKind.RoadAlongEdge, ExternalId = h.Edge, ExternalT = 0.5f, Position = onRing,
                    Direction = new float3(tangent.x, 0, tangent.y), RoadWidth = RoadWidth(edge), RingSegment = seg, RingT = st,
                });
            }
            foreach (var h in loop)
            {
                var p = NodePosition(h.From);
                Composer.ClosestSegment(ring, p.xz, out int seg, out float st, out _);
                shape.Attachments.Add(new BoundaryAttachment
                {
                    Kind = AttachmentKind.RoadNode, ExternalId = h.From, Position = p, RingSegment = seg, RingT = st,
                });
            }
            return shape;
        }

        private IReadOnlyList<float3> CurbPiece(HalfEdge h, float2[] inside)
        {
            var edgeEntity = EntityIds.Resolve(_em, h.Edge);
            if (edgeEntity == Entity.Null || !_em.HasComponent<EdgeGeometry>(edgeEntity)) return null;
            var g = _em.GetComponentData<EdgeGeometry>(edgeEntity);
            bool forward = _em.GetComponentData<Edge>(edgeEntity).m_Start == EntityIds.Unpack(h.From);
            // Decide the side by geometry instead of trusting left/right naming: the curb whose
            // middle lies inside the loop's centerline polygon faces the block.
            var leftMid = MathUtils.Position(g.m_Start.m_Left, 1f);
            var rightMid = MathUtils.Position(g.m_Start.m_Right, 1f);
            bool leftInside = Poly.Contains(inside, leftMid.xz);
            bool rightInside = Poly.Contains(inside, rightMid.xz);
            bool useLeft;
            if (leftInside != rightInside) useLeft = leftInside;
            else
            {
                // Degenerate (very thin block): fall back to the travel-direction rule.
                var c = _em.GetComponentData<Curve>(edgeEntity).m_Bezier;
                var t = MathUtils.Tangent(c, 0.5f).xz * (forward ? 1f : -1f);
                var toLeft = leftMid.xz - MathUtils.Position(c, 0.5f).xz;
                useLeft = (t.x * toLeft.y - t.y * toLeft.x) > 0f;
            }
            var a = useLeft ? g.m_Start.m_Left : g.m_Start.m_Right;
            var b = useLeft ? g.m_End.m_Left : g.m_End.m_Right;
            var pts = new List<float3>();
            pts.AddRange(SampleCurve(a, true, 6));
            pts.AddRange(SampleCurve(b, true, 6));
            if (!forward) pts.Reverse();
            return pts;
        }

        public static List<float3> SampleCurve(Bezier4x3 c, bool forward, int steps)
        {
            var pts = new List<float3>(steps + 1);
            float chord = math.distance(c.a.xz, c.d.xz);
            float hull = math.distance(c.a.xz, c.b.xz) + math.distance(c.b.xz, c.c.xz) + math.distance(c.c.xz, c.d.xz);
            // Straight segments need only their ends.
            int n = hull - chord < 0.05f ? 1 : math.max(1, steps);
            for (int i = 0; i <= n; i++)
            {
                float t = forward ? (float)i / n : 1f - (float)i / n;
                pts.Add(MathUtils.Position(c, t));
            }
            return pts;
        }

        /// <summary>Heights for cleaned 2D points: nearest source sample's height.</summary>
        private static float3[] Lift(float2[] clean, List<float3> source)
        {
            var ccw = Poly.EnsureCcw(clean);
            var r = new float3[ccw.Length];
            for (int i = 0; i < ccw.Length; i++)
            {
                float best = float.MaxValue, y = 0;
                foreach (var s in source)
                {
                    float d = math.distancesq(s.xz, ccw[i]);
                    if (d < best) { best = d; y = s.y; }
                }
                r[i] = new float3(ccw[i].x, y, ccw[i].y);
            }
            return r;
        }
    }
}
