// s37: see-through clone names must be safe for the CS2 asset database (EscapeUtils treats '%' as an escape
// character and reserves <>:"/\|?*%.), or ManagedBatchSystem.CreateMaterial throws every frame.

using System.Linq;
using Citymaker.Core.Decals;
using Xunit;

namespace Citymaker.Tests
{
    public class DecalNameTests
    {
        private static bool Safe(string s) => s.All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == ' ' || c == '-' || c == '_');

        [Theory]
        [InlineData("Ground Texture Decal Pack Ground Moss01 Decal")]
        [InlineData("G87 Stains and Puddles Decals Wet Pack Puddles G87 Wet Effect 092 B 50 Medium Decal")]
        [InlineData("Cracks and Damage Decal Pack RoadAssets Crack - Small 2 Decal")]
        [InlineData("Weird 100% (copy) v1.2 <a|b> \"q\" C:\\x/y?*#")]
        [InlineData("Décor ünïcode – dash")]
        [InlineData("%%%")]
        [InlineData("")]
        public void CloneNamesOnlyUseSafeCharacters(string original)
        {
            foreach (int tier in DecalLook.Tiers)
            {
                var name = DecalLook.CloneName(original, tier);
                Assert.True(Safe(name), name);
                Assert.StartsWith(DecalLook.ClonePrefix + tier + " - ", name);
                Assert.DoesNotContain("  ", name);
            }
        }

        [Fact]
        public void SafeNamesAreKeptAndUnsafeOnesStayDistinct()
        {
            const string ok = "Ground Texture Decal Pack Ground Moss01 Decal";
            Assert.Equal(ok, DecalLook.SafeAssetName(ok));
            Assert.Equal("Citymaker Decal 15 - " + ok, DecalLook.CloneName(ok, 15));
            // Sanitising is idempotent and two originals differing only in unsafe characters never share a name.
            var a = DecalLook.SafeAssetName("Leaf.Pile 1");
            Assert.Equal(a, DecalLook.SafeAssetName(a));
            Assert.NotEqual(a, DecalLook.SafeAssetName("Leaf Pile 1"));
            Assert.NotEqual(DecalLook.SafeAssetName("A%B"), DecalLook.SafeAssetName("A.B"));
        }

        [Fact]
        public void LegacyNameIsTheOldScheme()
        {
            Assert.Equal("Citymaker Decal 15% (Ground Texture Decal Pack Ground Moss01 Decal)",
                DecalLook.LegacyCloneName("Ground Texture Decal Pack Ground Moss01 Decal", 15));
        }
    }
}
