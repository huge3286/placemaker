// Citymaker — charging what the game would charge (US5 "honest price") (GPL-3.0).
//
// Decompiled facts (CS2 1.6.2f1, docs/RESEARCH-NOTES-ADDENDUM.md):
//  * ToolApplySystem sums Temp.m_Cost of every applied temporary and calls PlayerMoney.Subtract —
//    same units as the money display, no scaling.
//  * Objects: Temp.m_Cost = PlaceableObjectData.m_ConstructionCost (trees scaled by age) → charged.
//  * Areas/surfaces: nothing sets Temp.m_Cost → free, no upkeep.
//  * Nets: Game.Net.CostSystem only prices edges whose Temp has TempFlags.Essential, which
//    GenerateEdgesSystem sets only for definitions with CreationFlags.SubElevation|Stamping. Vanilla's
//    NetToolSystem always sets SubElevation; mods (GRG, PM, PLT, us in round 1) never did, so their
//    roads were FREE. SubElevation also changes course splitting/intersection and node elevation in
//    CourseSplitSystem/GenerateNodesSystem, so instead of switching it on blind we charge the nets
//    ourselves right after Apply with the game's own formula, per built edge:
//      NetUtils.GetConstructionCost(curve, startElevation, endElevation, PlaceableNetComposition)
//      = max(1, round(len / 8)) × (composition cost + elevation steps × elevation cost).
//    Upkeep needs nothing: the city budget already counts every permanent edge.

using System;
using Game;
using Game.City;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Citymaker.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace Citymaker.Game.Build
{
    public sealed partial class BuildService
    {
        /// <summary>Charge built roads/paths like the vanilla road tool (default on).</summary>
        public static bool ChargeNetConstruction = true;

        private int _netCharged;

        /// <summary>Construction cost of our edges (temporary or permanent) by the game's formula.</summary>
        private int NetConstructionCost(out int priced)
        {
            priced = 0;
            long total = 0;
            foreach (var e in _tagged)
            {
                if (!EntityManager.Exists(e) || EntityManager.HasComponent<Deleted>(e) || !EntityManager.HasComponent<Edge>(e)
                    || !EntityManager.HasComponent<Curve>(e) || !EntityManager.HasComponent<Composition>(e)) continue;
                var comp = EntityManager.GetComponentData<Composition>(e).m_Edge;
                if (comp == Entity.Null || !EntityManager.Exists(comp) || !EntityManager.HasComponent<PlaceableNetComposition>(comp)) continue;
                var edge = EntityManager.GetComponentData<Edge>(e);
                total += NetUtils.GetConstructionCost(EntityManager.GetComponentData<Curve>(e), ElevationOf(edge.m_Start), ElevationOf(edge.m_End),
                    EntityManager.GetComponentData<PlaceableNetComposition>(comp));
                priced++;
            }
            return (int)math.min(total, int.MaxValue);
        }

        private global::Game.Net.Elevation ElevationOf(Entity node) =>
            node != Entity.Null && EntityManager.Exists(node) && EntityManager.HasComponent<global::Game.Net.Elevation>(node)
                ? EntityManager.GetComponentData<global::Game.Net.Elevation>(node) : default;

        private bool TryGetMoney(out PlayerMoney money, out Entity city)
        {
            money = default;
            city = Entity.Null;
            try
            {
                var cs = World.GetExistingSystemManaged<CitySystem>();
                city = cs != null ? cs.City : Entity.Null;
                if (city == Entity.Null || !EntityManager.Exists(city) || !EntityManager.HasComponent<PlayerMoney>(city)) return false;
                money = EntityManager.GetComponentData<PlayerMoney>(city);
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>Before Apply: can the city pay for the nets on top of what vanilla checks?</summary>
        private bool CanAffordNets(out int cost)
        {
            cost = 0;
            if (!ChargeNetConstruction || _toolSystem.actionMode.IsEditor()) return true;
            cost = NetConstructionCost(out _);
            if (cost <= 0 || !TryGetMoney(out var money, out _)) return true;
            return money.money >= cost;
        }

        /// <summary>After Apply: subtract the nets' construction cost (once per build).</summary>
        private void ChargeNets()
        {
            _netCharged = 0;
            if (!ChargeNetConstruction || _toolSystem.actionMode.IsEditor()) return;
            int cost = NetConstructionCost(out int priced);
            if (cost <= 0 || !TryGetMoney(out var money, out var city)) return;
            money.Subtract(cost);
            EntityManager.SetComponentData(city, money);
            _netCharged = cost;
            Mod.Log.Info($"Citymaker charged {cost} for {priced} road/path edges (vanilla formula).");
        }
    }
}
