// Citymaker — keeps the park stats of loaded builds in line with their receipts and the settings (GPL-3.0).
// Attractiveness lives either on the owner lot (vanilla AttractivenessProvider, BuildService.ParkStats.cs) or,
// with "Real park stats", on the companion park building (Economy/ParkCompanionSystem). After a load we repair:
//  * companion setting off → companions are removed (PreTool) and the lot gets attractiveness back;
//  * a companion whose prefab did not resolve (clone list lost) → removed and created again from the receipt;
//  * the lot carries attractiveness only while there is no companion and "counts for tourism" is on.

using System;
using System.Collections.Generic;
using Game.Buildings;
using Game.Prefabs;
using Citymaker.Core.Build;
using Citymaker.Game.Build;
using Citymaker.Game.Economy;
using Unity.Entities;
using Unity.Mathematics;

namespace Citymaker.Game.Groups
{
    public sealed partial class GroupRegistrySystem
    {
        private void SyncParkStats()
        {
            int lotAdded = 0, lotRemoved = 0, removed = 0, requested = 0;
            var companions = World.GetOrCreateSystemManaged<ParkCompanionSystem>();
            var cleanup = World.GetOrCreateSystemManaged<ParkCompanionCleanupSystem>();
            var members = new List<Entity>();
            foreach (var kv in _records)
            {
                try
                {
                    var g = EntityManager.GetComponentData<CitymakerGroup>(kv.Value);
                    var rec = GetRecord(kv.Key);
                    if (rec?.ParkStats == null) continue;

                    Entity companion = Entity.Null;
                    members.Clear();
                    GetMembers(kv.Key, members);
                    foreach (var m in members)
                        if (EntityManager.Exists(m) && EntityManager.GetComponentData<CitymakerMember>(m).Role == MemberRole.ParkCompanion) { companion = m; break; }
                    bool wanted = BuildService.ApplyParkStats && companions.Available && rec.ParkStats.Applied.ContainsKey(ParkStatIds.Companion);
                    if (companion != Entity.Null)
                    {
                        var prefab = EntityManager.HasComponent<PrefabRef>(companion) ? EntityManager.GetComponentData<PrefabRef>(companion).m_Prefab : Entity.Null;
                        bool broken = prefab == Entity.Null || !EntityManager.Exists(prefab) || !EntityManager.HasComponent<ParkData>(prefab);
                        if (!wanted || broken)
                        {
                            float2 at = default; float yaw = 0;
                            if (EntityManager.HasComponent<global::Game.Objects.Transform>(companion))
                            {
                                var t = EntityManager.GetComponentData<global::Game.Objects.Transform>(companion);
                                at = t.m_Position.xz;
                                var f = math.mul(t.m_Rotation, new float3(0, 0, 1));
                                yaw = math.atan2(f.x, f.z);
                            }
                            cleanup.Remove(companion);
                            companion = Entity.Null;
                            removed++;
                            if (wanted && broken)
                            {
                                companions.Request(kv.Key, ParkPrefabKey.From(rec.ParkStats.Applied, rec.ParkStats.LeisureType == (int)LeisureKind.CityBeach).Name, at, yaw);
                                requested++;
                            }
                        }
                    }
                    else if (wanted && rec.ParkStats.Applied.TryGetValue(ParkStatIds.Companion, out var state) && state == 1)
                        Mod.Log.Warn($"Citymaker: the park companion of group {kv.Key} is gone (removed by another tool?); its lot carries attractiveness again.");

                    var owner = g.Owner;
                    if (owner == Entity.Null || !EntityManager.Exists(owner)) continue;
                    bool has = rec.ParkStats.Applied.TryGetValue(ParkStatIds.InGameAttractiveness, out double value) && value > 0;
                    bool present = EntityManager.HasComponent<AttractivenessProvider>(owner);
                    bool lotShould = BuildService.ApplyParkStats && has && companion == Entity.Null;
                    if (present && !lotShould)
                    {
                        EntityManager.RemoveComponent<AttractivenessProvider>(owner);
                        lotRemoved++;
                    }
                    else if (lotShould && !present && BuildService.ApplyAttractiveness(EntityManager, owner, value, 0) > 0)
                        lotAdded++;
                }
                catch (Exception e)
                {
                    Mod.Log.Warn($"Citymaker: park stats of group {kv.Key} not synced: {e.Message}");
                }
            }
            if (lotAdded + lotRemoved + removed + requested > 0)
                Mod.Log.Info($"Citymaker park stats after load: lot attractiveness {lotAdded} restored / {lotRemoved} removed, "
                    + $"{removed} companions removed, {requested} recreated.");
        }
    }
}
