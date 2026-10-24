// Game runner: names and members of things in the world, for the step log ("clicked net Medium Road") and for the
// steps that need a build's members (bulldoze, the info panel). It measures nothing.

using System;
using System.Collections.Generic;
using Game.Net;
using Game.Prefabs;
using Game.Zones;
using Citymaker.Core;
using Citymaker.Game.Groups;
using Unity.Entities;

namespace Citymaker.Game.Suite
{
    internal sealed class SuiteLookup
    {
        private readonly EntityManager _em;
        private readonly PrefabSystem _prefabs;

        public SuiteLookup(World world)
        {
            _em = world.EntityManager;
            _prefabs = world.GetOrCreateSystemManaged<PrefabSystem>();
        }

        public string PrefabName(Entity e)
        {
            try
            {
                if (!_em.Exists(e) || !_em.HasComponent<PrefabRef>(e)) return "";
                var p = _em.GetComponentData<PrefabRef>(e).m_Prefab;
                return _prefabs.GetPrefab<PrefabBase>(p)?.name ?? "";
            }
            catch (Exception) { return ""; }
        }

        private Entity PrefabOf(Entity e) => _em.HasComponent<PrefabRef>(e) ? _em.GetComponentData<PrefabRef>(e).m_Prefab : Entity.Null;

        public int GroupOf(Entity e)
        {
            try
            {
                if (_em.Exists(e) && _em.HasComponent<CitymakerMember>(e)) return _em.GetComponentData<CitymakerMember>(e).GroupId;
                return Services.Groups?.GroupOf(e) ?? 0;
            }
            catch (Exception) { return 0; }
        }

        /// <summary>A net (edge or node) that is a city street: a road prefab no Citymaker group owns.</summary>
        public bool CityNet(Entity e)
        {
            if (!_em.Exists(e) || (!_em.HasComponent<Edge>(e) && !_em.HasComponent<global::Game.Net.Node>(e))) return false;
            var pf = PrefabOf(e);
            if (pf == Entity.Null) return false;
            bool road = _em.HasComponent<RoadData>(pf) || (_em.HasComponent<global::Game.Net.Node>(e) && HasRoadEdge(e));
            return road && GroupOf(e) == 0;
        }

        private bool HasRoadEdge(Entity node)
        {
            if (!_em.HasBuffer<ConnectedEdge>(node)) return false;
            var ce = _em.GetBuffer<ConnectedEdge>(node, true);
            for (int i = 0; i < ce.Length; i++)
            {
                var pf = PrefabOf(ce[i].m_Edge);
                if (pf != Entity.Null && _em.HasComponent<RoadData>(pf) && GroupOf(ce[i].m_Edge) == 0) return true;
            }
            return false;
        }

        public string KindOf(Entity e)
        {
            if (_em.HasComponent<Edge>(e)) return "net";
            if (_em.HasComponent<global::Game.Net.Node>(e)) return "node";
            if (_em.HasComponent<global::Game.Areas.Area>(e))
            {
                var p = PrefabOf(e);
                if (p != Entity.Null && _em.HasComponent<SurfaceData>(p)) return "surface";
                if (p != Entity.Null && _em.HasComponent<LotData>(p)) return "lot";
                return "area";
            }
            if (_em.HasComponent<global::Game.Buildings.Building>(e)) return "building";
            if (_em.HasComponent<global::Game.Objects.Object>(e)) return "object";
            if (_em.HasComponent<Block>(e)) return "zoneblock";
            return "other";
        }

        public HashSet<Entity> MemberSet(int groupId)
        {
            var list = new List<Entity>();
            if (groupId > 0 && Services.Groups != null) { try { Services.Groups.GetMembers(groupId, list); } catch (Exception) { } }
            return new HashSet<Entity>(list);
        }
    }
}
