using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Citymaker.Core;
using Citymaker.Core.Layers.Roads;
using Unity.Mathematics;

namespace Citymaker.Tests
{
    /// <summary>Shared helpers for the roads tests (shapes, fixtures, running the layer).</summary>
    internal static class RoadsTestUtil
    {
        public static List<float3> Poly(params float[] xz)
        {
            var list = new List<float3>();
            for (int i = 0; i < xz.Length; i += 2) list.Add(new float3(xz[i], 0f, xz[i + 1]));
            return list;
        }

        public static List<float3> Rotated(List<float3> shape, float degrees)
        {
            float a = math.radians(degrees), c = math.cos(a), s = math.sin(a);
            return shape.Select(p => new float3(p.x * c - p.z * s, p.y, p.x * s + p.z * c)).ToList();
        }

        public static string FixtureDir([CallerFilePath] string here = "") =>
            Path.Combine(Path.GetDirectoryName(here) ?? ".", "Fixtures", "roads");

        public static List<float3> LoadRing(string file) =>
            File.ReadAllLines(Path.Combine(FixtureDir(), file))
                .Where(l => l.Trim().Length > 0).Select(l => l.Trim().Split(' '))
                .Select(q => new float3(float.Parse(q[0], CultureInfo.InvariantCulture), 0f, float.Parse(q[1], CultureInfo.InvariantCulture)))
                .ToList();

        public static float3[] Ccw(IEnumerable<float3> ring)
        {
            var r = ring.ToArray();
            double a = 0;
            for (int i = 0, j = r.Length - 1; i < r.Length; j = i++) a += (double)r[j].x * r[i].z - (double)r[i].x * r[j].z;
            if (a < 0) Array.Reverse(r);
            return r;
        }

        public static AreaInput Input(IEnumerable<float3> ring, AreaSource source = AreaSource.Drawn, SettingsBag settings = null, SiteContext site = null)
        {
            var layer = new RoadsLayer();
            return new AreaInput
            {
                Shape = new AreaShape { Ring = Ccw(ring), Source = source },
                Site = site ?? new SiteContext(),
                Settings = settings ?? layer.Defaults(),
                Seed = 1,
            };
        }

        public static SettingsBag Settings(params (string id, object value)[] values)
        {
            var b = new RoadsLayer().Defaults();
            foreach (var (id, v) in values) b.Values[id] = v;
            return b;
        }

        public static AreaPlan Run(AreaInput input) => new RoadsLayer().Plan(input, CancellationToken.None);

        public static Func<float2, float> Hills => q => 500f + 40f * math.sin(q.x / 260f) * math.cos(q.y / 310f) + 0.04f * q.x;

        public static float2[] Ring2(float3[] ring) => ring.Select(p => p.xz).ToArray();

        public static double Area(IReadOnlyList<float3> ring)
        {
            double a = 0;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++) a += (double)ring[j].x * ring[i].z - (double)ring[i].x * ring[j].z;
            return Math.Abs(a) * 0.5;
        }
    }
}
