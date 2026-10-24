using System.Collections.Generic;
using System.Linq;
using Citymaker.Core;
using Citymaker.Core.Build;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    /// <summary>Declutter with synthetic footprints (bounds as in the real catalog dump, playtest 4).</summary>
    public class BuildDeclutterTests
    {
        private static readonly Dictionary<string, AssetInfo> Assets = new Dictionary<string, AssetInfo>
        {
            ["Hedge"] = Box("Hedge", -0.52f, 0f, -2.04f, 0.54f, 1.09f, 2.04f),          // HedgePieceLow01: long along local Z
            ["Apple"] = Box("Apple", -1.34f, 0f, -1.32f, 1.34f, 5.72f, 1.32f),          // AppleTree01 (collision core)
            ["Bush"] = Box("Bush", -0.82f, 0f, -0.77f, 0.82f, 0.87f, 0.77f),            // FlowerBushWildChild02
            ["Bench"] = Box("Bench", -0.75f, 0f, -0.38f, 0.75f, 1.13f, 0.38f),          // CampgroundBench01
            ["Leaves"] = Box("Leaves", -3f, -0.5f, -3f, 3f, 0.5f, 3f),                  // leaves decal
            ["Fountain"] = Box("Fountain", -2.01f, 0f, -1.81f, 2.01f, 3.19f, 1.81f),    // SmallFountain01
            ["Path"] = new AssetInfo { Id = "Path", NetWidth = 3f },
        };

        private static AssetInfo Box(string id, float x0, float y0, float z0, float x1, float y1, float z1) =>
            new AssetInfo { Id = id, BoundsMin = new float3(x0, y0, z0), BoundsMax = new float3(x1, y1, z1) };

        private static PlannedObject Obj(ObjectRole role, string id, float x, float z, float yaw = 0f, string cat = null) =>
            new PlannedObject { Role = role, Asset = new AssetRef(id), Position = new float3(x, 0, z), Yaw = yaw, Category = cat };

        private static ComposedPlan Plan(params PlannedObject[] objects)
        {
            var l = new AreaPlan { GeneratorId = "test" };
            l.Objects.AddRange(objects);
            var p = new ComposedPlan();
            p.Layers.Add(l);
            return p;
        }

        private static List<Declutter.Footprint> Footprints(ComposedPlan p)
        {
            var r = new List<Declutter.Footprint>();
            foreach (var o in p.Layers[0].Objects)
                if (Declutter.TryFootprint(o, Assets, out var f)) r.Add(f);
            return r;
        }

        private static void AssertNoOverlaps(ComposedPlan p)
        {
            var f = Footprints(p);
            for (int i = 0; i < f.Count; i++)
                for (int j = i + 1; j < f.Count; j++)
                    Assert.False(Declutter.Overlaps(f[i], f[j], 0f), $"objects {i} and {j} still overlap");
        }

        [Fact]
        public void FootprintFollowsYaw()
        {
            Assert.True(Declutter.TryFootprint(new float3(10, 0, 5), math.PI / 2, 1f, Assets["Bench"].BoundsMin, Assets["Bench"].BoundsMax, out var f));
            // RotateY(90°): local +X points to world −Z, local +Z to world +X.
            Assert.Equal(0f, f.AxisX.x, 3); Assert.Equal(-1f, f.AxisX.y, 3);
            Assert.Equal(1f, f.AxisZ.x, 3); Assert.Equal(0f, f.AxisZ.y, 3);
            Assert.Equal(new float2(10, 5), f.Center);
            // A box 0.5 m to the side (+X world) overlaps only the rotated bench (depth 0.38 along X).
            Assert.True(Declutter.TryFootprint(new float3(10.8f, 0, 5), 0f, 1f, Assets["Bush"].BoundsMin, Assets["Bush"].BoundsMax, out var b));
            Assert.True(Declutter.Overlaps(f, b));
            Assert.True(Declutter.TryFootprint(new float3(11.3f, 0, 5), 0f, 1f, Assets["Bush"].BoundsMin, Assets["Bush"].BoundsMax, out b));
            Assert.False(Declutter.Overlaps(f, b));
        }

        [Fact]
        public void OverlappingHedgeRowIsRespacedOverTheSameSpan()
        {
            // 10 pieces (4.08 m long) every 3.8 m along +X: each overlaps its neighbour by ~0.3 m.
            var objs = Enumerable.Range(0, 10).Select(i => Obj(ObjectRole.Bush, "Hedge", i * 3.8f, 0f, math.PI / 2, AssetCategories.Hedge)).ToArray();
            var p = Plan(objs);
            var rep = Declutter.Run(p, Assets);
            var left = p.Layers[0].Objects;
            Assert.InRange(left.Count, 8, 9);
            Assert.Equal(10 - left.Count, rep.Dropped[ObjectRole.Bush]);
            AssertNoOverlaps(p);
            // Same span: the first and the last piece keep their places.
            var xs = left.Select(o => o.Position.x).OrderBy(x => x).ToList();
            Assert.Equal(0f, xs.First(), 3);
            Assert.Equal(9 * 3.8f, xs.Last(), 3);
            // Even spacing, never a gap wider than a piece.
            for (int i = 1; i < xs.Count; i++) Assert.InRange(xs[i] - xs[i - 1], 4.08f, 4.08f * 1.5f);
            var issue = Assert.Single(p.Issues);
            Assert.Equal(Declutter.IssueKey(ObjectRole.Bush), issue.Key);
            Assert.Equal(10 - left.Count, (int)issue.Args[0]);
        }

        [Theory]
        [InlineData(10f, 4f, 2)]
        [InlineData(8.1f, 4f, 2)]
        [InlineData(8f, 4f, 1)]
        [InlineData(1f, 4f, 1)]
        [InlineData(41.2f, 4.12f, 9)]
        public void PropFencePiecesNeverOverlap(float len, float piece, int expected)
        {
            int n = Citymaker.Core.Layers.Common.Fences.SpacedPieceCount(len, piece);
            Assert.Equal(expected, n);
            if (n > 1) Assert.True(len / n >= piece + Declutter.Gap - 1e-4f);
        }

        [Fact]
        public void HedgeRowWithoutOverlapIsUntouched()
        {
            var p = Plan(Enumerable.Range(0, 5).Select(i => Obj(ObjectRole.FencePiece, "Hedge", i * 4.2f, 0f, math.PI / 2)).ToArray());
            var rep = Declutter.Run(p, Assets);
            Assert.Equal(5, p.Layers[0].Objects.Count);
            Assert.Equal(0, rep.TotalDropped + rep.TotalMoved);
            Assert.Empty(p.Issues);
        }

        [Fact]
        public void BushesUnderATreeCanopyAreLegalButNotOnTheTrunk()
        {
            var p = Plan(
                Obj(ObjectRole.Tree, "Apple", 0, 0),
                Obj(ObjectRole.Bush, "Bush", 2.4f, 0f),    // under the canopy, cores apart → legal
                Obj(ObjectRole.Bush, "Bush", 0.5f, 0.3f)); // on the trunk core → nudged or dropped
            var rep = Declutter.Run(p, Assets);
            var objs = p.Layers[0].Objects;
            Assert.Equal(new float3(0, 0, 0), objs[0].Position);            // the tree wins (higher tier)
            Assert.Equal(new float3(2.4f, 0, 0), objs[1].Position);         // the legal bush stays put
            Assert.Equal(1, rep.TotalMoved + rep.TotalDropped);
            AssertNoOverlaps(p);
        }

        [Fact]
        public void BenchesBesidePathsAreNudgedOffThePathOrDropped()
        {
            var p = Plan(
                Obj(ObjectRole.Bench, "Bench", 1.6f, 0f),   // depth ±0.38 along X: pokes 0.48 m into the 3 m path
                Obj(ObjectRole.Bench, "Bench", 3f, 6f),     // clear of the path
                Obj(ObjectRole.Bench, "Bench", 0f, -6f));   // in the middle of the path: cannot be saved
            p.Layers[0].Nets.Add(new PlannedNet
            {
                Role = NetRole.Pathway, Asset = new AssetRef("Path"),
                A = new float3(0, 0, -20), B = new float3(0, 0, -7), C = new float3(0, 0, 7), D = new float3(0, 0, 20),
            });
            var rep = Declutter.Run(p, Assets);
            var objs = p.Layers[0].Objects;
            Assert.Equal(2, objs.Count);
            Assert.True(objs[0].Position.x >= 1.5f + 0.38f, "nudged bench clears the path edge");
            Assert.Equal(new float3(3f, 0, 6f), objs[1].Position);
            Assert.Equal(1, rep.Moved[ObjectRole.Bench]);
            var issue = Assert.Single(p.Issues);
            Assert.Equal(IssueSeverity.Warning, issue.Severity);
            Assert.Equal("Citymaker.Issue.Declutter.Bench", issue.Key);
            Assert.Equal(1, (int)issue.Args[0]);
        }

        [Fact]
        public void PriorityKeepsCenterpiecesAndFurnitureOverDecals()
        {
            var p = Plan(
                Obj(ObjectRole.Decal, "Leaves", 0, 0),       // first in plan order, but lowest tier
                Obj(ObjectRole.Bench, "Bench", 1f, 1f),
                Obj(ObjectRole.Centerpiece, "Fountain", -1f, -1f));
            var rep = Declutter.Run(p, Assets);
            var objs = p.Layers[0].Objects;
            Assert.Contains(objs, o => o.Role == ObjectRole.Centerpiece && o.Position.Equals(new float3(-1, 0, -1)));
            Assert.Contains(objs, o => o.Role == ObjectRole.Bench);
            // s21: decals take no space — the 6 m decal stays where it was, under the bench and fountain, with no issue.
            Assert.Contains(objs, o => o.Role == ObjectRole.Decal && o.Position.Equals(new float3(0, 0, 0)));
            Assert.DoesNotContain(p.Issues, i => i.Key == Declutter.IssueKey(ObjectRole.Decal));
        }

        [Fact]
        public void RunsOncePerPlanAndIgnoresUnknownAssetsAndMarkers()
        {
            var p = Plan(
                Obj(ObjectRole.Bush, "Bush", 0, 0),
                Obj(ObjectRole.Bush, "Bush", 0.2f, 0),
                Obj(ObjectRole.Bush, "Mystery", 0, 0),          // no catalog bounds: kept, never an obstacle
                Obj(ObjectRole.Marker, "Bush", 0, 0));          // markers never collide
            Declutter.Run(p, Assets);
            int count = p.Layers[0].Objects.Count, issues = p.Issues.Count;
            Assert.True(p.Decluttered);
            var again = Declutter.Run(p, Assets);
            Assert.True(again.Skipped);
            Assert.Equal(count, p.Layers[0].Objects.Count);
            Assert.Equal(issues, p.Issues.Count);
            Assert.Contains(p.Layers[0].Objects, o => o.Asset.Id == "Mystery");
            Assert.Contains(p.Layers[0].Objects, o => o.Role == ObjectRole.Marker);
        }

        [Fact]
        public void ObjectsIgnoreTheNetTheyAreAttachedTo()
        {
            var p = Plan(Obj(ObjectRole.Lamp, "Bench", 0f, 0f));
            p.Layers[0].Objects[0].AttachedNet = 0;
            p.Layers[0].Nets.Add(new PlannedNet { Role = NetRole.Pathway, Asset = new AssetRef("Path"), A = new float3(0, 0, -5), B = new float3(0, 0, -2), C = new float3(0, 0, 2), D = new float3(0, 0, 5) });
            var rep = Declutter.Run(p, Assets);
            Assert.Equal(0, rep.TotalDropped + rep.TotalMoved);
        }

        [Fact]
        public void DenseScatterEndsWithoutOverlapsAndIsDeterministic()
        {
            ComposedPlan Make()
            {
                var rng = new Random(7);
                var list = new List<PlannedObject>();
                for (int i = 0; i < 300; i++)
                    list.Add(Obj(i % 3 == 0 ? ObjectRole.Tree : ObjectRole.Bush, i % 3 == 0 ? "Apple" : "Bush",
                        rng.NextFloat(0, 30), rng.NextFloat(0, 30), rng.NextFloat(0, 6.28f)));
                return Plan(list.ToArray());
            }
            var a = Make(); var b = Make();
            var ra = Declutter.Run(a, Assets); Declutter.Run(b, Assets);
            AssertNoOverlaps(a);
            Assert.True(ra.TotalDropped > 0);
            Assert.Equal(a.Layers[0].Objects.Select(o => o.Position), b.Layers[0].Objects.Select(o => o.Position));
        }

        // s55: "Ignore collisions in my builds" on a lot with stall markings. The build gives every prop the parking carrier as owner,
        // so the game never pairs our own pieces: only a near-exact duplicate is moved or dropped (playtest 89: 85 bushes, lamps and trees lost).
        [Fact]
        public void AnarchyOnALotKeepsOverlappingPropsAndDropsOnlyDuplicates()
        {
            ComposedPlan Make() => Plan(
                Obj(ObjectRole.ParkingBay, "Bush", 0f, 0f),     // a "stall marking" (any asset with bounds)
                Obj(ObjectRole.Tree, "Apple", 1.2f, 0f),
                Obj(ObjectRole.Bush, "Bush", 2.6f, 0f),          // overlaps the tree's core
                Obj(ObjectRole.Bush, "Bush", 2.65f, 0.05f));     // the same spot as the bush before: a duplicate
            bool was = Declutter.Anarchy;
            try
            {
                Declutter.Anarchy = false;
                var off = Make(); var repOff = Declutter.Run(off, Assets);
                Assert.True(repOff.TotalDropped + repOff.TotalMoved >= 2);
                Declutter.Anarchy = true;
                var on = Make(); var repOn = Declutter.Run(on, Assets);
                Assert.Equal(1, repOn.TotalMoved + repOn.TotalDropped);   // only the duplicate (nudged off or dropped)
                Assert.Equal(new float3(1.2f, 0, 0), on.Layers[0].Objects.First(o => o.Role == ObjectRole.Tree).Position);
                Assert.Equal(new float3(2.6f, 0, 0), on.Layers[0].Objects.First(o => o.Role == ObjectRole.Bush).Position);
                // no markings on the plan: the old rule (the game would hide one of an overlapping pair)
                var plain = Plan(Obj(ObjectRole.Tree, "Apple", 0f, 0f), Obj(ObjectRole.Bush, "Bush", 0.5f, 0.3f));
                var repPlain = Declutter.Run(plain, Assets);
                Assert.Equal(0, repPlain.TotalMoved + repPlain.TotalDropped);   // s58: every build shares one group owner now, so the relaxed rule holds for every plan
                Declutter.Anarchy = false;
                var strict = Plan(Obj(ObjectRole.Tree, "Apple", 0f, 0f), Obj(ObjectRole.Bush, "Bush", 0.5f, 0.3f));
                var repStrict = Declutter.Run(strict, Assets);
                Assert.Equal(1, repStrict.TotalMoved + repStrict.TotalDropped);   // anarchy off: the game would hide one of an overlapping pair
            }
            finally { Declutter.Anarchy = was; }
        }
    }
}
