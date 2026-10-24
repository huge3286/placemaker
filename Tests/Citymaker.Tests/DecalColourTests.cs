// s28: soft colour filter - automatic decals must not clearly clash with the ground they lie on.

using Citymaker.Core.Decals;
using Xunit;

namespace Citymaker.Tests
{
    public class DecalColourTests
    {
        [Theory]
        [InlineData("Ground Texture Decal Pack Ground Moss01 Decal", HostTone.Lawn, true)]
        [InlineData("Ground Texture Decal Pack Ground Dirt01 Decal", HostTone.Lawn, false)]          // dark brown disc on grass
        [InlineData("G87 Trash Decals Dirty Pack Trash G87 Dust 02 Brown Decal", HostTone.Lawn, false)]   // orange-brown on grass
        [InlineData("G87 Trash Decals Dirty Pack Trash G87 Dust 02 Brown Decal", HostTone.Sand, true)]
        [InlineData("G87 Stains and Puddles Decals Wet Pack Stains G87 Oil 2 Decal", HostTone.Paving, false)]   // black blot on light paving
        [InlineData("G87 Stains and Puddles Decals Wet Pack Stains G87 Oil 2 Decal", HostTone.Asphalt, true)]   // darker than asphalt is fine
        [InlineData("G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 05 B M Decal", HostTone.Paving, true)]
        [InlineData("Fallen leaves decals Leaf Dead Leaves8 Brown light Decal", HostTone.Lawn, true)]   // sparse specks
        public void ClearClashesAreDropped(string name, HostTone host, bool ok)
        {
            Assert.True(DecalColours.TryGet(name, out _), name);
            Assert.Equal(ok, DecalColours.NoClash(name, host));
        }

        [Fact]
        public void UnknownColoursPassTheSoftFilter()
        {
            Assert.False(DecalColours.TryGet("Some Unmeasured Pack Thing Decal", out _));
            Assert.True(DecalColours.NoClash("Some Unmeasured Pack Thing Decal", HostTone.Lawn));
        }

        [Fact]
        public void LawnWearNeverTakesBrownDirt()
        {
            Assert.False(DecalPools.Eligible(DecalPools.Wear, "Ground Texture Decal Pack Ground Dirt01 Decal"));
            Assert.Equal(HostTone.Lawn, DecalPools.Of(DecalPools.Wear).Host);
        }
    }
}
