using Citymaker.Core;
using Citymaker.Core.Build;
using Citymaker.Core.Layers.AddOns;
using Citymaker.Core.Layers.Roads;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    /// <summary>"A grid of roads is just roads": loose layers build ordinary city content (no group).</summary>
    public class BuildLooseTests
    {
        private static AreaPlan Roads(string gen)
        {
            var l = new AreaPlan { GeneratorId = gen };
            l.Nets.Add(new PlannedNet { A = 0, B = 1, C = 2, D = new float3(30, 0, 0), Role = NetRole.Street });
            return l;
        }

        [Fact]
        public void RoadsAndEdgeZoningAreLooseOthersAreNot()
        {
            Assert.True(GeneratorRegistry.IsLoose(RoadsLayer.LayerId));
            Assert.True(GeneratorRegistry.IsLoose(EdgeZoningAddOn.LayerId));
            Assert.False(GeneratorRegistry.IsLoose("parking"));
            Assert.False(GeneratorRegistry.IsLoose(""));
            Assert.False(GeneratorRegistry.IsLoose("no-such-generator"));
            int grouped = 0;
            foreach (var g in GeneratorRegistry.All) if (!GeneratorRegistry.IsLoose(g.Id)) grouped++;
            Assert.True(grouped > 10);   // only the road network is loose
        }

        [Fact]
        public void RoadOnlyPlanIsALooseBuild()
        {
            var plan = new ComposedPlan();
            plan.Layers.Add(Roads(RoadsLayer.LayerId));
            var z = new AreaPlan { GeneratorId = EdgeZoningAddOn.LayerId };
            z.Zoning.Add(new PlannedZoning { ZoneId = "Residential Low" });
            plan.Layers.Add(z);
            var w = PlanWork.Flatten(plan);
            Assert.True(w.AllLoose);
            Assert.All(w.All(), item => Assert.True(item.Loose));
        }

        [Fact]
        public void MixedPlanGroupsOnlyNonLooseLayers()
        {
            var plan = new ComposedPlan();
            plan.Layers.Add(Roads(RoadsLayer.LayerId));
            var park = new AreaPlan { GeneratorId = "parking" };
            park.Objects.Add(new PlannedObject { Position = new float3(1, 0, 1), Role = ObjectRole.Tree });
            plan.Layers.Add(park);
            var w = PlanWork.Flatten(plan);
            Assert.True(w.AnyGrouped);
            Assert.True(w.Nets[0].Loose);
            Assert.False(w.Objects[0].Loose);
        }

        [Fact]
        public void EmptyGroupedLayerDoesNotTurnRoadsIntoAGroup()
        {
            var plan = new ComposedPlan();
            plan.Layers.Add(Roads(RoadsLayer.LayerId));
            plan.Layers.Add(new AreaPlan { GeneratorId = "parking" });   // nothing to build
            Assert.True(PlanWork.Flatten(plan).AllLoose);
        }

        [Fact]
        public void LooseNetsWithoutNodesAreNotAMismatch()
        {
            var counts = new MemberCounts();
            counts.Add(MemberKind.Edge, 12);
            var planned = new PlannedCounts { NetPieces = 12 };
            Assert.Empty(BuildVerification.Compare(planned, counts, looseNets: true));
            Assert.Single(BuildVerification.Compare(planned, counts));
        }
    }
}
