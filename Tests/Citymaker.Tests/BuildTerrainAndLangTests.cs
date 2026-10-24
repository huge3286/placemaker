using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Citymaker.Core;
using Citymaker.Core.Build;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    public class BuildTerrainAndLangTests
    {
        private static readonly float2[] Square =
        {
            new float2(0, 0), new float2(100, 0), new float2(100, 60), new float2(0, 60),
        };

        [Fact]
        public void DabsStayInsideWithInset()
        {
            var dabs = TerraformDabs.Dabs(Square, 12f);
            Assert.NotEmpty(dabs);
            foreach (var d in dabs)
            {
                Assert.True(ReservedSpace.PointInPolygon(d, Square));
                Assert.True(TerraformDabs.DistanceToBoundary(Square, d) >= 12f * 0.7f - 1e-3f);
            }
        }

        [Fact]
        public void ThinShapesStillGetADab()
        {
            var thin = new[] { new float2(0, 0), new float2(50, 0), new float2(50, 3), new float2(0, 3) };
            Assert.NotEmpty(TerraformDabs.Dabs(thin, 12f));
            Assert.Empty(TerraformDabs.Dabs(new float2[0], 12f));
        }

        [Fact]
        public void AnchorIsDeepestInside()
        {
            var a = TerraformDabs.Anchor(Square, out float r);
            Assert.True(math.distance(a, new float2(50, 30)) < 8f);
            Assert.True(r > 25f && r <= 30f);
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "citymaker.csproj"))) dir = dir.Parent;
            return dir?.FullName;
        }

        [Fact]
        public void EveryBuildKeyUsedInCodeHasEnglishText()
        {
            var root = RepoRoot();
            Assert.NotNull(root);
            var lang = File.ReadAllText(Path.Combine(root, "Lang", "en-US.build.json"));
            var sources = Directory.GetFiles(Path.Combine(root, "Game"), "*.cs", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(Path.Combine(root, "Core", "Build"), "*.cs"))
                .Where(f => f.Replace('\\', '/').Contains("/Game/Build/") || f.Replace('\\', '/').Contains("/Game/Groups/")
                            || f.Replace('\\', '/').Contains("/Core/Build/"));
            var keyPattern = new Regex("\"(Citymaker\\.(?:Build|Group)\\.[A-Za-z]+)\"");
            foreach (var file in sources)
                foreach (Match m in keyPattern.Matches(File.ReadAllText(file)))
                    Assert.True(lang.Contains("\"" + m.Groups[1].Value + "\""), m.Groups[1].Value + " missing in en-US.build.json (" + Path.GetFileName(file) + ")");
            foreach (var t in new[] { "OverlapExisting", "NotEnoughMoney", "SteepSlope" })
                Assert.Contains("\"" + BuildErrorKeys.For(t) + "\"", lang);
            Assert.Contains("\"" + BuildErrorKeys.Other + "\"", lang);
        }
    }
}
