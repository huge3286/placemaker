using System;
using System.Globalization;
using System.Threading;
using Citymaker.Core;
using Citymaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    public class ToolRecipeJsonTests
    {
        [Fact]
        public void JsonParsesTolerantInput()
        {
            var v = JsonValue.Parse("{ // comment\n \"a\": [1, 2.5, -3e2, ], /* block */ \"b\": \"x\\\"y\\u00e9\", \"c\": null, \"d\": true, }");
            Assert.Equal(3, v["a"].Count);
            Assert.Equal(-300, v["a"].Items[2].AsNumber());
            Assert.Equal("x\"yé", v["b"].AsString());
            Assert.True(v["c"].IsNull);
            Assert.True(v["d"].AsBool());
            Assert.True(v["missing"]["deeper"].IsNull);
        }

        [Fact]
        public void JsonRejectsGarbageWithPosition()
        {
            Assert.False(JsonValue.TryParse("{\"a\": }", out _, out var err));
            Assert.Contains("line 1", err);
            Assert.False(JsonValue.TryParse("[1, 2", out _, out _));
            Assert.False(JsonValue.TryParse("{} trailing", out _, out _));
        }

        [Fact]
        public void JsonWritesInvariantNumbersRegardlessOfCulture()
        {
            var old = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                var o = JsonValue.Obj().Set("f", 1.5).Set("i", 3).Set("nan", double.NaN).Set("s", "a\nb");
                Assert.Equal("{\"f\":1.5,\"i\":3,\"nan\":null,\"s\":\"a\\nb\"}", o.ToString());
            }
            finally { Thread.CurrentThread.CurrentCulture = old; }
        }

        [Fact]
        public void JsonRoundTripsThroughPrettyOutput()
        {
            var o = JsonValue.Obj().Set("a", JsonValue.Arr().Add(1).Add("x")).Set("b", JsonValue.Obj().Set("c", false));
            var back = JsonValue.Parse(o.Write(true));
            Assert.Equal(o.ToString(), back.ToString());
        }

        private static Recipe Sample()
        {
            var r = new Recipe { Id = "roads-with-parks", NameKey = "Citymaker.Recipe.RoadsWithParks", Icon = "coui://x.svg" };
            r.Root.Layer = "roads";
            r.Root.Seed = 12345;
            r.Root.Settings.Values["spacing"] = 90.5;
            r.Root.Settings.Values["culDeSacs"] = true;
            r.Root.Settings.Values["street"] = "Small Road";
            r.Root.Settings.Values["trees"] = new[] { "Oak01", "Birch02" };
            r.Root.Locked.Add("spacing");
            r.Root.Markers.Add(new EdgeMarker { Kind = "entrance", RingSegment = 2, RingT = 0.25f, Position = new float3(1.5f, 2, -3.25f) });
            var park = new RecipeStep { Layer = "park", Target = "block" };
            park.Children.Add(new RecipeStep { Layer = "playground", Enabled = false });
            r.Root.Children.Add(park);
            r.Root.RegionOverrides["block:3"] = new RecipeStep { Layer = "plaza" };
            return r;
        }

        [Fact]
        public void RecipeRoundTripPreservesEverything()
        {
            var r = Sample();
            var text = RecipeJson.Write(r, "Citymaker.Recipe.RoadsWithParks.Desc", 40).Write(true);
            var file = RecipeJson.Parse(text);
            var b = file.Recipe;
            Assert.Equal("Citymaker.Recipe.RoadsWithParks.Desc", file.DescriptionKey);
            Assert.Equal(40, file.Order);
            Assert.Equal(r.Id, b.Id);
            Assert.Equal(r.NameKey, b.NameKey);
            Assert.Equal(r.Icon, b.Icon);
            Assert.Equal("roads", b.Root.Layer);
            Assert.Equal(12345u, b.Root.Seed);
            Assert.True(r.Root.Settings.ContentEquals(b.Root.Settings));
            Assert.Contains("spacing", b.Root.Locked);
            Assert.Single(b.Root.Markers);
            Assert.Equal("entrance", b.Root.Markers[0].Kind);
            Assert.Equal(2, b.Root.Markers[0].RingSegment);
            Assert.Equal(-3.25f, b.Root.Markers[0].Position.z);
            Assert.Equal("park", b.Root.Children[0].Layer);
            Assert.Equal("block", b.Root.Children[0].Target);
            Assert.False(b.Root.Children[0].Children[0].Enabled);
            Assert.Equal("plaza", b.Root.RegionOverrides["block:3"].Layer);
            // Second round trip is byte-identical (stable key order).
            Assert.Equal(text, RecipeJson.Write(b, file.DescriptionKey, file.Order).Write(true));
        }

        [Fact]
        public void RecipeReaderAcceptsNestedRootAndIgnoresUnknownFields()
        {
            var file = RecipeJson.Parse("{\"formatVersion\": 9, \"id\": \"p\", \"future\": {\"x\":1}, \"root\": {\"layer\": \"park\", \"settings\": {\"a\": 1, \"weird\": {\"n\":1}}, \"alsoFuture\": 3}}");
            Assert.Equal("park", file.Recipe.Root.Layer);
            Assert.Equal(9, file.FormatVersion);
            Assert.Equal(1.0, file.Recipe.Root.Settings.Values["a"]);
            Assert.False(file.Recipe.Root.Settings.Values.ContainsKey("weird"));
        }

        [Fact]
        public void RecipeWithoutLayerIsRejected()
        {
            Assert.Throws<FormatException>(() => RecipeJson.Parse("{\"id\": \"x\"}"));
            Assert.Throws<FormatException>(() => RecipeJson.Parse("[1]"));
        }

        [Fact]
        public void MinimalHandWrittenRecipeLoads()
        {
            var file = RecipeJson.Parse("{\"id\":\"park\",\"nameKey\":\"Citymaker.Recipe.Park\",\"layer\":\"park\"}");
            Assert.Equal("park", file.Recipe.Root.Layer);
            Assert.True(file.Recipe.Root.Enabled);
            Assert.Empty(file.Recipe.Root.Children);
            Assert.Equal(1000, file.Order);
        }

        [Fact]
        public void ShippedRecipeFilesParseWithTheToolReader()
        {
            var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "Recipes"))) dir = dir.Parent;
            Assert.NotNull(dir);
            var files = System.IO.Directory.GetFiles(System.IO.Path.Combine(dir.FullName, "Recipes"), "*.json");
            Assert.NotEmpty(files);
            foreach (var f in files)
            {
                var file = RecipeJson.Parse(System.IO.File.ReadAllText(f));
                Assert.Equal(System.IO.Path.GetFileNameWithoutExtension(f), file.Recipe.Id);
                Assert.False(string.IsNullOrEmpty(file.Recipe.Root.Layer));
                Assert.True(file.FormatVersion <= RecipeJson.CurrentFormatVersion);
                // Writing and reading back keeps the tree.
                var back = RecipeJson.Parse(RecipeJson.Write(file.Recipe, file.DescriptionKey).ToString()).Recipe;
                Assert.Equal(RecipeJson.WriteStep(file.Recipe.Root).ToString(), RecipeJson.WriteStep(back.Root).ToString());
            }
        }

        [Fact]
        public void DefaultsFileRoundTrip()
        {
            var d = new System.Collections.Generic.Dictionary<string, SettingsBag>();
            var b = new SettingsBag();
            b.Values["size"] = 10.0;
            b.Values["style"] = "wild";
            d["park"] = b;
            var back = RecipeJson.ReadDefaults(JsonValue.Parse(RecipeJson.WriteDefaults(d).ToString()));
            Assert.True(back["park"].ContentEquals(b));
        }

        [Fact]
        public void PlayerRecipeIdsAreFileSafe()
        {
            var id = RecipeJson.NewPlayerRecipeId("My Grid! / ÄÖ", 0xABCDEF);
            Assert.Equal("player-my-grid-00abcdef", id);
            Assert.StartsWith("player-recipe-", RecipeJson.NewPlayerRecipeId("", 1));
        }
    }
}
