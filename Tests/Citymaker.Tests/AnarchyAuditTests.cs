using System.Collections.Generic;
using Citymaker.Core;
using Citymaker.Core.Build;
using Citymaker.Core.Layers.Parking;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    /// <summary>s53: own-overlap detection (Citymaker anarchy), stall marking size, build audit.</summary>
    public class AnarchyAuditTests
    {
        private static readonly Dictionary<string, AssetInfo> Assets = new Dictionary<string, AssetInfo>
        {
            // ParkingLotDecal01: 3.2 x 6.2 m collision box
            ["Decal"] = new AssetInfo { Id = "Decal", BoundsMin = new float3(-1.6f, -0.1f, -3.1f), BoundsMax = new float3(1.6f, 0.1f, 3.1f) },
        };

        private static PlannedObject Bay(float x, float scale = 1f) =>
            new PlannedObject { Role = ObjectRole.ParkingBay, Asset = new AssetRef("Decal"), Position = new float3(x, 0, 0), Scale = scale };

        private static ComposedPlan Plan(params PlannedObject[] o)
        {
            var l = new AreaPlan { GeneratorId = "test" };
            l.Objects.AddRange(o);
            var p = new ComposedPlan();
            p.Layers.Add(l);
            return p;
        }

        [Fact]
        public void UnscaledMarkingsOnThreeMetrePitchOverlap()
        {
            var ov = new OwnOverlap(Plan(Bay(0), Bay(3f), Bay(100f)), Assets);
            Assert.True(ov.Overlaps(0, 0));
            Assert.True(ov.Overlaps(0, 1));
            Assert.False(ov.Overlaps(0, 2));   // alone: any error there is a collision with the city
        }

        [Fact]
        public void ScaledMarkingsFitTheirStalls()
        {
            float s = ParkingMarkings.Scale(false, 3.0, 5.9);
            Assert.InRange(s, 0.85f, 0.94f);
            var ov = new OwnOverlap(Plan(Bay(0, s), Bay(3f, s)), Assets);
            Assert.False(ov.Overlaps(0, 0, 0f));
            Assert.False(ov.Overlaps(0, 1, 0f));
        }

        [Fact]
        public void MarkingScaleNeverGrowsOrCollapses()
        {
            Assert.Equal(1f, ParkingMarkings.Scale(false, 3.5, 7.0));
            Assert.Equal((float)ParkingMarkings.MinScale, ParkingMarkings.Scale(false, 2.2, 5.0), 3);
            Assert.Equal(1f, ParkingMarkings.Scale(false, double.NaN, 5.9));
        }

        [Fact]
        public void AuditCountsReasonsAndKeepsAnarchyOutOfLeftOut()
        {
            var a = new BuildAudit { Planned = 100, Built = 0 };
            a.Add("decal", "ParkingLotDecal01", new float3(1, 0, 2), AuditReason.Rejected("OverlapExisting"));
            a.Add("decal", "ParkingLotDecal01", new float3(3, 0, 2), AuditReason.Rejected("OverlapExisting"));
            a.Add("object", "Lamp", new float3(5, 0, 5), AuditReason.SiteSafety);
            a.Add("object", "Bush", new float3(9, 0, 9), AuditReason.Anarchy);
            a.Add("object", "Bench", float3.zero, AuditReason.Declutter, 4);
            Assert.Equal(7, a.LeftOut);
            Assert.Equal(1, a.Kept);
            var rows = a.Rows();
            Assert.Equal(AuditReason.Declutter, rows[0].Reason);   // 4 is the biggest
            Assert.Equal(2, a.Positions(AuditReason.Rejected("OverlapExisting")).Count);
            var text = a.Summary();
            Assert.Contains("left out 7", text);
            Assert.Contains("rejected:OverlapExisting x2", text);
            Assert.Contains("ParkingLotDecal01 x2", text);
            Assert.Contains("\"items\"", a.ToJson(3, "t"));
            Assert.Equal("Citymaker.Audit.Reason.rejected", AuditReason.LabelKey("rejected:InvalidShape"));
            Assert.Equal("InvalidShape", AuditReason.Arg("rejected:InvalidShape"));
            Assert.Null(new BuildAudit().ToUi());
        }
    }
}
