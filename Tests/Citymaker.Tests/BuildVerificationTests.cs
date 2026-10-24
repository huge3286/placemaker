using Citymaker.Core.Build;
using Xunit;

namespace Citymaker.Tests
{
    public class BuildVerificationTests
    {
        private static MemberCounts Counts(int objects, int areas, int edges, int nodes)
        {
            var c = new MemberCounts();
            c.Add(MemberKind.Object, objects);
            c.Add(MemberKind.Area, areas);
            c.Add(MemberKind.Edge, edges);
            c.Add(MemberKind.Node, nodes);
            return c;
        }

        [Fact]
        public void ConsistentBuildHasNoIssues()
        {
            var planned = new PlannedCounts { Objects = 100, OptionalDropped = 10, Areas = 5, NetPieces = 12 };
            Assert.Empty(BuildVerification.Compare(planned, Counts(95, 5, 20, 18)));
        }

        [Fact]
        public void UntaggedObjectsAreReported()
        {
            var planned = new PlannedCounts { Objects = 100, OptionalDropped = 0, Areas = 5, NetPieces = 0 };
            var issues = BuildVerification.Compare(planned, Counts(80, 5, 0, 0));
            Assert.Single(issues);
            Assert.Contains("20 missing", issues[0]);
        }

        [Fact]
        public void ExtraMembersAndNetsWithoutNodesAreReported()
        {
            var planned = new PlannedCounts { Objects = 10, Areas = 2, NetPieces = 3 };
            var issues = BuildVerification.Compare(planned, Counts(12, 3, 4, 0));
            Assert.Equal(3, issues.Count);
        }

        [Fact]
        public void PlannedNetsWithoutEdgesAreReported()
        {
            var planned = new PlannedCounts { NetPieces = 3 };
            Assert.Single(BuildVerification.Compare(planned, Counts(0, 0, 0, 0)));
        }

        [Fact]
        public void DoubledBuildIsDetectedSmallAdoptionsAreNot()
        {
            var planned = new PlannedCounts { Objects = 532, Areas = 2, NetPieces = 60 };
            Assert.True(BuildVerification.Duplicated(planned, Counts(1066, 4, 140, 128), out var why));
            Assert.Contains("1066", why);
            Assert.False(BuildVerification.Duplicated(planned, Counts(534, 2, 70, 60), out _));
            Assert.True(BuildVerification.Duplicated(new PlannedCounts { Objects = 10, Areas = 2 }, Counts(10, 6, 0, 0), out _));
            Assert.False(BuildVerification.Duplicated(new PlannedCounts { Objects = 0, Areas = 0 }, Counts(3, 1, 0, 0), out _));
        }

        [Fact]
        public void CountsAddByKind()
        {
            var c = new MemberCounts();
            c.Add(MemberKind.Auxiliary, 2);
            c.Add(MemberKind.Other);
            c.Add(MemberKind.Edge, 4);
            Assert.Equal(3, c.Auxiliary);
            Assert.Equal(7, c.Total);
        }
    }
}
