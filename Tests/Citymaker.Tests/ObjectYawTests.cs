using Citymaker.Core.Layers.Park;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    /// <summary>
    /// Yaw convention end to end: PlannedObject.Yaw 0 = facing +Z; the build emits
    /// quaternion.RotateY(yaw), whose forward is (sin yaw, cos yaw) — the front of CS2 benches.
    /// </summary>
    public class ObjectYawTests
    {
        private static float2 Forward(float yaw) => math.mul(quaternion.RotateY(yaw), new float3(0, 0, 1)).xz;

        [Theory]
        [InlineData(1f, 0f, 1f)]
        [InlineData(1f, 0f, -1f)]
        [InlineData(0f, 1f, 1f)]
        [InlineData(0.6f, -0.8f, -1f)]
        public void PathBenchFacesThePath(float tx, float tz, float side)
        {
            var tangent = math.normalize(new float2(tx, tz));
            var normal = new float2(-tangent.y, tangent.x);
            var path = new float2(10, 20);
            var bench = path + normal * 2.5f * side;
            float yaw = ParkPlanting.PathSideYaw(normal, side);
            var toPath = math.normalize(path - bench);
            Assert.True(math.dot(Forward(yaw), toPath) > 0.999f, $"bench forward {Forward(yaw)} vs to-path {toPath}");
        }

        [Fact]
        public void RotateYMatchesAtan2Convention()
        {
            var dir = math.normalize(new float2(0.3f, -0.9f));
            float yaw = math.atan2(dir.x, dir.y);
            Assert.True(math.distance(Forward(yaw), dir) < 1e-4f);
        }
    }
}
