using System.Collections.Generic;
using System.Linq;
using Citymaker.Core;
using Citymaker.Core.Build;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    /// <summary>s59: one validation pass (no exclude-and-retry loop) and plan-time collision freedom against existing nets.</summary>
    public class BuildVerdictsTests
    {
        private static FlaggedPiece Piece(long key, VerdictKind kind, string type = "OverlapExisting", bool functional = false, bool building = false, string name = null, bool ours = true)
            => new FlaggedPiece { Key = key, Ours = ours, Kind = kind, ErrorType = type, Functional = functional, TouchesBuilding = building, Name = name ?? kind.ToString(), At = new float3(key, 0, -key) };

        // ───────────── decision logic ─────────────

        [Fact]
        public void AnarchyBuildsEverythingTheGameFlagged()
        {
            // The 2026-10-15 lot: markings on markings, leaves decals, bushes, an invisible lane with InvalidShape.
            var flagged = new List<FlaggedPiece>();
            for (int i = 0; i < 61; i++) flagged.Add(Piece(i + 1, VerdictKind.Object, functional: true, name: "ParkingLotDecal01"));
            for (int i = 0; i < 25; i++) flagged.Add(Piece(100 + i, VerdictKind.Decal, name: "Leaves"));
            for (int i = 0; i < 12; i++) flagged.Add(Piece(200 + i, VerdictKind.Object, name: "GreenBushWild02"));
            flagged.Add(Piece(300, VerdictKind.Net, "InvalidShape", name: "Invisible Road Path - 2xTwoway"));
            flagged.Add(Piece(0, VerdictKind.Net, ours: false));   // an existing road copy the game also flagged
            var d = BuildVerdicts.Decide(flagged, anarchy: true);
            Assert.False(d.Fail);
            Assert.Empty(d.Drop);
            Assert.Equal(61 + 25 + 12 + 1, d.KeptDespite.Count);
        }

        [Fact]
        public void AnarchyNeverDemolishesButBuildsNetsByBuildings()
        {
            var d = BuildVerdicts.Decide(new[]
            {
                Piece(1, VerdictKind.Object, building: true, name: "Lamp"),
                Piece(2, VerdictKind.Area, building: true),
                Piece(3, VerdictKind.Net, building: true, name: "Alley"),
                Piece(4, VerdictKind.Object),
            }, anarchy: true);
            Assert.False(d.Fail);
            Assert.Equal(new long[] { 1, 2 }, d.Drop.Select(x => x.Key.Key).ToArray());
            Assert.All(d.Drop, x => Assert.Equal(AuditReason.SiteSafety, x.Value));
            Assert.Single(d.NetsByBuildings);
            Assert.Equal(3, d.NetsByBuildings[0].Key);
            Assert.Contains(d.KeptDespite, p => p.Key == 4);
        }

        [Fact]
        public void MoneyStopsTheBuildInEveryMode()
        {
            foreach (bool anarchy in new[] { true, false })
            {
                var d = BuildVerdicts.Decide(new[] { Piece(1, VerdictKind.Object), Piece(2, VerdictKind.Net, BuildVerdicts.Money) }, anarchy);
                Assert.True(d.Fail);
                Assert.Equal(BuildVerdicts.Money, d.FailErrorType);
            }
        }

        [Fact]
        public void VanillaRulesLeaveOutDecorativePiecesOnceAndFailOnlyOnNets()
        {
            var flagged = new[]
            {
                Piece(1, VerdictKind.Object, name: "Bush"),
                Piece(1, VerdictKind.Object, name: "Bush"),      // two errors on one piece: decided once
                Piece(2, VerdictKind.Decal),
                Piece(3, VerdictKind.Object, functional: true),
                Piece(4, VerdictKind.Surface, "InvalidShape"),
                Piece(9, VerdictKind.Net, ours: false),           // not ours: the game's business
            };
            var d = BuildVerdicts.Decide(flagged, anarchy: false);
            Assert.False(d.Fail);
            Assert.Equal(new long[] { 1, 2, 3, 4 }, d.Drop.Select(x => x.Key.Key).ToArray());
            Assert.Equal(AuditReason.Rejected("InvalidShape"), d.Drop[3].Value);
            Assert.Empty(d.KeptDespite);

            var withNet = BuildVerdicts.Decide(flagged.Concat(new[] { Piece(7, VerdictKind.Net, "InvalidShape", name: "Alley") }), anarchy: false);
            Assert.True(withNet.Fail);
            Assert.Equal(7, withNet.Blocker.Value.Key);
            Assert.Equal("InvalidShape", withNet.FailErrorType);
            Assert.Equal("net 'Alley' at (7, -7)", BuildVerdicts.Describe(withNet.Blocker.Value));
        }

        [Fact]
        public void VanillaRulesStopForAPieceOnABuildingAndWhenLeavingOutIsOff()
        {
            var net = BuildVerdicts.Decide(new[] { Piece(5, VerdictKind.Net, building: true) }, anarchy: false);
            Assert.True(net.Fail);
            Assert.True(net.Blocker.Value.TouchesBuilding);

            var obj = BuildVerdicts.Decide(new[] { Piece(5, VerdictKind.Object, building: true) }, anarchy: false);
            Assert.False(obj.Fail);   // the object goes, the building stays
            Assert.Equal(AuditReason.SiteSafety, obj.Drop.Single().Value);

            var strict = BuildVerdicts.Decide(new[] { Piece(6, VerdictKind.Object) }, anarchy: false, dropDecorative: false);
            Assert.True(strict.Fail);
        }

        [Fact]
        public void HistogramNamesTypesAndCarriers()
        {
            var h = BuildVerdicts.Histogram(new[]
            {
                Piece(1, VerdictKind.Decal, name: "ParkingLotDecal01"), Piece(2, VerdictKind.Decal, name: "ParkingLotDecal01"),
                Piece(3, VerdictKind.Object, name: "Bush"), Piece(4, VerdictKind.Net, "InvalidShape", name: "Alley"),
            });
            Assert.Equal("InvalidShape×1 [net 'Alley' ×1], OverlapExisting×3 [decal 'ParkingLotDecal01' ×2; object 'Bush' ×1]", h);
            Assert.False(BuildVerdicts.Decide(null, true).Fail);
        }

        // ───────────── plan time: objects stay off captured city nets ─────────────

        private static readonly Dictionary<string, AssetInfo> Assets = new Dictionary<string, AssetInfo>
        {
            ["Bench"] = new AssetInfo { Id = "Bench", BoundsMin = new float3(-0.75f, 0f, -0.38f), BoundsMax = new float3(0.75f, 1.13f, 0.38f) },
            ["Tree"] = new AssetInfo { Id = "Tree", BoundsMin = new float3(-1.3f, 0f, -1.3f), BoundsMax = new float3(1.3f, 5.7f, 1.3f) },
            ["Decal"] = new AssetInfo { Id = "Decal", BoundsMin = new float3(-1.6f, -0.1f, -3.1f), BoundsMax = new float3(1.6f, 0.1f, 3.1f) },
        };

        /// <summary>An existing 12 m street along x = 0 (z from -50 to 50): its sidewalk edge is at x = ±6.</summary>
        private static NearbyNet Street(long id = 42) => new NearbyNet
        {
            ExternalId = id, A = new float3(0, 0, -50), B = new float3(0, 0, -17), C = new float3(0, 0, 17), D = new float3(0, 0, 50), Width = 12f, Category = NetCategory.Road, HasSidewalk = true,
        };

        private static ComposedPlan Plan(IEnumerable<NearbyNet> existing, params PlannedObject[] objects)
        {
            var l = new AreaPlan { GeneratorId = "test" };
            l.Objects.AddRange(objects);
            var p = new ComposedPlan();
            p.Layers.Add(l);
            p.ExistingNets = existing?.ToList() ?? new List<NearbyNet>();
            return p;
        }

        private static PlannedObject Obj(ObjectRole role, string id, float x, float z) =>
            new PlannedObject { Role = role, Asset = new AssetRef(id), Position = new float3(x, 0, z) };

        [Fact]
        public void ObjectsAreMovedOffOrDroppedFromExistingStreets()
        {
            var plan = Plan(new[] { Street() },
                Obj(ObjectRole.Bench, "Bench", 6.3f, 0f),    // pokes 0.45 m into the sidewalk: nudged out
                Obj(ObjectRole.Tree, "Tree", 6.5f, 20f),     // 1.3 m into it: nudged out (≤ 2 m)
                Obj(ObjectRole.Tree, "Tree", 2f, -20f),      // in the middle of the road: dropped
                Obj(ObjectRole.Bench, "Bench", 12f, 10f));   // clear: untouched
            bool was = Declutter.Anarchy;
            try
            {
                foreach (bool anarchy in new[] { true, false })
                {
                    Declutter.Anarchy = anarchy;
                    var p = Plan(plan.ExistingNets, plan.Layers[0].Objects.Select(o => Obj(o.Role, o.Asset.Id, o.Position.x, o.Position.z)).ToArray());
                    Declutter.Run(p, Assets);
                    var left = p.Layers[0].Objects;
                    Assert.Equal(3, left.Count);
                    foreach (var o in left)
                    {
                        Assert.True(Declutter.TryFootprint(o, Assets, out var f));
                        Assert.True(Declutter.Distance(f, new float2(0, -50), new float2(0, 50)) >= 6f, $"{o.Asset.Id} at {o.Position} still on the street (anarchy {anarchy})");
                    }
                    Assert.Contains(left, o => o.Position.x == 12f && o.Position.z == 10f);
                }
            }
            finally { Declutter.Anarchy = was; }
        }

        [Fact]
        public void DecalsAndParkingPiecesAreNotPushedByStreets()
        {
            var p = Plan(new[] { Street() }, Obj(ObjectRole.Decal, "Decal", 5f, 0f), Obj(ObjectRole.ParkingBay, "Decal", 6.5f, 10f));
            Declutter.Run(p, Assets);
            Assert.Equal(2, p.Layers[0].Objects.Count);
            Assert.Equal(5f, p.Layers[0].Objects[0].Position.x);
            Assert.Equal(6.5f, p.Layers[0].Objects[1].Position.x);
        }

        [Fact]
        public void SiteSafetyRecordsOnlyCapturedCityNets()
        {
            var site = new SiteContext();
            site.NearbyNets.Add(Street(7));
            site.NearbyNets.Add(Street(-700001));   // a composite's synthetic lane (planned, not existing)
            var zero = Street(8); zero.Width = 0f;
            site.NearbyNets.Add(zero);
            var plan = new ComposedPlan();
            SiteSafety.Apply(plan, null, site);
            Assert.Single(plan.ExistingNets);
            Assert.Equal(7, plan.ExistingNets[0].ExternalId);
        }
    }
}
