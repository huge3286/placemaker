using Citymaker.Game.Build;
using Unity.Entities;

namespace Citymaker.Game.Selection
{
    /// <summary>
    /// Entity ↔ the opaque long ids used by Core (BoundaryAttachment.ExternalId, NearbyNet.ExternalId,
    /// road-graph half-edges). Delegates to the build workstream's NetRefs so selection and build
    /// agree on one encoding ((Version &lt;&lt; 32) | Index; a stale id never resolves to a reused slot).
    /// </summary>
    public static class EntityIds
    {
        public static long Pack(Entity e) => NetRefs.Encode(e);

        public static Entity Unpack(long id) => new Entity { Index = (int)(uint)(id & 0xFFFFFFFF), Version = (int)(id >> 32) };

        /// <summary>Resolves an id back to a live entity (Entity.Null when it no longer exists).</summary>
        public static Entity Resolve(EntityManager em, long id) => NetRefs.Decode(em, id);
    }
}
