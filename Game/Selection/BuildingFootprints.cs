// Citymaker — the ground an existing building owns (s15): its lot (BuildingData lot cells × 8 m) and,
// where it reaches past the lot (or the prefab has no lot, e.g. a building extension), its collision
// footprint (ObjectGeometryData bounds). Whatever we build must keep off both: a net or physical area
// touching a spawnable building deletes it on Apply, and blocked zone cells under its lot condemn it
// (docs/RESEARCH-NOTES-ADDENDUM "Existing buildings").

using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Entities;
using Unity.Mathematics;

namespace Citymaker.Game.Selection
{
    public static class BuildingFootprints
    {
        /// <summary>True for live buildings and building extensions (not temporaries, not deleted).</summary>
        public static bool IsBuilding(EntityManager em, Entity e) =>
            (em.HasComponent<global::Game.Buildings.Building>(e) || em.HasComponent<global::Game.Buildings.Extension>(e))
            && em.HasComponent<global::Game.Objects.Transform>(e) && em.HasComponent<PrefabRef>(e)
            && !em.HasComponent<Temp>(e) && !em.HasComponent<Deleted>(e);

        /// <summary>Lot quad (or null) and footprint quad where it reaches past the lot (or null), XZ.</summary>
        public static void Of(EntityManager em, Entity e, out float2[] lotQuad, out float2[] footprintQuad)
        {
            lotQuad = footprintQuad = null;
            var prefab = em.GetComponentData<PrefabRef>(e).m_Prefab;
            var tr = em.GetComponentData<global::Game.Objects.Transform>(e);
            float2 lot = default;
            if (em.HasComponent<BuildingData>(prefab))
            {
                lot = (float2)em.GetComponentData<BuildingData>(prefab).m_LotSize * 8f;
                if (math.all(lot > 0f))
                {
                    var q = global::Game.Objects.ObjectUtils.CalculateBaseCorners(tr.m_Position, tr.m_Rotation, lot);
                    lotQuad = new[] { q.a.xz, q.b.xz, q.c.xz, q.d.xz };
                }
            }
            if (!em.HasComponent<ObjectGeometryData>(prefab)) return;
            var b = em.GetComponentData<ObjectGeometryData>(prefab).m_Bounds;
            float2 bmin = b.min.xz, bmax = b.max.xz;
            if (!math.all(bmax > bmin) || math.cmax(bmax - bmin) > 2000f) return;
            bool beyond = lotQuad == null || math.any(bmin < -lot * 0.5f - 0.25f) || math.any(bmax > lot * 0.5f + 0.25f);
            if (!beyond) return;
            var f = global::Game.Objects.ObjectUtils.CalculateBaseCorners(tr.m_Position, tr.m_Rotation, b);
            footprintQuad = new[] { f.a.xz, f.b.xz, f.c.xz, f.d.xz };
        }
    }
}
