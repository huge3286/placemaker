using System.Collections.Generic;
using System.Threading;
using Citymaker.Core;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    public class ComposerTests
    {
        [Fact]
        public void UnknownLayerReportsErrorInsteadOfThrowing()
        {
            var recipe = new Recipe { Root = new RecipeStep { Layer = "does-not-exist" } };
            var area = new AreaShape { Ring = new[] { new float3(0, 0, 0), new float3(10, 0, 0), new float3(10, 0, 10) } };
            var plan = Composer.Compose(recipe, area, new SiteContext(), CancellationToken.None);
            Assert.True(plan.HasErrors);
        }

        [Fact]
        public void SeedsAreStable()
        {
            Assert.Equal(Seeds.Hash("park"), Seeds.Hash("park"));
            Assert.NotEqual(Seeds.Mix(1, 2), Seeds.Mix(2, 1));
        }

        [Fact]
        public void ReservedSpaceBlocksDiscsNearCorridors()
        {
            var r = new ReservedSpace();
            r.AddCorridor(new float2(0, 0), new float2(10, 0), 1f);
            Assert.True(r.Blocks(new float2(5, 1.5f), 1f));
            Assert.False(r.Blocks(new float2(5, 3f), 1f));
        }
    }
}
