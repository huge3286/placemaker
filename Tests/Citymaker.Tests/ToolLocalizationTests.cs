using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Citymaker.Core.Tool;
using Xunit;

namespace Citymaker.Tests
{
    public class ToolLocalizationTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "citymaker.csproj"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return dir.FullName;
        }

        [Fact]
        public void EveryKeyTheToolUsesHasEnglishText()
        {
            string root = RepoRoot();
            var keys = new HashSet<string>();
            foreach (var f in Directory.GetFiles(Path.Combine(root, "Lang"), "en-US*.json"))
                foreach (var kv in JsonValue.Parse(File.ReadAllText(f)).Properties) keys.Add(kv.Key);

            var sources = new List<string>();
            foreach (var d in new[] { "Core/Tool", "Game/Tool", "Game/UI", "Game/Overlay", "Game/Selection", "Game/Preview", "Game/Recipes", "Game/Settings", "Game/Localization" })
            {
                var p = Path.Combine(root, d);
                if (Directory.Exists(p)) sources.AddRange(Directory.GetFiles(p, "*.cs", SearchOption.AllDirectories));
            }
            // Complete literal keys only ("Citymaker.Hint.Undo"); prefixes ending in '.' are built dynamically.
            var literal = new Regex("\"(Citymaker\\.[A-Za-z]+\\.[A-Za-z0-9_.]*[A-Za-z0-9_])\"");
            var missing = new List<string>();
            foreach (var s in sources)
                foreach (Match m in literal.Matches(File.ReadAllText(s)))
                    if (!keys.Contains(m.Groups[1].Value)) missing.Add(m.Groups[1].Value + " (" + Path.GetFileName(s) + ")");
            Assert.Empty(missing);
        }
    }
}
