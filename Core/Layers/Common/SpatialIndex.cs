// Uniform-grid indexes for the clearance checks every layer runs thousands of times.
// A 5 ha park tests ~30k planting candidates against paths and other plants; linear scans
// (ParkManager's original HasPathClearance/TooCloseToKind) cost O(n·m) and dominated planning
// time. Buckets keep each query local.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Citymaker.Core.Layers.Common
{
    /// <summary>Discs (objects) with an integer kind, queried by neighbourhood.</summary>
    public sealed class DiscIndex
    {
        public struct Item { public float2 P; public float R; public int Kind; }

        private readonly float _cell;
        private readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();
        public readonly List<Item> Items = new List<Item>();
        public float MaxRadius { get; private set; }

        public DiscIndex(float cell = 8f) { _cell = math.max(0.5f, cell); }

        public int Add(float2 p, float r, int kind)
        {
            int id = Items.Count;
            Items.Add(new Item { P = p, R = r, Kind = kind });
            MaxRadius = math.max(MaxRadius, r);
            var c = Cell(p);
            var key = Key(c.x, c.y);
            if (!_cells.TryGetValue(key, out var list)) _cells[key] = list = new List<int>(4);
            list.Add(id);
            return id;
        }

        /// <summary>Indices of items whose centre lies within <paramref name="radius"/> + MaxRadius of p (superset).</summary>
        public void Query(float2 p, float radius, List<int> into)
        {
            into.Clear();
            float reach = radius + MaxRadius;
            var lo = Cell(p - reach); var hi = Cell(p + reach);
            for (int y = lo.y; y <= hi.y; y++)
                for (int x = lo.x; x <= hi.x; x++)
                    if (_cells.TryGetValue(Key(x, y), out var list)) into.AddRange(list);
        }

        /// <summary>True when a disc (p, r) plus <paramref name="gap"/> overlaps any item.</summary>
        public bool Overlaps(float2 p, float r, float gap = 0f)
        {
            float reach = r + gap + MaxRadius;
            var lo = Cell(p - reach); var hi = Cell(p + reach);
            for (int y = lo.y; y <= hi.y; y++)
                for (int x = lo.x; x <= hi.x; x++)
                {
                    if (!_cells.TryGetValue(Key(x, y), out var list)) continue;
                    foreach (int i in list)
                    {
                        var it = Items[i];
                        float d = r + it.R + gap;
                        if (math.distancesq(p, it.P) < d * d) return true;
                    }
                }
            return false;
        }

        /// <summary>Distance from p to the nearest item rim (negative inside), capped at <paramref name="cap"/>.</summary>
        public float Clearance(float2 p, float cap)
        {
            float best = cap;
            float reach = cap + MaxRadius;
            var lo = Cell(p - reach); var hi = Cell(p + reach);
            for (int y = lo.y; y <= hi.y; y++)
                for (int x = lo.x; x <= hi.x; x++)
                {
                    if (!_cells.TryGetValue(Key(x, y), out var list)) continue;
                    foreach (int i in list)
                    {
                        var it = Items[i];
                        best = math.min(best, math.distance(p, it.P) - it.R);
                    }
                }
            return best;
        }

        private int2 Cell(float2 p) => (int2)math.floor(p / _cell);
        private static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;
    }

    /// <summary>Segments with half widths (paths, fences, reserved corridors).</summary>
    public sealed class SegmentIndex
    {
        public struct Seg { public float2 A, B; public float HalfWidth; public int Kind; }

        private readonly float _cell;
        private readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();
        public readonly List<Seg> Segments = new List<Seg>();
        private int[] _stamp = new int[64];
        private int _stampValue;

        public SegmentIndex(float cell = 10f) { _cell = math.max(0.5f, cell); }

        public int Count => Segments.Count;

        public void Add(float2 a, float2 b, float halfWidth, int kind = 0)
        {
            int id = Segments.Count;
            Segments.Add(new Seg { A = a, B = b, HalfWidth = halfWidth, Kind = kind });
            if (_stamp.Length < Segments.Count) Array.Resize(ref _stamp, Segments.Count * 2);
            var lo = Cell(math.min(a, b) - halfWidth); var hi = Cell(math.max(a, b) + halfWidth);
            // Long diagonal segments would fill a big rectangle of cells; only cells near the line matter.
            float reach = halfWidth + _cell * 0.75f;
            for (int y = lo.y; y <= hi.y; y++)
                for (int x = lo.x; x <= hi.x; x++)
                {
                    var centre = (new float2(x, y) + 0.5f) * _cell;
                    if (DistSq(centre, a, b) > reach * reach) continue;
                    var key = Key(x, y);
                    if (!_cells.TryGetValue(key, out var list)) _cells[key] = list = new List<int>(4);
                    list.Add(id);
                }
        }

        /// <summary>True when a disc (p, r) touches any segment corridor (+gap).</summary>
        public bool Overlaps(float2 p, float r, float gap = 0f)
        {
            return Clearance(p, r + gap + 0.001f) < r + gap;
        }

        /// <summary>Distance from p to the nearest corridor edge (segment distance − half width), capped.</summary>
        public float Clearance(float2 p, float cap)
        {
            if (Segments.Count == 0) return cap;
            unchecked { _stampValue++; }
            if (_stampValue == 0) { Array.Clear(_stamp, 0, _stamp.Length); _stampValue = 1; }
            float best = cap;
            // Segments are registered in every cell within halfWidth + 0.75 cell of the line, so
            // scanning cells within `cap` of p plus one cell of slack finds every candidate.
            var lo = Cell(p - cap - _cell); var hi = Cell(p + cap + _cell);
            long cellsWide = (long)(hi.x - lo.x + 1) * (hi.y - lo.y + 1);
            if (cellsWide > Segments.Count * 4L)
            {
                for (int i = 0; i < Segments.Count; i++) best = math.min(best, Dist(p, i));
                return best;
            }
            for (int y = lo.y; y <= hi.y; y++)
                for (int x = lo.x; x <= hi.x; x++)
                {
                    if (!_cells.TryGetValue(Key(x, y), out var list)) continue;
                    foreach (int i in list)
                    {
                        if (_stamp[i] == _stampValue) continue;
                        _stamp[i] = _stampValue;
                        best = math.min(best, Dist(p, i));
                    }
                }
            return best;
        }

        /// <summary>Nearest segment index within cap (−1 if none) and the closest point on it.</summary>
        public int Nearest(float2 p, float cap, out float2 closest)
        {
            closest = p;
            int bestId = -1; float best = cap;
            for (int i = 0; i < Segments.Count; i++)
            {
                float d = Dist(p, i);
                if (d < best) { best = d; bestId = i; }
            }
            if (bestId >= 0)
            {
                var s = Segments[bestId];
                closest = ClosestPoint(p, s.A, s.B);
            }
            return bestId;
        }

        private float Dist(float2 p, int i)
        {
            var s = Segments[i];
            return math.sqrt(DistSq(p, s.A, s.B)) - s.HalfWidth;
        }

        public static float DistSq(float2 p, float2 a, float2 b) => math.distancesq(p, ClosestPoint(p, a, b));

        public static float2 ClosestPoint(float2 p, float2 a, float2 b)
        {
            var ab = b - a;
            float len = math.lengthsq(ab);
            float t = len > 1e-9f ? math.saturate(math.dot(p - a, ab) / len) : 0f;
            return a + ab * t;
        }

        private int2 Cell(float2 p) => (int2)math.floor(p / _cell);
        private static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;
    }
}
