// rf-shops3 — entrances for the shops types planned from the player's entrances (strip mall, mall park, mall district:
// Core/Layers/Commercial/MallEntrances.cs). Those types suggest nothing and join no street until the player places a way in,
// so a test that composes one places entrances the way a player would: a two-way car entrance in the middle of the main
// street side (the longest stretch of outline facing a street), optionally a footpath beside it and a driveway (a real lane
// junction) on another street side. The markers go on the recipe's root step, as the tool and the gallery put them.

using System.Collections.Generic;
using System.Linq;
using Citymaker.Core;
using Citymaker.Core.Geometry;
using Citymaker.Core.Layers.Commercial;
using Citymaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Citymaker.Tests
{
    internal static class ShopTestEntrances
    {
        /// <summary>The type is planned from entrances: it offers the shops' "entrances" Markers control.</summary>
        public static bool Uses(IAreaGenerator gen) =>
            gen?.Controls != null && gen.Controls.Any(c => c.Id == ShopEntrances.ControlId && c.Kind == ControlKind.Markers);

        public static bool Uses(string type) => Uses(GeneratorRegistry.Get(type));

        /// <summary>
        /// Places entrances on the recipe's root step when its type is planned from entrances and none are placed yet: a two-way
        /// car entrance mid main street, a footpath on the same side (<paramref name="footpath"/>) and a driveway on another
        /// street side, or further along the main one (<paramref name="driveway"/>). Returns the root's markers.
        /// </summary>
        public static List<EdgeMarker> Place(Recipe r, float3[] ring, SiteContext site, bool footpath = false, bool driveway = false)
        {
            var step = r.Root;
            if (!Uses(step.Layer) || step.Markers.Count > 0) return step.Markers;
            step.Markers.AddRange(For(ring, site, footpath, driveway));
            return step.Markers;
        }

        /// <summary>The street-facing runs of the outline (as the shops types find them), longest first.</summary>
        public static List<(float2 a, float2 b)> Frontage(float3[] ring, SiteContext site)
        {
            var roads = StreetFrontage.Roads(new AreaInput { Site = site ?? new SiteContext() });
            if (roads.Count == 0) return new List<(float2, float2)>();
            var ccw = Poly.EnsureCcw(Poly.To2D(ring));
            var runs = StreetFrontage.FollowEdges(ccw, roads, 2 * StreetFrontage.Cell, StreetShops.Tol);
            // Longest first; near ties keep outline order (deterministic).
            return runs.Select((x, i) => (x, i)).OrderByDescending(p => math.round(math.distance(p.x.a, p.x.b))).ThenBy(p => p.i).Select(p => p.x).ToList();
        }

        public static List<EdgeMarker> For(float3[] ring, SiteContext site, bool footpath = false, bool driveway = false)
        {
            var list = new List<EdgeMarker>();
            var runs = Frontage(ring, site);
            if (runs.Count == 0)
            {
                // No outline faces a street: the car entrance where the outline comes closest to one (or mid first edge).
                list.Add(At(ring, NearestToStreet(ring, site), ShopEntrances.TwoWay));
                return list;
            }
            var (a, b) = runs[0];
            list.Add(At(ring, math.lerp(a, b, 0.5f), ShopEntrances.TwoWay));
            // The footpath beside the car entrance (16 m along, towards the run's start), as a player puts the walk in by the drive.
            float runLen = math.distance(a, b);
            if (footpath) list.Add(At(ring, math.lerp(a, b, math.max(0.15f, 0.5f - 16f / math.max(1f, runLen))), ShopEntrances.Footpath));
            if (driveway)
            {
                // Another street side when there is one (the back or side street), else further along the main one.
                var main = math.normalizesafe(b - a);
                float Dot((float2 a, float2 b) x) => math.dot(math.normalizesafe(x.b - x.a), main);
                var other = runs.Skip(1).Where(x => math.distance(x.a, x.b) >= 40f).ToList();
                var pick = other.Where(x => Dot(x) < -0.5f).Concat(other.Where(x => Dot(x) >= -0.5f && Dot(x) < 0.5f)).ToList();
                var p = pick.Count > 0 ? math.lerp(pick[0].a, pick[0].b, 0.5f) : math.lerp(a, b, 0.75f);
                list.Add(At(ring, p, ShopEntrances.Alley));
            }
            return list;
        }

        /// <summary>A marker of <paramref name="kind"/> at the outline point closest to <paramref name="p"/> (segment and t as the tool stores them).</summary>
        public static EdgeMarker At(float3[] ring, float2 p, string kind)
        {
            int best = 0; float bd = float.MaxValue, bt = 0f;
            for (int i = 0; i < ring.Length; i++)
            {
                var a = ring[i].xz; var b = ring[(i + 1) % ring.Length].xz;
                var ab = b - a; float l2 = math.lengthsq(ab);
                float t = l2 < 1e-6f ? 0f : math.saturate(math.dot(p - a, ab) / l2);
                float d = math.distancesq(p, a + ab * t);
                if (d < bd) { bd = d; best = i; bt = t; }
            }
            var pos = math.lerp(ring[best], ring[(best + 1) % ring.Length], bt);
            return new EdgeMarker { Kind = kind, RingSegment = best, RingT = bt, Position = pos };
        }

        private static float2 NearestToStreet(float3[] ring, SiteContext site)
        {
            var roads = StreetFrontage.Roads(new AreaInput { Site = site ?? new SiteContext() });
            float2 best = math.lerp(ring[0].xz, ring[1 % ring.Length].xz, 0.5f);
            if (roads.Count == 0) return best;
            float bd = float.MaxValue;
            for (int i = 0; i < ring.Length; i++)
            {
                var a = ring[i].xz; var b = ring[(i + 1) % ring.Length].xz;
                for (int k = 0; k <= 8; k++)
                {
                    var p = math.lerp(a, b, k / 8f);
                    foreach (var r in roads)
                    {
                        float d = math.sqrt(SegmentIndex.DistSq(p, r.A, r.B)) - r.HalfWidth;
                        if (d < bd) { bd = d; best = p; }
                    }
                }
            }
            return best;
        }
    }
}
