// Citymaker — "Debug: dump terrain after build" (s30, GPL-3.0).
//
// The offline gallery models the terraform raster, not what CS2 then does with it (net terrain clipping, the heightmap's
// 3.5 m texels, quay walls). With the setting on, every finished build writes the live result to
// ModsData/Citymaker/debug/<group>.json (Core/Build/TerrainDump): heights before (sampled at build start) and after
// (sampled at Finish, after the terraform passes and Apply), water depth, the planned terraform target, the group's
// built nets (live curves) and objects, the plan's facts. `dotnet run --project Tests/Citymaker.Gallery -- --dump <file>`
// renders it. Off by default: sampling ~90 k heights and writing ~0.5 MB takes a few frames.

using System;
using System.Collections.Generic;
using System.IO;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Simulation;
using Citymaker.Core;
using Citymaker.Core.Build;
using Citymaker.Game.Settings;
using Unity.Entities;
using Unity.Mathematics;

namespace Citymaker.Game.Build
{
    public sealed partial class BuildService
    {
        /// <summary>Set from CitymakerSettings.DebugTerrainDump.</summary>
        public static bool DebugTerrainDump;

        private const int DumpMaxSide = 300;
        private TerrainDump _dump;

        /// <summary>Build start: the grid over the plan (its rings, nets and objects + 30 m) and the ground before the build.</summary>
        private void DumpBegin()
        {
            _dump = null;
            if (!DebugTerrainDump || _request?.Plan == null) return;
            try
            {
                float2 min = float.MaxValue, max = float.MinValue;
                void Grow(float2 p) { min = math.min(min, p); max = math.max(max, p); }
                foreach (var l in _request.Plan.Layers)
                {
                    if (l == null) continue;
                    foreach (var t in l.Terraform) if (t?.Ring != null) foreach (var v in t.Ring) Grow(v.xz);
                    foreach (var n in l.Nets) if (n != null) { Grow(n.A.xz); Grow(n.D.xz); }
                    foreach (var o in l.Objects) if (o != null) Grow(o.Position.xz);
                }
                foreach (var r in _request.Plan.Footprints) foreach (var p in r) Grow(p);
                if (!(max.x > min.x)) return;
                min -= 30f; max += 30f;
                float cell = math.max(2f, math.cmax(max - min) / DumpMaxSide);
                var d = new TerrainDump
                {
                    Origin = min, Cell = cell,
                    Nx = (int)math.ceil((max.x - min.x) / cell) + 1, Ny = (int)math.ceil((max.y - min.y) / cell) + 1,
                    When = DateTime.Now.ToString("s"),
                };
                d.Before = SampleGrid(d);
                foreach (var r in _request.Plan.Footprints) d.Outlines.Add(r);
                _dump = d;
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Citymaker terrain dump (start) failed: " + e.Message);
                _dump = null;
            }
        }

        private float[] SampleGrid(TerrainDump d)
        {
            var g = new float[d.Count];
            var heights = _terrain.GetHeightData(true);
            for (int j = 0; j < d.Ny; j++)
                for (int i = 0; i < d.Nx; i++)
                {
                    var p = d.Pos(i, j);
                    g[j * d.Nx + i] = TerrainUtils.SampleHeight(ref heights, new float3(p.x, 0f, p.y));
                }
            return g;
        }

        /// <summary>Finish: the live ground, water, planned target, the group's nets and objects, written to ModsData/Citymaker/debug.</summary>
        private void DumpFinish(int groupId)
        {
            var d = _dump;
            _dump = null;
            if (d == null || !DebugTerrainDump) return;
            try
            {
                d.Group = groupId.ToString();
                d.Name = _request?.CustomName ?? "";
                d.After = SampleGrid(d);
                // Water depth from the live surface.
                d.Water = new float[d.Count];
                try
                {
                    var water = _water.GetSurfacesData(out var deps);
                    deps.Complete();
                    for (int j = 0; j < d.Ny; j++)
                        for (int i = 0; i < d.Nx; i++)
                        {
                            var p = d.Pos(i, j);
                            d.Water[j * d.Nx + i] = WaterUtils.SampleDepth(ref water, new float3(p.x, 0f, p.y));
                        }
                }
                catch (Exception e) { Mod.Log.Warn("Citymaker terrain dump: water sampling failed: " + e.Message); }
                // The planned target where the raster touched the ground.
                if (_tfRaster != null)
                {
                    d.Planned = new float[d.Count];
                    var before = d.Before;
                    for (int j = 0; j < d.Ny; j++)
                        for (int i = 0; i < d.Nx; i++)
                        {
                            int k = j * d.Nx + i;
                            float b = before[k];
                            d.Planned[k] = _tfRaster.TrySample(d.Pos(i, j), _ => b, out float h) ? h : float.NaN;
                        }
                }
                // The group's live nets and objects.
                var members = new List<Entity>();
                if (groupId != 0) _registry.GetMembers(groupId, members);
                foreach (var e in members)
                {
                    if (!EntityManager.Exists(e)) continue;
                    string prefab = PrefabName(e);
                    if (EntityManager.HasComponent<Curve>(e) && EntityManager.HasComponent<Edge>(e))
                    {
                        var c = EntityManager.GetComponentData<Curve>(e).m_Bezier;
                        float width = 3f;
                        if (EntityManager.HasComponent<PrefabRef>(e))
                        {
                            var pr = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                            if (EntityManager.HasComponent<NetGeometryData>(pr)) width = EntityManager.GetComponentData<NetGeometryData>(pr).m_DefaultWidth;
                        }
                        bool elevated = EntityManager.HasComponent<global::Game.Net.Elevation>(e);
                        d.Nets.Add(new TerrainDump.Net
                        {
                            A = c.a, B = c.b, C = c.c, D = c.d, Width = width, Prefab = prefab,
                            Follow = !elevated, Quay = prefab.IndexOf("Quay", StringComparison.OrdinalIgnoreCase) >= 0,
                        });
                    }
                    else if (EntityManager.HasComponent<Transform>(e))
                    {
                        var t = EntityManager.GetComponentData<Transform>(e);
                        var fwd = math.mul(t.m_Rotation, new float3(0, 0, 1));
                        d.Objects.Add(new TerrainDump.Obj { Position = t.m_Position, Yaw = math.atan2(fwd.x, fwd.z), Prefab = prefab });
                    }
                }
                // The plan's facts (walk radius, wall height, earthworks…) and the water level it found.
                if (_request?.Plan != null)
                    foreach (var l in _request.Plan.Layers)
                    {
                        if (l == null) continue;
                        foreach (var kv in l.Facts)
                        {
                            d.Facts.Add(new KeyValuePair<string, double>(kv.Key, kv.Value));
                            if (kv.Key == "shoreWaterLevel") d.WaterLevel = (float)kv.Value;
                        }
                    }
                var dir = Path.Combine(ModPaths.DataFolder, "debug");
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, $"group-{groupId}-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                File.WriteAllText(file, d.ToJson());
                Mod.Log.Info($"Citymaker terrain dump: {file} ({d.Nx}x{d.Ny} @ {d.Cell:F1} m, {d.Nets.Count} nets, {d.Objects.Count} objects; {d.Metrics()})");
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Citymaker terrain dump failed: " + e.Message);
            }
        }

        private string PrefabName(Entity e)
        {
            try
            {
                if (!EntityManager.HasComponent<PrefabRef>(e)) return "";
                var pr = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                return _prefabs.TryGetPrefab<PrefabBase>(pr, out var p) && p != null ? p.name : "";
            }
            catch (Exception) { return ""; }
        }
    }
}
