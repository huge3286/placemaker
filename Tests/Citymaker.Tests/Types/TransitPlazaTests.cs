// Type wave: transit plaza (group "transport"). Shared checks: TransportTypeChecks (RestAreaTests.cs).

using System.Collections.Generic;
using System.Linq;
using Citymaker.Core;
using Citymaker.Core.Layers.Common;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Citymaker.Tests.Types
{
    [Collection(Citymaker.Tests.TimingCollection.Name)]
    public class TransitPlazaTests
    {
        private const string Id = "transitPlaza";
        private readonly ITestOutputHelper _out;
        public TransitPlazaTests(ITestOutputHelper output) { _out = output; }

        [Fact] public void EveryPresetPlansOneAndSixHectares() => TransportTypeChecks.EveryPresetPlansOneAndSixHectares(Id);
        [Fact] public void IsDeterministic() => TransportTypeChecks.Deterministic(Id);
        [Fact] public void RespectsReserved() => TransportTypeChecks.RespectsReserved(Id);
        [Fact] public void IsFastOnFiveHectares() => TransportTypeChecks.Fast(Id, _out);
        [Fact] public void EveryFeatureComposes() => TransportTypeChecks.FeaturesCompose(Id);
        [Fact] public void EveryLabelHasEnglishText() => TransportTypeChecks.LabelsExist(Id, "TransitPlaza", "transitPlaza.json");
        [Fact] public void ControlsAreDeepButTidy() => TransportTypeChecks.ControlCountAndGroups(Id);

        private static double Stat(AreaPlan p, string key) => TestKit.Stat(p, key);

        [Fact]
        public void BaysLieOnTheKerbLaneAlongTheStreet()
        {
            var plan = TransportTypeChecks.Preset(Id, "busPlaza");
            Assert.True(Stat(plan, "Citymaker.TransitPlaza.Stat.BusBays") >= 4);
            // The kerb lane is asphalt along the road (bottom edge, z ≈ 0..4 m).
            var kerb = plan.Surfaces.Where(s => s.Role == SurfaceRole.Asphalt).ToList();
            Assert.NotEmpty(kerb);
            Assert.All(kerb, s => Assert.True(s.Ring.Max(p => p.z) < 5f, "kerb lane hugs the frontage"));
            // Bay markings sit on it, shelters stand right behind it.
            var marks = plan.Objects.Where(o => o.Category == AssetCategories.DecalMarkings).ToList();
            Assert.NotEmpty(marks);
            Assert.All(marks, o => Assert.InRange(o.Position.z, 0f, 4f));
            var shelters = plan.Objects.Where(o => o.Category == AssetCategories.TransportShelter).ToList();
            Assert.NotEmpty(shelters);
            Assert.All(shelters, o => Assert.InRange(o.Position.z, 4f, 9f));
            Assert.Contains(plan.Objects, o => o.Category == AssetCategories.Bollard);
            Assert.Contains(plan.Areas, a => a.Role == AreaRole.PedestrianArea);
            Assert.Contains(plan.Surfaces, s => s.Role == SurfaceRole.Plaza);
        }

        [Fact]
        public void PresetsLookDifferent()
        {
            var park = TransportTypeChecks.Preset(Id, "parkRide");
            var bus = TransportTypeChecks.Preset(Id, "busPlaza");
            var bike = TransportTypeChecks.Preset(Id, "bikeHub");
            var taxi = TransportTypeChecks.Preset(Id, "taxiRank");
            var fore = TransportTypeChecks.Preset(Id, "forecourt");
            Assert.True(park.Facts["capacity"] > 50, "park & ride has a car park");
            Assert.Contains(park.Nets, n => n.Role == NetRole.DriveAisle);
            Assert.False(bus.Facts.ContainsKey("capacity"));
            Assert.True(Stat(bike, "Citymaker.TransitPlaza.Stat.BikeRacks") >= 30);
            Assert.True(bike.Objects.Count(o => o.Category == AssetCategories.Shelter) >= 4, "bike shelters");
            Assert.True(Stat(taxi, "Citymaker.TransitPlaza.Stat.TaxiSpaces") >= 8);
            Assert.Equal(0, Stat(taxi, "Citymaker.TransitPlaza.Stat.BusBays"));
            Assert.True(Stat(bus, "Citymaker.TransitPlaza.Stat.BusBays") > Stat(fore, "Citymaker.TransitPlaza.Stat.BusBays"));
            Assert.Contains(fore.Objects, o => o.Role == ObjectRole.Centerpiece);
            Assert.DoesNotContain(bus.Objects, o => o.Role == ObjectRole.Centerpiece);
        }

        [Fact]
        public void TreeLayoutsDiffer()
        {
            int Trees(string layout)
            {
                var values = TestKit.Gen(Id).Defaults();
                values.Values["treeLayout"] = layout;
                var plan = TransportTypeChecks.Compose(TransportTypeChecks.With(Id, values), TransportTypeChecks.FiveHa, TransportTypeChecks.Road(TransportTypeChecks.FiveHa));
                return plan.Layers[0].Objects.Count(o => o.Role == ObjectRole.Tree);
            }
            int grid = Trees("grid"), rows = Trees("rows"), islands = Trees("islands"), none = Trees("none");
            Assert.Equal(0, none);
            Assert.True(grid > 0 && rows > 0 && islands > 0);
            Assert.NotEqual(grid, rows);
        }

        [Fact]
        public void WithoutStreetItStillFacesTheLongestEdge()
        {
            var plan = TransportTypeChecks.Compose(TransportTypeChecks.With(Id), TransportTypeChecks.SixHaL);
            Assert.True(string.IsNullOrEmpty(TransportTypeChecks.Errors(plan)), TransportTypeChecks.Errors(plan));
            Assert.True(TestKit.Stat(plan.Layers[0], "Citymaker.TransitPlaza.Stat.BusBays") > 0);
        }

        [Fact]
        public void TinyAreaDegradesWithoutErrors()
        {
            var ring = TestKit.Rect(30, 25);
            var plan = TransportTypeChecks.Compose(TransportTypeChecks.With(Id), ring, TransportTypeChecks.Road(ring));
            Assert.True(string.IsNullOrEmpty(TransportTypeChecks.Errors(plan)), TransportTypeChecks.Errors(plan));
            TransportTypeChecks.AssertInside(plan.Layers[0], ring);
        }

        [Fact]
        public void MissingCategoriesDegradeToInfo() => TransportTypeChecks.MissingCategoriesDegradeToInfo(Id, new Dictionary<string, int>
        {
            [AssetCategories.TransportShelter] = 0, [AssetCategories.Shelter] = 0, [AssetCategories.BikeRack] = 0, [AssetCategories.Wayfinding] = 0,
            [AssetCategories.Bollard] = 0, [AssetCategories.DecalMarkings] = 0, [AssetCategories.CenterpieceStatue] = 0, [AssetCategories.Centerpiece] = 0,
            [AssetCategories.Kiosk] = 0,
        });
    }
}
