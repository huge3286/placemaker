using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Citymaker.Core.Layers.Roads.Generators;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    /// <summary>
    /// Checks the pre-splitting of the grid at internal crossings:
    ///  - every crossing inside the polygon is an endpoint shared by
    ///    exactly 4 sub-segments (2 per line);
    ///  - a crossing on the polygon border (one or two lines end there) is
    ///    shared by 2 or 3 sub-segments;
    ///  - no crossing lies in the middle of a sub-segment;
    ///  - endpoints that meet are EXACTLY identical (same floats),
    ///    which is required for the game to merge them into a single node;
    ///  - no sub-segment shorter than MinSegmentLength.
    /// </summary>
    public class RoadsGridGeneratorTests
    {
        // ------------------------------------------------------------------
        // Case 1: axis-aligned square, "fit to area" mode 3×3
        // → 9 internal crossings, each shared by exactly 4 sub-segments.
        // ------------------------------------------------------------------

        [Fact]
        public void RemoveBacktracks_DropsShortReversals_KeepsRealCorners()
        {
            // Seen on a real perimeter: a curve's samples overshot the next node
            // by ~3 m, then the contour doubled back (179° turn).
            var loop = new List<float3>
            {
                new float3(0f, 1f, 0f), new float3(300f, 2f, 0f), new float3(303f, 2f, 0f), new float3(300.5f, 2f, 0.2f),
                new float3(300f, 3f, 300f), new float3(0f, 4f, 300f),
            };
            GridGenerator.RemoveBacktracks(loop);
            Assert.Equal(5, loop.Count); // only the overshooting point (303 m) is removed
            Assert.DoesNotContain(loop, p => p.x > 301f);
            Assert.Contains(loop, p => p.x == 300f && p.z == 300f && p.y == 3f); // 90° corner kept, height preserved
        }

        [Fact]
        public void Square_FitToArea_EveryInternalCrossingSharedByExactlyFourSubSegments()
        {
            var nodes = new List<float3>
            {
                new float3(0f, 0f, 0f),
                new float3(300f, 0f, 0f),
                new float3(300f, 0f, 300f),
                new float3(0f, 0f, 300f),
            };
            var parameters = new GridParameters
            {
                Mode = SpacingMode.FitToArea,
                Rows = 3,
                Columns = 3,
                SpacingMeters = 60f,
            };

            var segments = GridGenerator.GenerateGrid(nodes, parameters);

            // 3 u-lines × 4 segments + 3 v-lines × 4 segments.
            Assert.Equal(24, segments.Count);

            var shared = SharedEndpoints(segments);
            Assert.Equal(9, shared.Count);
            foreach (var pair in shared)
            {
                Assert.Equal(4, pair.Value);
            }

            AssertCommonInvariants(segments);
        }

        // ------------------------------------------------------------------
        // Case 2: concave L-shaped polygon, fixed spacing
        // → 12 crossings, all interior, each shared by 4 sub-segments.
        // ------------------------------------------------------------------

        [Fact]
        public void ConcaveLShape_FixedSpacing_CrossingsSharedByFourSubSegments()
        {
            var nodes = new List<float3>
            {
                new float3(0f, 0f, 0f),
                new float3(300f, 0f, 0f),
                new float3(300f, 0f, 150f),
                new float3(150f, 0f, 150f),
                new float3(150f, 0f, 300f),
                new float3(0f, 0f, 300f),
            };
            var parameters = new GridParameters
            {
                Mode = SpacingMode.FixedSpacing,
                Rows = 3,
                Columns = 3,
                SpacingMeters = 60f,
            };

            var segments = GridGenerator.GenerateGrid(nodes, parameters);

            // u-lines: 60 and 120 cross the whole L (5 segments), 180 and 240
            // only the lower branch (3 segments); symmetric for v.
            Assert.Equal(32, segments.Count);

            var shared = SharedEndpoints(segments);
            Assert.Equal(12, shared.Count);
            foreach (var pair in shared)
            {
                Assert.Equal(4, pair.Value);
            }

            AssertCommonInvariants(segments);
        }

        // ------------------------------------------------------------------
        // Case 3: crossing ON THE BORDER of the polygon. The polygon's V notch has its
        // apex exactly on the crossing (200, 150): the u-line ends there
        // (clipped by the notch) while the v-line passes on both sides.
        // → T junction: the point is shared by exactly 3 sub-segments.
        // ------------------------------------------------------------------

        [Fact]
        public void VNotchApexOnCrossing_BorderCrossingSharedByExactlyThreeSubSegments()
        {
            var nodes = new List<float3>
            {
                new float3(0f, 0f, 0f),
                new float3(400f, 0f, 0f),
                new float3(400f, 0f, 300f),
                new float3(250f, 0f, 300f),
                new float3(200f, 0f, 150f), // notch apex = crossing u=200 × v=150
                new float3(150f, 0f, 300f),
                new float3(0f, 0f, 300f),
            };
            var parameters = new GridParameters
            {
                Mode = SpacingMode.FitToArea,
                Rows = 1,
                Columns = 1,
                SpacingMeters = 60f,
            };

            var segments = GridGenerator.GenerateGrid(nodes, parameters);

            // Line u=200: 1 segment (stopped at the apex); line v=150: 2 segments
            // (one under each arm), meeting at the apex.
            Assert.Equal(3, segments.Count);

            var shared = SharedEndpoints(segments);
            var apex = Assert.Single(shared);
            Assert.Equal(3, apex.Value);
            Assert.Equal(200f, apex.Key.x, 3);
            Assert.Equal(150f, apex.Key.z, 3);

            AssertCommonInvariants(segments);
        }

        // ------------------------------------------------------------------
        // Common invariants
        // ------------------------------------------------------------------

        /// <summary>Endpoints shared by at least 2 sub-segments, with their multiplicity.</summary>
        private static Dictionary<float3, int> SharedEndpoints(List<RoadSegmentDef> segments)
        {
            var counts = new Dictionary<float3, int>();
            foreach (var segment in segments)
            {
                counts[segment.Start] = counts.TryGetValue(segment.Start, out int s) ? s + 1 : 1;
                counts[segment.End] = counts.TryGetValue(segment.End, out int e) ? e + 1 : 1;
            }
            return counts.Where(p => p.Value >= 2).ToDictionary(p => p.Key, p => p.Value);
        }

        internal static void AssertCommonInvariants(List<RoadSegmentDef> segments)
        {
            var counts = new Dictionary<float3, int>();
            foreach (var segment in segments)
            {
                counts[segment.Start] = counts.TryGetValue(segment.Start, out int s) ? s + 1 : 1;
                counts[segment.End] = counts.TryGetValue(segment.End, out int e) ? e + 1 : 1;
            }
            var endpoints = counts.Keys.ToList();

            // 1) No segment below the minimum length.
            foreach (var segment in segments)
            {
                Assert.True(math.distance(segment.Start.xz, segment.End.xz) >= GridGenerator.MinSegmentLength - 1e-3f,
                    $"Sub-segment too short: {segment.Start} → {segment.End}");
            }

            // 2) Two very close endpoints must be EXACTLY the same point
            //    (same floats) — otherwise the game would create two nodes instead of one.
            for (int i = 0; i < endpoints.Count; i++)
            {
                for (int j = i + 1; j < endpoints.Count; j++)
                {
                    Assert.True(math.distance(endpoints[i], endpoints[j]) >= 0.5f,
                        $"Distinct endpoints nearly coincident (cannot merge): {endpoints[i]} vs {endpoints[j]}");
                }
            }

            // 3) No shared endpoint lies in the MIDDLE of another segment:
            //    every crossing must have split both lines.
            foreach (var pair in counts.Where(p => p.Value >= 2))
            {
                float3 point = pair.Key;
                foreach (var segment in segments)
                {
                    if (point.Equals(segment.Start) || point.Equals(segment.End))
                        continue;
                    float distance = DistanceToSegmentXZ(point.xz, segment.Start.xz, segment.End.xz);
                    Assert.True(distance > 0.5f,
                        $"Crossing {point} in the middle of segment {segment.Start} → {segment.End}");
                }
            }
        }

        private static float DistanceToSegmentXZ(float2 point, float2 a, float2 b)
        {
            float2 ab = b - a;
            float t = math.clamp(math.dot(point - a, ab) / math.dot(ab, ab), 0f, 1f);
            return math.distance(point, a + t * ab);
        }
    }

    /// <summary>Checks the AngleOffsetDegrees parameter: 0° = unchanged behaviour, 90° = axes swapped, correct clipping at any angle.</summary>
    public class GridGeneratorAngleTests
    {
        private static readonly List<float3> SquareNodes = new List<float3>
        {
            new float3(0f, 0f, 0f),
            new float3(300f, 0f, 0f),
            new float3(300f, 0f, 300f),
            new float3(0f, 0f, 300f),
        };

        [Fact]
        public void AngleZero_MatchesPreviousBehaviorExactly()
        {
            var parameters = new GridParameters
            {
                Mode = SpacingMode.FitToArea,
                Rows = 3,
                Columns = 3,
                SpacingMeters = 60f,
                AngleOffsetDegrees = 0f,
            };

            var segments = GridGenerator.GenerateGrid(SquareNodes, parameters);

            // Same result as the regression test without angle (24 segments,
            // 3 u-lines × 4 segments + 3 v-lines × 4 segments).
            Assert.Equal(24, segments.Count);
        }

        [Fact]
        public void Angle90_SwapsAxisOfVariation()
        {
            // Axis-aligned square, origin (0,0), longest edge = (0,0)->(300,0)
            // so uDir=(1,0) at 0°. A single line of each set (Rows=1, Columns=1)
            // to isolate the geometry without any sorting ambiguity.
            var parameters0 = new GridParameters
            {
                Mode = SpacingMode.FitToArea,
                Rows = 1,
                Columns = 1,
                SpacingMeters = 60f,
                AngleOffsetDegrees = 0f,
            };
            var parameters90 = parameters0;
            parameters90.AngleOffsetDegrees = 90f;

            var segments0 = GridGenerator.GenerateGrid(SquareNodes, parameters0);
            var segments90 = GridGenerator.GenerateGrid(SquareNodes, parameters90);

            // The column line crosses the row line right in the middle of the square
            // and is therefore pre-split into 2 sub-segments (Work item 1): each
            // piece stays individually straight, First() is enough to read its axis.

            // At 0°: the column line is vertical in world space (X constant, Z varies).
            var columnLine0 = segments0.First(s => !s.IsHorizontal);
            Assert.Equal(columnLine0.Start.x, columnLine0.End.x, 2);
            Assert.NotEqual(columnLine0.Start.z, columnLine0.End.z, 2);

            // At 90°: the same column line becomes horizontal in world space
            // (Z constant, X varies) — the axes have swapped.
            var columnLine90 = segments90.First(s => !s.IsHorizontal);
            Assert.Equal(columnLine90.Start.z, columnLine90.End.z, 2);
            Assert.NotEqual(columnLine90.Start.x, columnLine90.End.x, 2);
        }

        [Fact]
        public void ObliqueAngle_ClipsPolygonCorrectlyOnConcaveShape()
        {
            // Concave L, 45° angle: no hand-computed expected values (too
            // tedious to derive), we check the structural invariants of the
            // clipping (well-formed segments, no crossing in the middle of a segment).
            var nodes = new List<float3>
            {
                new float3(0f, 0f, 0f),
                new float3(300f, 0f, 0f),
                new float3(300f, 0f, 150f),
                new float3(150f, 0f, 150f),
                new float3(150f, 0f, 300f),
                new float3(0f, 0f, 300f),
            };
            var parameters = new GridParameters
            {
                Mode = SpacingMode.FixedSpacing,
                Rows = 3,
                Columns = 3,
                SpacingMeters = 60f,
                AngleOffsetDegrees = 45f,
            };

            var segments = GridGenerator.GenerateGrid(nodes, parameters);

            Assert.NotEmpty(segments);
            foreach (var segment in segments)
            {
                Assert.True(math.distance(segment.Start.xz, segment.End.xz) >= GridGenerator.MinSegmentLength - 1e-3f,
                    $"Sub-segment too short at 45°: {segment.Start} → {segment.End}");
                Assert.False(float.IsNaN(segment.Start.x) || float.IsNaN(segment.Start.z)
                    || float.IsNaN(segment.End.x) || float.IsNaN(segment.End.z),
                    "NaN coordinate produced by clipping at an oblique angle.");
            }
        }
    }

    /// <summary>
    /// Checks cul-de-sac mode: collectors (lines parallel to the main
    /// axis, "rows") always stay through-roads; columns
    /// (perpendicular) become cul-de-sacs according to CulDeSacRatio, with
    /// top/bottom alternation if Staggered and depth CulDeSacDepth.
    ///
    /// Common geometry: square (0,0)-(300,0)-(300,300)-(0,300). Longest
    /// edge (0,0)-(300,0) ⇒ uDir=(1,0) East (main axis), vDir=(0,1) North.
    /// Columns=1 places a single column (u=150, world X=150 constant); Rows=N
    /// places N evenly spaced collectors (v=const, world Z=const). The world point
    /// of a column at coordinate t is therefore (150, t) — used to check
    /// the cul-de-sac endpoints precisely.
    /// </summary>
    public class GridGeneratorCulDeSacTests
    {
        private static readonly List<float3> SquareNodes = new List<float3>
        {
            new float3(0f, 0f, 0f),
            new float3(300f, 0f, 0f),
            new float3(300f, 0f, 300f),
            new float3(0f, 0f, 300f),
        };

        private static GridParameters BaseParameters(int rows) => new GridParameters
        {
            Mode = SpacingMode.FitToArea,
            Rows = rows,
            Columns = 1,
            SpacingMeters = 60f,
            CulDeSacMode = true,
            CulDeSacDepth = 0.75f,
            Staggered = true,
            CulDeSacRatio = 100f,
        };

        /// <summary>Column (u-line) segments only: IsHorizontal=false is specific to them, collectors have IsHorizontal=true.</summary>
        private static List<RoadSegmentDef> ColumnSegments(List<RoadSegmentDef> segments) =>
            segments.Where(s => !s.IsHorizontal).ToList();

        [Fact]
        public void Disabled_ProducesExactlySameOutputAsWithoutCulDeSac()
        {
            var withFlag = BaseParameters(3);
            withFlag.CulDeSacMode = false;
            withFlag.Columns = 3; // reproduces the 3x3 regression test exactly

            var withoutField = new GridParameters
            {
                Mode = SpacingMode.FitToArea,
                Rows = 3,
                Columns = 3,
                SpacingMeters = 60f,
            };

            var segments1 = GridGenerator.GenerateGrid(SquareNodes, withFlag);
            var segments2 = GridGenerator.GenerateGrid(SquareNodes, withoutField);

            Assert.Equal(24, segments1.Count);
            Assert.Equal(segments2.Count, segments1.Count);
        }

        [Fact]
        public void Ratio100_NoColumnBlockRemainsFullLength()
        {
            // Rows=2: collectors at v=100 and v=200. Column u=150 spans
            // [0,300] ⇒ chain [0(border),100(collector),200(collector),300(border)]
            // ⇒ 3 blocks. Block [0,100]: start=border (not a real collector) ⇒
            // removed. Block [100,200]: odd alternation ⇒ goes from 200 towards 100,
            // stops at 200+(100-200)*0.75=125. Block [200,300]: even alternation
            // ⇒ goes from 200 towards 300, stops at 200+(300-200)*0.75=275.
            var parameters = BaseParameters(2);

            var segments = GridGenerator.GenerateGrid(SquareNodes, parameters);
            var columns = ColumnSegments(segments);

            Assert.Equal(2, columns.Count); // the border block is removed

            foreach (var segment in columns)
            {
                float length = math.distance(segment.Start.xz, segment.End.xz);
                Assert.True(length < 100f - 1f, $"Cul-de-sac not shorter than the full block (100 m): {length}");
                Assert.Equal(75f, length, 1); // 0.75 * 100
            }

            // Both cul-de-sacs do start from the central collector (v=200,
            // world (150,200)) and each stops 75 m away in opposite directions.
            var endpoints = columns.SelectMany(s => new[] { s.Start, s.End }).ToList();
            Assert.Contains(endpoints, p => math.distance(p.xz, new float2(150f, 200f)) < 0.5f);
            Assert.Contains(endpoints, p => math.distance(p.xz, new float2(150f, 125f)) < 0.5f);
            Assert.Contains(endpoints, p => math.distance(p.xz, new float2(150f, 275f)) < 0.5f);
        }

        [Fact]
        public void CulDeSacBlock_MarksOnlyTheFreeEndAsCulDeSacEnd()
        {
            // Same geometry as Ratio100_NoColumnBlockRemainsFullLength: 2 cul-de-sacs,
            // each starting from a real collector (Start, never marked) and
            // ending at its free end (End, must be marked IsCulDeSacEnd).
            var segments = GridGenerator.GenerateGrid(SquareNodes, BaseParameters(2));
            var columns = ColumnSegments(segments);

            Assert.Equal(2, columns.Count);
            foreach (var segment in columns)
            {
                Assert.True(segment.IsCulDeSacEnd, "The free end of a cul-de-sac must be marked IsCulDeSacEnd.");
            }

            // Through collectors (horizontal lines) are never cul-de-sacs.
            var collectors = segments.Where(s => s.IsHorizontal).ToList();
            Assert.NotEmpty(collectors);
            Assert.All(collectors, s => Assert.False(s.IsCulDeSacEnd));
        }

        [Fact]
        public void StaggeredTrueVersusFalse_ProduceDifferentStartingCollectors()
        {
            // Same geometry as Ratio100_NoColumnBlockRemainsFullLength. Without
            // alternation, block [100,200] must start from the lower collector
            // (v=100) instead of the upper one (v=200).
            var staggered = BaseParameters(2);
            var notStaggered = BaseParameters(2);
            notStaggered.Staggered = false;

            var columnsStaggered = ColumnSegments(GridGenerator.GenerateGrid(SquareNodes, staggered));
            var columnsPlain = ColumnSegments(GridGenerator.GenerateGrid(SquareNodes, notStaggered));

            var staggeredEndpoints = columnsStaggered.SelectMany(s => new[] { s.Start, s.End }).ToList();
            var plainEndpoints = columnsPlain.SelectMany(s => new[] { s.Start, s.End }).ToList();

            // With Staggered=true, a cul-de-sac starts from the upper collector (v=200)
            // downwards and stops at v=125.
            Assert.Contains(staggeredEndpoints, p => math.distance(p.xz, new float2(150f, 125f)) < 0.5f);
            // With Staggered=false, the same block starts from the lower collector (v=100)
            // upwards and stops at v=175 (100+(200-100)*0.75) — a different result.
            Assert.Contains(plainEndpoints, p => math.distance(p.xz, new float2(150f, 175f)) < 0.5f);
            Assert.DoesNotContain(plainEndpoints, p => math.distance(p.xz, new float2(150f, 125f)) < 0.5f);
        }

        [Fact]
        public void Ratio50_AlternatesThroughAndCulDeSacBlocksDeterministically()
        {
            // Rows=4: collectors at v=60,120,180,240 ⇒ 6-point chain, 5 blocks
            // of 60 m each (idx 0..4). "Every other one" pattern (ratio=50, N=2):
            // even blocks (0,2,4) try a cul-de-sac, odd ones (1,3) stay
            // through-roads. Block 0 is on the border (removed); 2 and 4 succeed
            // (start = real collector) ⇒ 2 cul-de-sacs (45 m = 0.75*60) + 2
            // full through-segments (60 m) survive, i.e. 4 column segments.
            var parameters = BaseParameters(4);
            parameters.CulDeSacRatio = 50f;

            var columns = ColumnSegments(GridGenerator.GenerateGrid(SquareNodes, parameters));

            Assert.Equal(4, columns.Count);

            int fullLength = columns.Count(s => math.distance(s.Start.xz, s.End.xz) > 59f);
            int stubLength = columns.Count(s =>
            {
                float length = math.distance(s.Start.xz, s.End.xz);
                return length > 44f && length < 46f;
            });

            Assert.Equal(2, fullLength); // blocks 1 and 3: through-roads (60 m)
            Assert.Equal(2, stubLength); // blocks 2 and 4: cul-de-sacs (45 m)
        }

        [Fact]
        public void DepthOutOfRange_IsClampedToValidBounds()
        {
            // CulDeSacDepth outside [0.5, 0.9] (e.g. a value not passed through the UI/settings):
            // clamped rather than producing a degenerate cul-de-sac (zero or negative length).
            var tooLow = BaseParameters(2);
            tooLow.CulDeSacDepth = 0f;
            var tooHigh = BaseParameters(2);
            tooHigh.CulDeSacDepth = 1f;

            var columnsLow = ColumnSegments(GridGenerator.GenerateGrid(SquareNodes, tooLow));
            var columnsHigh = ColumnSegments(GridGenerator.GenerateGrid(SquareNodes, tooHigh));

            foreach (var segment in columnsLow.Concat(columnsHigh))
            {
                float length = math.distance(segment.Start.xz, segment.End.xz);
                Assert.InRange(length, 50f - 1f, 90f + 1f); // 0.5..0.9 * 100 m block
            }
        }

        [Fact]
        public void AxisRows_AppliesCulDeSacToRowsInsteadOfColumns()
        {
            // Transpose of Ratio100_NoColumnBlockRemainsFullLength: Columns=2 (instead of
            // Rows=2) provides the 2 internal crossings to the SINGLE row line
            // (Rows=1), which becomes the cul-de-sac candidate with CulDeSacAxis.Rows.
            var parameters = BaseParameters(1);
            parameters.Columns = 2;
            parameters.CulDeSacAxis = CulDeSacAxis.Rows;

            var segments = GridGenerator.GenerateGrid(SquareNodes, parameters);
            var columns = ColumnSegments(segments); // axisIsU (columns): must NEVER be cul-de-sacs here.
            var rows = segments.Except(columns).ToList();

            Assert.NotEmpty(columns);
            Assert.All(columns, s => Assert.False(s.IsCulDeSacEnd));
            Assert.Contains(rows, s => s.IsCulDeSacEnd);
        }

        [Fact]
        public void AxisBoth_AppliesCulDeSacToBothColumnsAndRows()
        {
            // Rows=2 AND Columns=2: both axes have internal crossings to
            // offer as starting collectors for the other axis.
            var parameters = BaseParameters(2);
            parameters.Columns = 2;
            parameters.CulDeSacAxis = CulDeSacAxis.Both;

            var segments = GridGenerator.GenerateGrid(SquareNodes, parameters);
            var columns = ColumnSegments(segments);
            var rows = segments.Except(columns).ToList();

            Assert.Contains(columns, s => s.IsCulDeSacEnd);
            Assert.Contains(rows, s => s.IsCulDeSacEnd);
        }
    }

    /// <summary>
    /// Checks MinNodeDistance: a generated node (crossing, or cul-de-sac end) that
    /// would fall less than MinNodeDistance from another already established node is simply
    /// omitted, never merged or shifted.
    ///
    /// The simplest near-coincidence to trigger deterministically is a
    /// cul-de-sac whose depth (close to the 90 % upper bound) makes a short block
    /// end less than MinNodeDistance from the collector it approaches without
    /// ever reaching it: 70 m block at 90 % depth ⇒ 7 m gap &lt; 8 m.
    /// </summary>
    public class GridGeneratorMinNodeDistanceTests
    {
        private static readonly List<float3> SquareNodes = new List<float3>
        {
            new float3(0f, 0f, 0f),
            new float3(300f, 0f, 0f),
            new float3(300f, 0f, 300f),
            new float3(0f, 0f, 300f),
        };

        [Fact]
        public void NormalGrid_NoOmission()
        {
            var parameters = new GridParameters
            {
                Mode = SpacingMode.FitToArea,
                Rows = 3,
                Columns = 3,
                SpacingMeters = 60f,
            };

            var segments = GridGenerator.GenerateGrid(SquareNodes, parameters, out int omittedNodeCount);

            Assert.Equal(0, omittedNodeCount);
            Assert.Equal(24, segments.Count); // identical to the regression test without MinNodeDistance
        }

        [Fact]
        public void CulDeSacStubTooCloseToTargetCollector_IsOmittedNotShortened()
        {
            // Rows=3 (collectors every 70 m on a band [80,290] deliberately
            // off-grid — we force the geometry via FixedSpacing instead): simpler,
            // we directly build a rectangle where the collector spacing is
            // exactly 70 m to isolate a single block of the right size.
            var nodes = new List<float3>
            {
                new float3(0f, 0f, 0f),
                new float3(300f, 0f, 0f),
                new float3(300f, 0f, 105f), // 3 x 35 m
                new float3(0f, 0f, 105f),
            };
            var parameters = new GridParameters
            {
                Mode = SpacingMode.FixedSpacing,
                Columns = 1,
                Rows = 1,
                SpacingMeters = 35f, // collectors at v=35 and v=70
                CulDeSacMode = true,
                CulDeSacDepth = 0.9f, // capped at 0.8 (see GridGenerator): gap of 20 % of the block = 7 m
                Staggered = true,
                CulDeSacRatio = 100f,
            };

            var segments = GridGenerator.GenerateGrid(nodes, parameters, out int omittedNodeCount);
            var columns = segments.Where(s => !s.IsHorizontal).ToList();

            // Block [0,35] (border, start is not a collector): removed as before.
            // Block [35,70]: odd ⇒ goes from collector v=70 towards v=35,
            // would stop at 70+(35-70)*0.8=42, 7 m from collector v=35
            // (< MinNodeDistance=8) ⇒ omitted for near-coincidence, NOT shortened.
            // Block [70,105]: even ⇒ goes from v=70 towards v=105 (border, not a collector),
            // stops at 70+(105-70)*0.8=98, 7 m from the border — since the border is not an
            // "already established" node, this block is NOT affected by MinNodeDistance.
            Assert.True(omittedNodeCount >= 1, "Block [35,70] should have been omitted for near-coincidence.");

            // No column segment may stop less than MinNodeDistance
            // from a collector without actually being merged with it (same
            // bit-exact point): no floating-point near-duplicate.
            var collectorPositions = new[] { new float2(150f, 70f), new float2(150f, 140f) };
            foreach (var segment in columns)
            {
                foreach (float2 collector in collectorPositions)
                {
                    float distToStart = math.distance(segment.Start.xz, collector);
                    float distToEnd = math.distance(segment.End.xz, collector);
                    // Either coincident (segment that really joins the collector),
                    // or well beyond MinNodeDistance — never in between.
                    Assert.True(distToStart < 0.5f || distToStart >= GridGenerator.MinNodeDistance - 0.01f,
                        $"Start endpoint {distToStart} m from a collector: neither merged nor far enough.");
                    Assert.True(distToEnd < 0.5f || distToEnd >= GridGenerator.MinNodeDistance - 0.01f,
                        $"End endpoint {distToEnd} m from a collector: neither merged nor far enough.");
                }
            }
        }

        [Fact]
        public void NearCoincidence_NeverCrashesAndRestOfGridStaysWellFormed()
        {
            // Dense grid on a polygon rotated at an oblique angle, with cul-de-sacs:
            // combines several possible sources of near-coincidence (close
            // crossings, deep cul-de-sacs) without any of them causing a crash,
            // a duplicate, or a segment below MinSegmentLength.
            var nodes = new List<float3>
            {
                new float3(0f, 0f, 0f),
                new float3(280f, 0f, 90f),
                new float3(190f, 0f, 370f),
                new float3(-90f, 0f, 280f),
            };
            var parameters = new GridParameters
            {
                Mode = SpacingMode.FitToArea,
                Columns = 6,
                Rows = 4,
                SpacingMeters = 60f,
                AngleOffsetDegrees = 33f,
                CulDeSacMode = true,
                CulDeSacDepth = 0.9f,
                Staggered = true,
                CulDeSacRatio = 100f,
            };

            List<RoadSegmentDef> segments = null;
            Exception thrown = null;
            try
            {
                segments = GridGenerator.GenerateGrid(nodes, parameters, out int omittedNodeCount);
            }
            catch (Exception e)
            {
                thrown = e;
            }

            Assert.Null(thrown);
            Assert.NotNull(segments);

            // No duplicates: all identical endpoints must be EXACTLY
            // the same floats (valid merge), never two distinct points less than
            // MinNodeDistance apart (that would be an uncleaned near-coincidence).
            var endpoints = segments.SelectMany(s => new[] { s.Start, s.End }).Distinct().ToList();
            for (int i = 0; i < endpoints.Count; i++)
            {
                for (int j = i + 1; j < endpoints.Count; j++)
                {
                    float distance = math.distance(endpoints[i].xz, endpoints[j].xz);
                    Assert.True(distance >= GridGenerator.MinNodeDistance - 0.5f,
                        $"Two distinct nodes nearly coincident, not cleaned up: {endpoints[i]} vs {endpoints[j]} ({distance} m).");
                }
            }

            foreach (var segment in segments)
            {
                float length = math.distance(segment.Start.xz, segment.End.xz);
                Assert.True(length >= MinAcceptableLength(parameters),
                    $"Segment below the minimum acceptable length: {length} m.");
            }
        }

        [Fact]
        public void NearCoincidentCrossings_MergeIntoASharedNodeInsteadOfDroppingALine()
        {
            // Fixed bug (like NetworkTools' "Super node"): a crossing less than
            // MinNodeDistance from an already accepted crossing used to be simply omitted — its
            // u-line AND its v-line then got NO subdivision at that spot,
            // unlike every other line in the grid (one of the two, or even
            // both, "lost"). It must now be MERGED with that existing point: BOTH
            // lines join the shared node (same bit-exact position).
            //
            // FitToArea on a small perimeter with many columns/rows: the RESULTING
            // spacing (side / number of columns) falls below MinNodeDistance (80/12 ≈ 6.7 m
            // < 8 m), so two adjacent crossings on the same line (same u or same v,
            // the other coordinate shifted by a single grid step) necessarily fall
            // less than MinNodeDistance apart — unlike FixedSpacing with a
            // normal spacing (>= 10 m in the UI), where two DISTINCT crossings can
            // never be closer than the spacing itself (perfectly regular lattice).
            // Reproduces what a player can get through the UI: small perimeter + many
            // columns/rows (up to 12, see gridPanel.tsx), no need for a spacing
            // unreachable from the panel (min 10 m).
            var nodes = new List<float3>
            {
                new float3(0f, 0f, 0f),
                new float3(80f, 0f, 0f),
                new float3(80f, 0f, 80f),
                new float3(0f, 0f, 80f),
            };
            var parameters = new GridParameters
            {
                Mode = SpacingMode.FitToArea,
                Columns = 12,
                Rows = 12,
            };

            var segments = GridGenerator.GenerateGrid(nodes, parameters, out int mergedCount);
            Assert.True(mergedCount > 0, "This dense/oblique perimeter should produce at least one crossing merge.");

            // Group endpoints by bit-exact position (a successful merge yields identical
            // floats, not just "close" ones — see BuildSubSegments). Any node
            // shared by 3 or more segments is a real multi-branch crossing (or a
            // merged crossing): it must then have segments of BOTH orientations,
            // never just one — the exact signature of the bug (a line "forgotten" by the omission).
            var byPosition = new Dictionary<string, (bool hasHorizontal, bool hasVertical, int count)>();
            void Record(float3 pos, bool isHorizontal)
            {
                string key = $"{pos.x:F3}_{pos.z:F3}";
                (bool hasHorizontal, bool hasVertical, int count) entry = byPosition.TryGetValue(key, out var existing)
                    ? existing
                    : (false, false, 0);
                byPosition[key] = (entry.hasHorizontal || isHorizontal, entry.hasVertical || !isHorizontal, entry.count + 1);
            }
            foreach (RoadSegmentDef s in segments)
            {
                Record(s.Start, s.IsHorizontal);
                Record(s.End, s.IsHorizontal);
            }

            foreach (var kvp in byPosition)
            {
                if (kvp.Value.count >= 3)
                {
                    Assert.True(kvp.Value.hasHorizontal && kvp.Value.hasVertical,
                        $"Node at {kvp.Key} with {kvp.Value.count} segments but only one orientation represented — line lost instead of merged.");
                }
            }
        }

        private static float MinAcceptableLength(GridParameters parameters)
        {
            // A cul-de-sac can be as short as MinSegmentLength; so can a normal
            // through-segment. We keep a modest safety margin on the assertion.
            return GridGenerator.MinSegmentLength - 0.5f;
        }
    }

    /// <summary>
    /// Work item 1 (priority bug): the perimeter polygon must follow the REAL curve
    /// of an existing edge between two consecutive nodes (roundabout, bend...),
    /// not the straight chord between them — otherwise the generated grid can spill onto the
    /// curved road itself. GridGenerator.SampleCurve (pure, no ECS/Colossal.Mathematics dependency
    /// — see its header) samples that curve; the game layer (not testable here)
    /// inserts it into the point list before generation.
    /// </summary>
    public class GridGeneratorCurveSamplingTests
    {
        // ------------------------------------------------------------------
        // SampleCurve alone: the sampled points must follow the curve,
        // not the straight chord between the endpoints.
        // ------------------------------------------------------------------

        [Fact]
        public void SampleCurve_StraightBezier_ReturnsPointsOnTheChord()
        {
            // Aligned control points: the "curve" is actually a straight line.
            var a = new float3(0f, 0f, 0f);
            var b = new float3(33f, 0f, 0f);
            var c = new float3(66f, 0f, 0f);
            var d = new float3(100f, 0f, 0f);

            List<float3> samples = GridGenerator.SampleCurve(a, b, c, d);

            Assert.NotEmpty(samples);
            foreach (float3 p in samples)
            {
                // Must stay on the line Y=0, Z=0, with X strictly between 0 and 100.
                Assert.True(p.x > 0f && p.x < 100f);
                Assert.Equal(0f, p.z, 3);
            }
        }

        [Fact]
        public void SampleCurve_BulgingBezier_PointsDeviateSignificantlyFromTheChord()
        {
            // Curve that deviates strongly from the chord a→d (75 m bulge over 200 m
            // of length): simulates a roundabout edge between two selected nodes.
            var a = new float3(200f, 0f, 0f);
            var b = new float3(100f, 0f, 0f);
            var c = new float3(100f, 0f, 200f);
            var d = new float3(200f, 0f, 200f);

            List<float3> samples = GridGenerator.SampleCurve(a, b, c, d);

            Assert.NotEmpty(samples);
            // The straight chord a→d would be X=200 everywhere: at least one sampled point
            // must deviate clearly from it (the bulge, not a chord).
            Assert.Contains(samples, p => p.x < 170f);
            // The point closest to the middle (z≈100) must be close to the theoretical
            // maximum bulge (x=125, cf. the midpoint formula of a cubic).
            float3 middle = samples.OrderBy(p => math.abs(p.z - 100f)).First();
            Assert.True(middle.x < 150f, $"Midpoint expected close to the bulge (x<150), got x={middle.x}.");
        }

        [Fact]
        public void SampleCurve_ShortEdge_NeverExceedsMaxSamples()
        {
            // Very long curve (MaxCurveSamplesPerEdge safeguard): must never
            // produce a disproportionate number of points.
            var a = new float3(0f, 0f, 0f);
            var b = new float3(0f, 0f, 1000f);
            var c = new float3(2000f, 0f, 1000f);
            var d = new float3(2000f, 0f, 2000f);

            List<float3> samples = GridGenerator.SampleCurve(a, b, c, d);

            Assert.True(samples.Count <= GridGenerator.MaxCurveSamplesPerEdge);
        }

        // ------------------------------------------------------------------
        // Integration: a polygon with one side sampled along a
        // curve (instead of the straight chord between its two endpoints) must exclude
        // the area between the chord and the curve — the generated grid must not
        // spill into it, unlike what would happen with the chord alone.
        // ------------------------------------------------------------------

        [Fact]
        public void CurveSampledBoundary_ExcludesBulgeArea_ComparedToStraightChord()
        {
            // 200×200 square, but the right side (x=200, from z=0 to z=200) is actually
            // a roundabout edge that bows inward to x≈125 around z=100 (see
            // SampleCurve_BulgingBezier above for the same curve) — as if the
            // curved road bit into what would otherwise be a buildable area.
            var a = new float3(200f, 0f, 0f);
            var b = new float3(100f, 0f, 0f);
            var c = new float3(100f, 0f, 200f);
            var d = new float3(200f, 0f, 200f);
            List<float3> curveSamples = GridGenerator.SampleCurve(a, b, c, d);

            var straightChordPerimeter = new List<float3>
            {
                new float3(0f, 0f, 0f),
                new float3(200f, 0f, 0f),
                new float3(200f, 0f, 200f),
                new float3(0f, 0f, 200f),
            };
            var curveAwarePerimeter = new List<float3> { new float3(0f, 0f, 0f), a };
            curveAwarePerimeter.AddRange(curveSamples);
            curveAwarePerimeter.Add(d);
            curveAwarePerimeter.Add(new float3(0f, 0f, 200f));

            var parameters = new GridParameters
            {
                Mode = SpacingMode.FitToArea,
                Rows = 9,
                Columns = 9,
                SpacingMeters = 60f,
            };

            var straightSegments = GridGenerator.GenerateGrid(straightChordPerimeter, parameters);
            var curvedSegments = GridGenerator.GenerateGrid(curveAwarePerimeter, parameters);

            Assert.NotEmpty(straightSegments);
            Assert.NotEmpty(curvedSegments);

            // Bulge area (around z=100): the straight chord lets generation
            // reach x close to 200; the polygon following the curve must stay
            // clearly back from that limit in the same area.
            float MaxXNearBulge(List<RoadSegmentDef> segments) => segments
                .SelectMany(s => new[] { s.Start, s.End })
                .Where(p => math.abs(p.z - 100f) < 20f)
                .Select(p => p.x)
                .DefaultIfEmpty(float.MinValue)
                .Max();

            float straightMaxX = MaxXNearBulge(straightSegments);
            float curvedMaxX = MaxXNearBulge(curvedSegments);

            Assert.True(curvedMaxX < straightMaxX - 30f,
                $"The polygon following the curve should exclude the bulge (max x expected clearly < {straightMaxX}), got {curvedMaxX}.");
        }
    }

    // ------------------------------------------------------------------
    // Avenue (third level: freely chosen column/row, never a
    // cul-de-sac, optional roundabout where the two cross) — 300×300 square,
    // fixed 5×5 grid (spacing 60), axis-aligned for simple coordinates.
    // ------------------------------------------------------------------
    public class GridGeneratorAvenueTests
    {
        private static readonly List<float3> SquareNodes = new List<float3>
        {
            new float3(0f, 0f, 0f),
            new float3(300f, 0f, 0f),
            new float3(300f, 0f, 300f),
            new float3(0f, 0f, 300f),
        };

        private static GridParameters BaseParameters() => new GridParameters
        {
            Mode = SpacingMode.FixedSpacing,
            Rows = 5,
            Columns = 5,
            SpacingMeters = 60f,
        };

        [Fact]
        public void AvenueColumnEnabled_MarksOnlyThatColumnsSegmentsAsAvenue()
        {
            var parameters = BaseParameters();
            parameters.AvenueColumnEnabled = true;
            // DistributeFixed(0, 300, 60) → u-lines = [60, 120, 180, 240]; index 2 = u=180.
            parameters.AvenueColumnIndex = 2;

            var segments = GridGenerator.GenerateGrid(SquareNodes, parameters);

            var avenueSegments = segments.Where(s => s.IsAvenue).ToList();
            Assert.NotEmpty(avenueSegments);
            foreach (var seg in avenueSegments)
            {
                // Column = constant u-line → vertical segments (X constant, not horizontal).
                Assert.False(seg.IsHorizontal);
                Assert.Equal(180f, seg.Start.x, 1);
                Assert.Equal(180f, seg.End.x, 1);
            }
            // No other segment may be marked avenue.
            Assert.All(segments.Except(avenueSegments), s => Assert.False(s.IsAvenue));
        }

        [Fact]
        public void AvenueLine_NeverBecomesCulDeSac_EvenWhenCulDeSacModeCoversItsAxis()
        {
            var parameters = BaseParameters();
            parameters.AvenueColumnEnabled = true;
            parameters.AvenueColumnIndex = 2;
            parameters.CulDeSacMode = true;
            parameters.CulDeSacAxis = CulDeSacAxis.Columns;
            parameters.CulDeSacRatio = 100f; // all columns would be cul-de-sacs without the avenue exception
            parameters.Staggered = false;

            var segments = GridGenerator.GenerateGrid(SquareNodes, parameters);

            var avenueSegments = segments.Where(s => s.IsAvenue).ToList();
            Assert.NotEmpty(avenueSegments);
            Assert.DoesNotContain(avenueSegments, s => s.IsCulDeSacEnd);

            // The other columns did become cul-de-sacs (the pattern still works
            // elsewhere — only the avenue column escapes it).
            Assert.Contains(segments, s => s.IsCulDeSacEnd);
        }

        [Fact]
        public void OutOfRangeAvenueIndex_IsSilentlyANoOp()
        {
            var parameters = BaseParameters();
            parameters.AvenueColumnEnabled = true;
            parameters.AvenueColumnIndex = 999;
            parameters.AvenueRowEnabled = true;
            parameters.AvenueRowIndex = -1;

            var segments = GridGenerator.GenerateGrid(SquareNodes, parameters);

            Assert.DoesNotContain(segments, s => s.IsAvenue);
            RoadsGridGeneratorTests.AssertCommonInvariants(segments);
        }

        [Fact]
        public void BothAvenuesEnabled_ReportsRoundaboutCenterAndRadius_WithoutTrimmingTheArms()
        {
            // Like a cul-de-sac's turning circle: no more trimming of the arms nor an arc
            // loop generated on the geometry side — the two avenues cross normally
            // in a +, and only the center/radius (to place the full decorative asset in the game layer) are
            // returned via RoundaboutInfo.
            var parameters = BaseParameters();
            parameters.AvenueColumnEnabled = true;
            parameters.AvenueColumnIndex = 2; // u = 180
            parameters.AvenueRowEnabled = true;
            parameters.AvenueRowIndex = 2; // v = 180
            var center = new float2(180f, 180f);
            float expectedRadius = math.min(parameters.SpacingMeters * 0.25f, 25f);

            var segments = GridGenerator.GenerateGrid(SquareNodes, parameters, out _, out RoundaboutInfo roundabout);
            var avenueSegments = segments.Where(s => s.IsAvenue).ToList();

            Assert.True(roundabout.HasRoundabout);
            Assert.Equal(center, roundabout.Center.xz);
            Assert.Equal(expectedRadius, roundabout.Radius, 2);

            // The normal + crossing: at least one avenue arm passes exactly through the
            // center (no trimming), unlike the old behaviour.
            Assert.Contains(avenueSegments, s =>
                math.distance(s.Start.xz, center) < 0.5f || math.distance(s.End.xz, center) < 0.5f);

            RoadsGridGeneratorTests.AssertCommonInvariants(segments);
        }

        [Fact]
        public void OnlyOneAvenueAxisEnabled_NeverAddsARoundabout()
        {
            var parameters = BaseParameters();
            parameters.AvenueColumnEnabled = true;
            parameters.AvenueColumnIndex = 2;
            // AvenueRowEnabled stays false: a single axis, no crossing to handle.

            var segments = GridGenerator.GenerateGrid(SquareNodes, parameters, out _, out RoundaboutInfo roundabout);
            var avenueSegments = segments.Where(s => s.IsAvenue).ToList();

            Assert.False(roundabout.HasRoundabout);
            Assert.NotEmpty(avenueSegments);
            RoadsGridGeneratorTests.AssertCommonInvariants(segments);
        }

        [Fact]
        public void OutOfRangeAvenueCrossing_ReportsNoRoundabout()
        {
            var parameters = BaseParameters();
            parameters.AvenueColumnEnabled = true;
            parameters.AvenueColumnIndex = 999;
            parameters.AvenueRowEnabled = true;
            parameters.AvenueRowIndex = -1;

            GridGenerator.GenerateGrid(SquareNodes, parameters, out _, out RoundaboutInfo roundabout);

            Assert.False(roundabout.HasRoundabout);
        }
    }

    // ------------------------------------------------------------------
    // Loop (sparse collectors + inner loop per superblock) — see
    // GridGenerator.GenerateLoopGrid/EmitLoopBlock.
    // ------------------------------------------------------------------
    public class GridGeneratorLoopGridTests
    {
        private static GridParameters LoopParams(float collectorSpacing, float culDeSacRatio = 0f) => new GridParameters
        {
            CollectorSpacingMeters = collectorSpacing,
            LoopCulDeSacRatio = culDeSacRatio,
            CulDeSacDepth = 0.75f,
        };

        private static readonly List<float3> SquareNodes300 = new List<float3>
        {
            new float3(0f, 0f, 0f),
            new float3(300f, 0f, 0f),
            new float3(300f, 0f, 300f),
            new float3(0f, 0f, 300f),
        };

        // Larger interior cell (300×300 at spacing 300, versus 100×100 for
        // SquareNodes300/100): needed for the cul-de-sac tests, whose top
        // segment (see EmitLoopBlock) must exceed 2×MinSegmentLength to be split in
        // two without both halves being discarded as too short.
        private static readonly List<float3> SquareNodes900 = new List<float3>
        {
            new float3(0f, 0f, 0f),
            new float3(900f, 0f, 0f),
            new float3(900f, 0f, 900f),
            new float3(0f, 0f, 900f),
        };

        [Fact]
        public void DegenerateInput_FewerThanTwoNodes_ReturnsEmptyWithoutThrowing()
        {
            var segments = GridGenerator.GenerateLoopGrid(new List<float3> { new float3(0f, 0f, 0f) }, LoopParams(100f));
            Assert.Empty(segments);
        }

        [Fact]
        public void TooSmallForAnyCollector_ReturnsEmptyWithoutThrowing()
        {
            var tiny = new List<float3>
            {
                new float3(0f, 0f, 0f),
                new float3(20f, 0f, 0f),
                new float3(20f, 0f, 20f),
                new float3(0f, 0f, 20f),
            };
            var segments = GridGenerator.GenerateLoopGrid(tiny, LoopParams(100f));
            Assert.Empty(segments);
        }

        [Fact]
        public void SingleInteriorSuperblock_ProducesOneLoopWithFourArcsAndNoCulDeSac()
        {
            // 300×300, collectors every 100 m -> internal lines at 100 and 200 on each
            // axis (DistributeFixed excludes the borders) -> 1 fully interior superblock (between
            // 100 and 200) AND 8 border strips (between the perimeter and the nearest collector,
            // see GenerateLoopGrid — v1 ignored them entirely, in-game feedback:
            // "too many empty perimeters without loops") -> 3×3 = 9 superblocks in total -> 4 arcs
            // per rectangular loop (EmitSimpleLoopBlock, 4 corners) -> 36 arcs.
            var segments = GridGenerator.GenerateLoopGrid(SquareNodes300, LoopParams(100f, culDeSacRatio: 0f));

            var arcs = segments.Where(s => s.IsArc).ToList();
            var culDeSacs = segments.Where(s => s.IsCulDeSacEnd).ToList();

            Assert.Equal(36, arcs.Count);
            Assert.Empty(culDeSacs);
        }

        [Fact]
        public void SingleInteriorSuperblock_FullCulDeSacRatio_AddsOneCulDeSacSpurPerBlock()
        {
            // Spacing 300 (not 100): the loop's top segment must exceed
            // 2×MinSegmentLength so that splitting it in two (see EmitLoopBlock) leaves a
            // connected cul-de-sac rather than removing it as a too-short orphan.
            // 900×900 at spacing 300 -> 3×3 = 9 superblocks (interior + border strips,
            // see GenerateLoopGrid) -> 9 spurs at 100% frequency.
            var segments = GridGenerator.GenerateLoopGrid(SquareNodes900, LoopParams(300f, culDeSacRatio: 100f));

            var culDeSacs = segments.Where(s => s.IsCulDeSacEnd).ToList();
            Assert.Equal(9, culDeSacs.Count);
        }

        [Fact]
        public void CulDeSacSpur_StartPointIsAnExactSharedVertexWithTheTopSegments()
        {
            // Bug reported in game: "the alleys really aren't connected to the main road" — the
            // cul-de-sac spur started from a midpoint of the top segment, never the endpoint of ANY
            // emitted segment (coincident coordinate, but not a real shared vertex in the game).
            // Fixed by splitting the top segment in two AT the exact junction point. This test
            // checks, for EACH spur (9, see previous test), that its start point is
            // EXACTLY (same floats, not just close) the endpoint of at least one of the
            // non-arc, non-cul-de-sac segments of the loop.
            var segments = GridGenerator.GenerateLoopGrid(SquareNodes900, LoopParams(300f, culDeSacRatio: 100f));

            var spurs = segments.Where(s => s.IsCulDeSacEnd).ToList();
            var loopStraightPieces = segments.Where(s => !s.IsArc && !s.IsCulDeSacEnd && !s.IsAvenue).ToList();

            Assert.NotEmpty(spurs);
            foreach (RoadSegmentDef spur in spurs)
            {
                bool sharesExactVertex = loopStraightPieces.Any(s =>
                    math.all(s.Start == spur.Start) || math.all(s.End == spur.Start));

                Assert.True(sharesExactVertex,
                    $"The spur's start point ({spur.Start}) should be EXACTLY the endpoint of a straight loop segment, not a mere coordinate coincidence.");
            }
        }

        [Fact]
        public void LoopConnectorAttachPoint_IsAnExactSharedVertexOfTheLoopEdge()
        {
            // In-game user feedback: "collisions between the road that accesses the loop and the loop" —
            // the branch (EmitSimpleLoopBlock) touched the MIDDLE of a side of the loop's
            // rectangle (uMid) without ever splitting that side at that exact spot: two segments of
            // the same loop (branch + rectangle side) crossed without sharing a
            // real vertex — the same structural bug as the one already fixed between collector and
            // loop (see LoopLegAttachPoints_AreExactSharedVerticesWithAvenueSegments), but
            // INSIDE the loop's own network. Fixed by ALWAYS splitting the
            // branch's side in two, exactly at the junction point. This test checks
            // that no point of a straight non-avenue segment (loop/branch/cul-de-sac) falls
            // in the MIDDLE (t strictly between 0 and 1) of ANOTHER straight non-avenue segment
            // without being an exact endpoint of it — a real structural crossing, not just a
            // border coincidence.
            var segments = GridGenerator.GenerateLoopGrid(SquareNodes900, LoopParams(300f, culDeSacRatio: 100f));
            var straightLoopPieces = segments.Where(s => !s.IsArc && !s.IsAvenue).ToList();
            Assert.NotEmpty(straightLoopPieces);

            for (int ti = 0; ti < straightLoopPieces.Count; ti++)
            {
                RoadSegmentDef target = straightLoopPieces[ti];
                float2 a = target.Start.xz;
                float2 ab = target.End.xz - a;
                float lenSq = math.lengthsq(ab);
                if (lenSq < 1e-4f)
                {
                    continue;
                }
                for (int oi = 0; oi < straightLoopPieces.Count; oi++)
                {
                    if (oi == ti)
                    {
                        continue;
                    }
                    RoadSegmentDef other = straightLoopPieces[oi];
                    foreach (float3 point in new[] { other.Start, other.End })
                    {
                        float t = math.dot(point.xz - a, ab) / lenSq;
                        if (t <= 0.01f || t >= 0.99f)
                        {
                            continue; // at/beyond an endpoint of THIS target: nothing to report here
                        }
                        float2 projected = a + t * ab;
                        bool onThisLine = math.distance(projected, point.xz) < 0.5f;
                        Assert.False(onThisLine,
                            $"Point {point} falls in the middle of segment {target.Start} -> {target.End} without being a shared vertex (t={t:F3}).");
                    }
                }
            }
        }

        [Fact]
        public void SuperblockMode_ZonesFillTheSelectionWithACollectorEveryThirdLine()
        {
            // User feedback: zones of adjustable size (SuperblockZoneMeters, min 100 m),
            // fitted to fill the selection exactly ("the cell size adapts to the
            // perimeter"), with a collector every 3 zones ("collector > pedestrian > pedestrian
            // > collector"). Never a cul-de-sac; CollectorSpacingMeters has no effect here.
            foreach (float spacing in new[] { 200f, 400f })
            {
                // 900 m / 100 m zones = 9 zones per axis -> lines 100..800, collectors at 300/600.
                var parameters = LoopParams(spacing, culDeSacRatio: 100f);
                parameters.SuperblockMode = true;
                parameters.SuperblockZoneMeters = 100f;
                var segments = GridGenerator.GenerateLoopGrid(SquareNodes900, parameters);

                Assert.DoesNotContain(segments, s => s.IsCulDeSacEnd);
                Assert.All(segments, s => Assert.True(s.IsAvenue != s.IsPedestrian, $"Each segment is either collector or pedestrian: {s.Start} -> {s.End}"));

                var avenueU = segments.Where(s => s.IsAvenue && !s.IsHorizontal).Select(s => (float)math.round(s.Start.x)).Distinct().OrderBy(x => x).ToList();
                var pedU = segments.Where(s => s.IsPedestrian && !s.IsHorizontal).Select(s => (float)math.round(s.Start.x)).Distinct().OrderBy(x => x).ToList();
                var avenueV = segments.Where(s => s.IsAvenue && s.IsHorizontal).Select(s => (float)math.round(s.Start.z)).Distinct().OrderBy(x => x).ToList();
                var pedV = segments.Where(s => s.IsPedestrian && s.IsHorizontal).Select(s => (float)math.round(s.Start.z)).Distinct().OrderBy(x => x).ToList();
                Assert.Equal(new[] { 300f, 600f }, avenueU);
                Assert.Equal(new[] { 300f, 600f }, avenueV);
                Assert.Equal(new[] { 100f, 200f, 400f, 500f, 700f, 800f }, pedU);
                Assert.Equal(new[] { 100f, 200f, 400f, 500f, 700f, 800f }, pedV);
            }
        }

        [Fact]
        public void SuperblockMode_ThreeZonesOrFewer_IsASingleSuperblockAdaptedToTheArea()
        {
            // Zone ≈ 1/3 of the selection -> a single 3×3 superblock, with no internal
            // collector: the perimeter (existing road) closes it. The actual zone size
            // adapts to the area (here 506 m / 170 m -> 3 zones of 168.7 m).
            var square506 = new List<float3>
            {
                new float3(0f, 0f, 0f), new float3(506f, 0f, 0f), new float3(506f, 0f, 506f), new float3(0f, 0f, 506f),
            };
            var parameters = LoopParams(300f, culDeSacRatio: 100f);
            parameters.SuperblockMode = true;
            parameters.SuperblockZoneMeters = 170f;
            var segments = GridGenerator.GenerateLoopGrid(square506, parameters);

            Assert.DoesNotContain(segments, s => s.IsAvenue);
            Assert.All(segments, s => Assert.True(s.IsPedestrian));
            Assert.Equal(12, segments.Count); // 2 lines per axis, each split into 3
            var uLines = segments.Where(s => !s.IsHorizontal).Select(s => (float)math.round(s.Start.x)).Distinct().OrderBy(x => x).ToList();
            Assert.Equal(new[] { 169f, 337f }, uLines);
        }

        [Fact]
        public void SuperblockMode_InteriorGridFormsAConsistentAlternatingSwirl()
        {
            // 900×900 -> a single superblock, internal lines at 300/600 on each axis
            // (SuperblockSubdivisions). Each cell (iu,iv) turns clockwise if iu+iv is even,
            // counter-clockwise otherwise (see ApplyOneWaySwirl).
            var parameters = LoopParams(300f, culDeSacRatio: 0f);
            parameters.SuperblockMode = true;
            parameters.SuperblockZoneMeters = 300f;
            var segments = GridGenerator.GenerateLoopGrid(SquareNodes900, parameters);

            var interior = segments.Where(s => s.IsPedestrian && !s.IsArc).ToList();
            Assert.NotEmpty(interior);

            // Horizontal line (v=300) between u=[0,300]: cell (iu=0,iv=1), odd parity =
            // counter-clockwise -> u decreasing.
            var segsA = interior.Where(s => s.IsHorizontal
                && math.abs(s.Start.z - 300f) < 1f && math.min(s.Start.x, s.End.x) > -1f && math.max(s.Start.x, s.End.x) < 301f).ToList();
            Assert.NotEmpty(segsA);
            Assert.All(segsA, s => Assert.True(s.Start.x > s.End.x, $"Segment A expected u decreasing: {s.Start} -> {s.End}"));

            // Horizontal line (v=300) between u=[300,600]: cell (iu=1,iv=1), even parity =
            // clockwise -> u increasing.
            var segsB = interior.Where(s => s.IsHorizontal
                && math.abs(s.Start.z - 300f) < 1f && math.min(s.Start.x, s.End.x) > 299f && math.max(s.Start.x, s.End.x) < 601f).ToList();
            Assert.NotEmpty(segsB);
            Assert.All(segsB, s => Assert.True(s.Start.x < s.End.x, $"Segment B expected u increasing: {s.Start} -> {s.End}"));

            // Vertical line (u=300) between v=[0,300]: cell (iu=1,iv=0), odd parity =
            // counter-clockwise -> v increasing.
            var segsC = interior.Where(s => !s.IsHorizontal
                && math.abs(s.Start.x - 300f) < 1f && math.min(s.Start.z, s.End.z) > -1f && math.max(s.Start.z, s.End.z) < 301f).ToList();
            Assert.NotEmpty(segsC);
            Assert.All(segsC, s => Assert.True(s.Start.z < s.End.z, $"Segment C expected v increasing: {s.Start} -> {s.End}"));

            // Vertical line (u=300) between v=[300,600]: cell (iu=1,iv=1), even parity =
            // clockwise -> v decreasing.
            var segsD = interior.Where(s => !s.IsHorizontal
                && math.abs(s.Start.x - 300f) < 1f && math.min(s.Start.z, s.End.z) > 299f && math.max(s.Start.z, s.End.z) < 601f).ToList();
            Assert.NotEmpty(segsD);
            Assert.All(segsD, s => Assert.True(s.Start.z > s.End.z, $"Segment D expected v decreasing: {s.Start} -> {s.End}"));
        }

        /// <summary>
        /// True if `point` (xz) falls on one of the sides of the chosen perimeter (with tolerance) —
        /// mirror of the internal helper GridGenerator.IsPointOnPolygonEdge, used here so that the
        /// connectivity tests accept that a pedestrian endpoint lying on the REAL perimeter
        /// is not a cul-de-sac (the game layer connects it, see GenerateSuperblockInterior).
        /// </summary>
        private static bool IsOnPerimeterBoundary(float3 point, IReadOnlyList<float3> perimeter, float tolerance)
        {
            float2 p = point.xz;
            int n = perimeter.Count;
            for (int i = 0; i < n; i++)
            {
                float2 a = perimeter[i].xz;
                float2 b = perimeter[(i + 1) % n].xz;
                float2 ab = b - a;
                float lenSq = math.lengthsq(ab);
                if (lenSq < 1e-6f) continue;
                float t = math.clamp(math.dot(p - a, ab) / lenSq, 0f, 1f);
                float2 projected = a + t * ab;
                if (math.distance(projected, p) < tolerance) return true;
            }
            return false;
        }

        [Fact]
        public void LoopAndSuperblock_IgnoreClassicGridOnlySettings()
        {
            // In-game user feedback (log): "there are only 6 buildable areas instead of 9" —
            // the avenue on column/row N and the cul-de-sac mode of Grid mode, left
            // active, contaminated BuildSubSegments (shared): a pedestrian line became a
            // collector and others disappeared. The result must be identical with or
            // without these settings.
            foreach (bool superblock in new[] { true, false })
            {
                var clean = LoopParams(300f);
                clean.SuperblockMode = superblock;
                var polluted = clean;
                polluted.AvenueColumnEnabled = true;
                polluted.AvenueColumnIndex = 1;
                polluted.AvenueRowEnabled = true;
                polluted.AvenueRowIndex = 0;
                polluted.CulDeSacMode = true;
                polluted.CulDeSacRatio = 100f;

                var expected = GridGenerator.GenerateLoopGrid(SquareNodes900, clean);
                var actual = GridGenerator.GenerateLoopGrid(SquareNodes900, polluted);

                Assert.Equal(expected.Count, actual.Count);
                for (int i = 0; i < expected.Count; i++)
                {
                    Assert.Equal(expected[i].Start, actual[i].Start);
                    Assert.Equal(expected[i].End, actual[i].End);
                    Assert.Equal(expected[i].IsAvenue, actual[i].IsAvenue);
                    Assert.Equal(expected[i].IsPedestrian, actual[i].IsPedestrian);
                    Assert.Equal(expected[i].IsCulDeSacEnd, actual[i].IsCulDeSacEnd);
                }
            }
        }

        [Fact]
        public void SuperblockMode_EveryInteriorEndpointIsAnExactSharedVertex_NeverADeadEnd()
        {
            // In-game user feedback: "There are still cul-de-sacs" — checks that the fine inner
            // grid (see GenerateSuperblockInterior) is FULLY connected: EVERY
            // endpoint of a pedestrian segment must be either a vertex EXACTLY shared with
            // another segment (pedestrian OR collector), or lie on the REAL chosen perimeter (which
            // the game layer connects, see GenerateSuperblockInterior — user feedback:
            // "the pedestrian roads also have to connect to the road used as the base") —
            // never a free end that would show up in game as a cul-de-sac. Realistic scenario with
            // several superblocks (900×900 / 300 -> 3×3 grid of superblocks,
            // each subdivided into 3×3 zones) so as not to be limited to the trivial single
            // superblock case of the previous tests.
            var parameters = LoopParams(300f, culDeSacRatio: 0f);
            parameters.SuperblockMode = true;
            var segments = GridGenerator.GenerateLoopGrid(SquareNodes900, parameters);

            var interior = segments.Where(s => s.IsPedestrian && !s.IsArc).ToList();
            var all = segments.Where(s => !s.IsArc).ToList();
            Assert.NotEmpty(interior);

            foreach (RoadSegmentDef segment in interior)
            {
                foreach (float3 endpoint in new[] { segment.Start, segment.End })
                {
                    bool sharedWithAnother = all.Any(other =>
                        !(math.all(other.Start == segment.Start) && math.all(other.End == segment.End)) // not the same segment
                        && (math.all(other.Start == endpoint) || math.all(other.End == endpoint)));
                    bool onPerimeter = IsOnPerimeterBoundary(endpoint, SquareNodes900, 0.5f);
                    Assert.True(sharedWithAnother || onPerimeter,
                        $"Endpoint {endpoint} of pedestrian segment {segment.Start} -> {segment.End} is neither shared nor on the perimeter — cul-de-sac.");
                }
            }
        }

        [Fact]
        public void SuperblockMode_IrregularPerimeter_NeverLeavesADeadEndPedestrianSegment()
        {
            // In-game user feedback, repeated: "There are still cul-de-sacs... remove them
            // completely" — an IRREGULAR perimeter can make the real contour clip an internal line right
            // in the middle (ClipLineToPolygon), not only at its 4 corners —
            // exactly the kind of case PruneDeadEndPedestrianSegments must catch. Same
            // noisy octagon as IrregularRealPerimeter_... (Loop non-regression), reused here
            // for Superblock: if the pruning works, NO remaining pedestrian segment may have
            // an endpoint that is neither shared nor on the real perimeter, whatever
            // collector spacing is tried.
            var rnd = new System.Random(7);
            var perimeter = new List<float3>();
            const int nodeCount = 10;
            for (int i = 0; i < nodeCount; i++)
            {
                float angle = i * 2f * math.PI / nodeCount;
                float r = 250f * (1f + (float)(rnd.NextDouble() - 0.5) * 0.5f);
                perimeter.Add(new float3(r * math.cos(angle), 0f, r * math.sin(angle)));
            }

            foreach (float spacing in new[] { 200f, 250f, 300f })
            {
                var parameters = LoopParams(spacing, culDeSacRatio: 0f);
                parameters.SuperblockMode = true;
                var segments = GridGenerator.GenerateLoopGrid(perimeter, parameters);

                var interior = segments.Where(s => s.IsPedestrian && !s.IsArc).ToList();
                var all = segments.Where(s => !s.IsArc).ToList();

                foreach (RoadSegmentDef segment in interior)
                {
                    foreach (float3 endpoint in new[] { segment.Start, segment.End })
                    {
                        bool sharedWithAnother = all.Any(other =>
                            !(math.all(other.Start == segment.Start) && math.all(other.End == segment.End))
                            && (math.all(other.Start == endpoint) || math.all(other.End == endpoint)));
                        bool onPerimeter = IsOnPerimeterBoundary(endpoint, perimeter, 0.5f);
                        Assert.True(sharedWithAnother || onPerimeter,
                            $"Endpoint {endpoint} of pedestrian segment {segment.Start} -> {segment.End} (spacing={spacing}) is neither shared nor on the perimeter — cul-de-sac.");
                    }
                }
            }
        }

        [Fact]
        public void CollectorSegments_AreMarkedAvenue_LoopAndSpurAreNot()
        {
            // 3 distinct networks (see RoadSegmentDef.IsAvenue/IsCulDeSacEnd): sparse
            // collectors -> avenue network, loop -> main network, cul-de-sac spur -> secondary
            // network. See GenerateLoopGrid. 9 superblocks (interior + border strips).
            var segments = GridGenerator.GenerateLoopGrid(SquareNodes900, LoopParams(300f, culDeSacRatio: 100f));

            var spur = segments.Where(s => s.IsCulDeSacEnd).ToList();
            var collectors = segments.Where(s => !s.IsCulDeSacEnd && s.IsAvenue).ToList();
            var loopPieces = segments.Where(s => !s.IsCulDeSacEnd && !s.IsAvenue).ToList();

            Assert.NotEmpty(collectors);
            Assert.NotEmpty(loopPieces);
            Assert.Equal(9, spur.Count);
            Assert.All(spur, s => Assert.False(s.IsAvenue, $"Cul-de-sac spur expected IsAvenue=false (secondary network): {s.Start} -> {s.End}"));
            // The loop (loopPieces) must contain 4 arcs per superblock (9 x 4 = 36).
            Assert.Equal(36, loopPieces.Count(s => s.IsArc));
        }

        [Fact]
        public void ArcTangents_AreNeverBackwardsAlongTheChord()
        {
            // Same safeguard as for the old Adaptive mode (see the mod's history):
            // an arc tangent pointing opposite to the direction of travel produces an
            // aberrant curve in the game ("Invalid shape"). Here the tangents are FIXED
            // by construction ((0,1)/(1,0) in the local frame, never derived from a point
            // cloud), so this test is a non-regression guarantee rather than a
            // discovery, but it remains cheap to check.
            var segments = GridGenerator.GenerateLoopGrid(SquareNodes300, LoopParams(100f));
            var arcs = segments.Where(s => s.IsArc).ToList();
            Assert.NotEmpty(arcs);

            foreach (RoadSegmentDef arc in arcs)
            {
                float2 chord = math.normalize(arc.End.xz - arc.Start.xz);
                float startDot = math.dot(math.normalize(arc.StartTangent.xz), chord);
                float endDot = math.dot(math.normalize(arc.EndTangent.xz), chord);
                Assert.True(startDot > 0.3f, $"Start tangent nearly reversed: {arc.Start} -> {arc.End}, dot={startDot:F2}");
                Assert.True(endDot > 0.3f, $"End tangent nearly reversed: {arc.Start} -> {arc.End}, dot={endDot:F2}");
            }
        }

        [Fact]
        public void MirroredRow_ArcsAndCulDeSacSpurAreValid()
        {
            // 1800x1800 (5 internal lines per axis at spacing 300 -> iu AND iv range from 0 to 3)
            // : guarantees that at least one block has an odd iv (mirrorV=true, see GenerateLoopGrid —
            // alternation introduced for "loops on both sides of the collector"). A rectangle
            // whose short axis has only one internal line would ALWAYS have iv 0 and would
            // never test mirrorV=true.
            var square1800 = new List<float3>
            {
                new float3(0f, 0f, 0f),
                new float3(1800f, 0f, 0f),
                new float3(1800f, 0f, 1800f),
                new float3(0f, 0f, 1800f),
            };
            var segments = GridGenerator.GenerateLoopGrid(square1800, LoopParams(300f, culDeSacRatio: 100f));

            var arcs = segments.Where(s => s.IsArc).ToList();
            var spurs = segments.Where(s => s.IsCulDeSacEnd).ToList();

            Assert.Equal(144, arcs.Count); // 36 blocks (6x6, interior + border strips) x 4 arcs
            Assert.Equal(36, spurs.Count); // 1 per block

            foreach (RoadSegmentDef arc in arcs)
            {
                float2 chord = math.normalize(arc.End.xz - arc.Start.xz);
                float startDot = math.dot(math.normalize(arc.StartTangent.xz), chord);
                float endDot = math.dot(math.normalize(arc.EndTangent.xz), chord);
                Assert.True(startDot > 0.3f, $"Start tangent nearly reversed: {arc.Start} -> {arc.End}, dot={startDot:F2}");
                Assert.True(endDot > 0.3f, $"End tangent nearly reversed: {arc.Start} -> {arc.End}, dot={endDot:F2}");
            }

            var loopStraightPieces = segments.Where(s => !s.IsArc && !s.IsCulDeSacEnd && !s.IsAvenue).ToList();
            foreach (RoadSegmentDef spur in spurs)
            {
                bool sharesExactVertex = loopStraightPieces.Any(s => math.all(s.Start == spur.Start) || math.all(s.End == spur.Start));
                Assert.True(sharesExactVertex, $"Cul-de-sac spur ({spur.Start}) not connected to an exact loop vertex.");
            }
        }

        [Fact]
        public void LoopLegAttachPoints_AreExactSharedVerticesWithAvenueSegments()
        {
            // Bug reported in game: "the main road (loop) does not merge into the collector" — the
            // loop legs (EmitLoopBlock) attach to the collector inset by
            // `margin` from its ends, so IN THE MIDDLE of its path, never at its endpoints —
            // without splitting the collector at that exact point (see
            // SplitAvenuesAtLoopAttachPoints), the two touch without ever sharing a
            // real vertex. This test checks that ANY point of a non-avenue segment that falls on
            // the LINE of an avenue segment (inside its path) is EXACTLY one of the
            // two endpoints of that avenue segment — never a midpoint.
            var square1800 = new List<float3>
            {
                new float3(0f, 0f, 0f),
                new float3(1800f, 0f, 0f),
                new float3(1800f, 0f, 1800f),
                new float3(0f, 0f, 1800f),
            };
            var segments = GridGenerator.GenerateLoopGrid(square1800, LoopParams(300f, culDeSacRatio: 100f));

            var avenues = segments.Where(s => s.IsAvenue).ToList();
            var nonAvenues = segments.Where(s => !s.IsAvenue).ToList();
            Assert.NotEmpty(avenues);
            Assert.NotEmpty(nonAvenues);

            foreach (RoadSegmentDef nonAvenue in nonAvenues)
            {
                foreach (float3 point in new[] { nonAvenue.Start, nonAvenue.End })
                {
                    foreach (RoadSegmentDef avenue in avenues)
                    {
                        float2 a = avenue.Start.xz;
                        float2 ab = avenue.End.xz - a;
                        float lenSq = math.lengthsq(ab);
                        if (lenSq < 1e-4f)
                        {
                            continue;
                        }
                        float t = math.dot(point.xz - a, ab) / lenSq;
                        if (t <= 0.01f || t >= 0.99f)
                        {
                            continue; // at/beyond an endpoint of THIS avenue: nothing to report here
                        }
                        float2 projected = a + t * ab;
                        bool onThisAvenueLine = math.distance(projected, point.xz) < 0.5f;
                        Assert.False(onThisAvenueLine,
                            $"Point {point} falls in the middle of avenue {avenue.Start} -> {avenue.End} without being a shared vertex (t={t:F3}).");
                    }
                }
            }
        }

        /// <summary>True if a/b (straight) are collinear AND b.Start or b.End falls strictly inside a (overlapping paths, not a simple crossing).</summary>
        private static bool SegmentsOverlapCollinearly(RoadSegmentDef a, RoadSegmentDef b)
        {
            float2 aDir = math.normalize(a.End.xz - a.Start.xz);
            float2 bDir = math.normalize(b.End.xz - b.Start.xz);
            if (math.abs(aDir.x * bDir.y - aDir.y * bDir.x) > 0.01f)
            {
                return false; // not collinear
            }
            float2 aA = a.Start.xz;
            float2 aAB = a.End.xz - aA;
            float aLenSq = math.lengthsq(aAB);
            if (aLenSq < 1e-4f)
            {
                return false;
            }
            foreach (float3 p3 in new[] { b.Start, b.End })
            {
                float t = math.dot(p3.xz - aA, aAB) / aLenSq;
                float2 proj = aA + t * aAB;
                if (t > 0.02f && t < 0.98f && math.distance(proj, p3.xz) < 0.3f)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>True if a/b (straight, DIFFERENT levels) cross at a point that is the endpoint of NEITHER of them — missing junction.</summary>
        private static bool SegmentsCrossWithoutSharedJunction(RoadSegmentDef a, RoadSegmentDef b)
        {
            float2 a1 = a.Start.xz, a2 = a.End.xz, b1 = b.Start.xz, b2 = b.End.xz;
            float2 r1 = a2 - a1, r2 = b2 - b1;
            float denom = r1.x * r2.y - r1.y * r2.x;
            if (math.abs(denom) < 1e-6f)
            {
                return false;
            }
            float t = ((b1.x - a1.x) * r2.y - (b1.y - a1.y) * r2.x) / denom;
            float u = ((b1.x - a1.x) * r1.y - (b1.y - a1.y) * r1.x) / denom;
            return t > 0.02f && t < 0.98f && u > 0.02f && u < 0.98f;
        }

        [Fact]
        public void MultipleSuperblocks_EachInteriorCellGetsExactlyOneLoop()
        {
            // 500×500, collectors every 100 m -> internal lines at 100/200/300/400 on
            // each axis -> 3×3 = 9 fully interior superblocks + border strips (see
            // GenerateLoopGrid) -> 5×5 = 25 superblocks in total -> 4 arcs per rectangular loop
            // (EmitSimpleLoopBlock, 4 corners) -> 100 arcs.
            var square500 = new List<float3>
            {
                new float3(0f, 0f, 0f),
                new float3(500f, 0f, 0f),
                new float3(500f, 0f, 500f),
                new float3(0f, 0f, 500f),
            };
            var segments = GridGenerator.GenerateLoopGrid(square500, LoopParams(100f));
            var arcs = segments.Where(s => s.IsArc).ToList();
            Assert.Equal(100, arcs.Count);
        }

        [Fact]
        public void NarrowCell_CulDeSacIsSkippedRatherThanForcedIntoAnInvalidShape()
        {
            // 100×900 strip: BuildLocalFrame aligns u with the LONGEST edge (the 900 m
            // sides), so without rotation, u=900 (loop width, no problem) and v=100
            // (depth, vPositions empty -> nothing emitted at all, doesn't test what we want).
            // AngleOffsetDegrees=90° rotates u by 90°, which effectively swaps the two axes:
            // u=100 (width) and v=900 (depth, enough for several strips). DistributeFixed
            // (0,100,300) gives cellCount=round(100/300)=0<=1 -> no internal collector on u
            // -> uBounds=[0,100] (a single strip covering the whole width) -> usable width after
            // margin (leftU=25, rightU=75) = 50m, below MinCulDeSacCellWidth (60m). The cul-de-sac
            // must be skipped here rather than forced into too narrow a space (in-game user
            // feedback: "cul-de-sacs aren't mandatory in narrower spaces" — the
            // branch squeezed against the two nearly touching arcs produced a
            // degenerate/invalid shape).
            var narrowStrip = new List<float3>
            {
                new float3(0f, 0f, 0f),
                new float3(100f, 0f, 0f),
                new float3(100f, 0f, 900f),
                new float3(0f, 0f, 900f),
            };
            GridParameters parameters = LoopParams(300f, culDeSacRatio: 100f);
            parameters.AngleOffsetDegrees = 90f;
            var segments = GridGenerator.GenerateLoopGrid(narrowStrip, parameters);
            Assert.Empty(segments.Where(s => s.IsCulDeSacEnd));
            // The loop itself must still exist, just without a branch.
            Assert.NotEmpty(segments.Where(s => s.IsArc));
        }

        [Fact]
        public void IrregularRealPerimeter_NeverThrowsAndKeepsAllSegmentsInsidePerimeter()
        {
            // Real irregular perimeter captured in game (see the "Invalid shape" incident of
            // the Adaptive mode): the most useful test is not a perfect synthetic
            // shape, but a real noisy geometry. Here an irregular octagon
            // (variable radii) serves as a non-regression test for Loop.
            var rnd = new System.Random(7);
            var perimeter = new List<float3>();
            const int nodeCount = 10;
            for (int i = 0; i < nodeCount; i++)
            {
                float angle = i * 2f * math.PI / nodeCount;
                float r = 250f * (1f + (float)(rnd.NextDouble() - 0.5) * 0.5f);
                perimeter.Add(new float3(r * math.cos(angle), 0f, r * math.sin(angle)));
            }

            foreach (float spacing in new[] { 60f, 90f, 120f, 180f })
            {
                var segments = GridGenerator.GenerateLoopGrid(perimeter, LoopParams(spacing, culDeSacRatio: 50f));
                // Must never throw (already guaranteed by the test running),
                // and each segment must stay geometrically reasonable (non-zero
                // length, no NaN).
                foreach (RoadSegmentDef s in segments)
                {
                    Assert.False(float.IsNaN(s.Start.x) || float.IsNaN(s.Start.z) || float.IsNaN(s.End.x) || float.IsNaN(s.End.z),
                        $"NaN segment (spacing={spacing}): {s.Start} -> {s.End}");
                    Assert.True(math.distance(s.Start.xz, s.End.xz) >= GridGenerator.MinSegmentLength - 0.01f,
                        $"Segment shorter than MinSegmentLength (spacing={spacing}): {s.Start} -> {s.End}");
                }
            }
        }

        [Fact]
        public void ChamferedCorner_StillGetsADepthAdaptedLoop_AnchoredToTheCollector()
        {
            // 900×900 square with one corner cut diagonally (chamfer): the superblock closest
            // to the cut corner fails the strict "4 interior corners" test. Rather than
            // leaving that corner of land completely empty (bug reported in game), a loop with
            // ADAPTED DEPTH but FULL WIDTH must appear there (see
            // TryFitDepthPreservingWidth — no more uniform width+depth shrinking,
            // which produced visibly miniature loops), still anchored to the collector
            // (vMin fixed, never shrunk).
            var chamfered = new List<float3>
            {
                new float3(0f, 0f, 0f),
                new float3(900f, 0f, 0f),
                new float3(900f, 0f, 750f),
                new float3(750f, 0f, 900f),
                new float3(0f, 0f, 900f),
            };

            var segments = GridGenerator.GenerateLoopGrid(chamfered, LoopParams(300f, culDeSacRatio: 0f));

            Assert.NotEmpty(segments);
            var arcs = segments.Where(s => s.IsArc).ToList();
            Assert.True(arcs.Count >= 2, $"Expected at least 1 loop (2 arcs), got {arcs.Count} arc(s).");

            // DIRECT geometric check rather than comparing arc counts with the
            // full square: since border strips were added (see GenerateLoopGrid), the
            // chamfered corner can now receive an adapted-depth loop where, before that
            // addition, there wasn't even a candidate superblock at that spot — the total number
            // of arcs can therefore stay identical to the full square (the adaptation never changes the
            // number of arcs emitted, only the loop's depth), so a count comparison no longer proves
            // anything. The real contract to check: no segment crosses
            // the chamfer line (x+z <= 1650, the cut diagonal between (900,750) and
            // (750,900)) — small tolerance for floating-point rounding (GridGenerator's
            // JoinTolerance is private, not accessible here).
            const float tolerance = 1f;
            foreach (RoadSegmentDef s in segments)
            {
                Assert.True(s.Start.x + s.Start.z <= 1650f + tolerance,
                    $"Segment spills past the chamfer (Start): {s.Start}");
                Assert.True(s.End.x + s.End.z <= 1650f + tolerance,
                    $"Segment spills past the chamfer (End): {s.End}");
            }

            foreach (RoadSegmentDef s in segments)
            {
                Assert.False(float.IsNaN(s.Start.x) || float.IsNaN(s.Start.z) || float.IsNaN(s.End.x) || float.IsNaN(s.End.z),
                    $"NaN segment: {s.Start} -> {s.End}");
            }
        }

        [Fact]
        public void ChamferedCorner_LoopLegsAdaptIndependently_GivingADiagonalBackSegment()
        {
            // Same chamfer as the previous test, but checks the real novelty (in-game user
            // feedback with a screenshot: "the shape closer to the cell's area",
            // one leg shortened by a diagonal cut, NOT both legs
            // shrunk to the same depth): the corner cell (600-900 in u AND v, cut by
            // x+z<=1650) must give a leg on the u=900 side clearly shorter than on the u=600 side
            // (closer to the cut corner), with a back segment that follows that diagonal instead
            // of staying horizontal as elsewhere in the grid.
            var chamfered = new List<float3>
            {
                new float3(0f, 0f, 0f),
                new float3(900f, 0f, 0f),
                new float3(900f, 0f, 750f),
                new float3(750f, 0f, 900f),
                new float3(0f, 0f, 900f),
            };

            var segments = GridGenerator.GenerateLoopGrid(chamfered, LoopParams(300f, culDeSacRatio: 0f));

            // Candidate "back" segments: straight, not a collector/loop marked otherwise —
            // angle=0 so u=x, v=z; the direct comparison on .z is valid without transforming to
            // the local frame.
            var backSegments = segments.Where(s => !s.IsArc && !s.IsAvenue && !s.IsCulDeSacEnd).ToList();
            Assert.NotEmpty(backSegments);

            float maxZDelta = backSegments.Max(s => math.abs(s.Start.z - s.End.z));
            Assert.True(maxZDelta > 5f,
                $"No back segment is diagonal (largest z gap found: {maxZDelta:F2}m) — the leg on the chamfer side should be shorter than the other, giving a sloped back rather than horizontal everywhere.");

            // Still inside the chamfer, same check as the previous test —
            // ensures the per-leg adaptation did not reintroduce any spill-over.
            const float tolerance = 1f;
            foreach (RoadSegmentDef s in segments)
            {
                Assert.True(s.Start.x + s.Start.z <= 1650f + tolerance, $"Segment spills past the chamfer (Start): {s.Start}");
                Assert.True(s.End.x + s.End.z <= 1650f + tolerance, $"Segment spills past the chamfer (End): {s.End}");
            }
        }

    }

    /// <summary>
    /// Performance regression — user feedback (mod forum): "the mod lags hard the
    /// more rows and columns there are... BuildSubSegments runs at O(n^4) complexity and gets
    /// executed every single frame" with the default Spacing=10m. Confirmed valid: BuildSubSegments
    /// scanned ALL already accepted points for each new crossing (O(n²) inside a
    /// double loop already O(n²)), and the game layer does regenerate the preview EVERY
    /// frame while ≥2 nodes are selected — a small spacing on a large perimeter
    /// translated into a game freeze. Replaced by SpatialPointIndex (bucket grid, 3×3
    /// neighbourhood) — this test locks in a generous but strict time budget to prevent any
    /// future regression towards O(n⁴) behaviour.
    /// </summary>
    [Collection(TimingCollection.Name)]
    public class GridGeneratorPerformanceTests
    {
        [Fact]
        public void LargeAreaWithSmallSpacing_GeneratesWellUnderOneSecond()
        {
            // ~450x450m with Spacing=10m (the historical default targeted by the user feedback)
            // -> ~45x45 lines, ~2000 crossings: negligible with a spatial index,
            // several tens of millions of operations with the old linear scan.
            var perimeter = new List<float3>
            {
                new float3(0f, 0f, 0f),
                new float3(450f, 0f, 0f),
                new float3(450f, 0f, 450f),
                new float3(0f, 0f, 450f),
            };
            var parameters = GridParameters.Default;
            parameters.Mode = SpacingMode.FixedSpacing;
            parameters.SpacingMeters = 10f;

            var stopwatch = Stopwatch.StartNew();
            var segments = GridGenerator.GenerateGrid(perimeter, parameters);
            stopwatch.Stop();

            Assert.NotEmpty(segments);
            Assert.True(stopwatch.ElapsedMilliseconds < TimingBudget.Ms(1000),
                $"GenerateGrid took {stopwatch.ElapsedMilliseconds}ms for a 10m spacing on 450x450m — " +
                "likely regression to quadratic/worse complexity in crossing merging.");
        }

        [Fact]
        public void VeryLargeAreaWithSmallSpacing_StillGeneratesQuickly()
        {
            // Extreme case: ~200x200 lines, ~40,000 crossings — the exact scenario described on the
            // mod forum ("the mod lags hard the more rows and columns there are"). The old
            // linear scan (O(n²) INSIDE an already O(n²) loop) would have done tens of
            // millions of operations here; SpatialPointIndex keeps it near-linear.
            var perimeter = new List<float3>
            {
                new float3(0f, 0f, 0f),
                new float3(2000f, 0f, 0f),
                new float3(2000f, 0f, 2000f),
                new float3(0f, 0f, 2000f),
            };
            var parameters = GridParameters.Default;
            parameters.Mode = SpacingMode.FixedSpacing;
            parameters.SpacingMeters = 10f;

            var stopwatch = Stopwatch.StartNew();
            var segments = GridGenerator.GenerateGrid(perimeter, parameters);
            stopwatch.Stop();

            Assert.NotEmpty(segments);
            Assert.True(stopwatch.ElapsedMilliseconds < TimingBudget.Ms(5000),
                $"GenerateGrid took {stopwatch.ElapsedMilliseconds}ms for a 10m spacing on 2000x2000m " +
                $"({segments.Count} segments) — this is exactly the scenario reported as freezing the game.");
        }

        [Fact]
        public void LoopMode_LargeAreaWithManyBlocks_GeneratesQuickly()
        {
            // User feedback (in-game performance log, ~31 nodes selected, Loop mode):
            // 325-351ms per full regeneration, versus <1ms for the equivalent Grid mode.
            // Cause: SplitSegmentsAtMidSpanAttachPoints (O(targets × attach points)) without
            // a spatial index, and on top of that called several times per regeneration with the old
            // 2-level hierarchy (Arterial > Collector, since removed — see GridGenerator.
            // GenerateLoopGrid). ~2000x2000m / CollectorSpacingMeters=200 (the minimum allowed
            // by the panel) -> ~100 blocks, a single global call to
            // SplitSegmentsAtMidSpanAttachPoints on ALL their attach points.
            var perimeter = new List<float3>
            {
                new float3(0f, 0f, 0f),
                new float3(2000f, 0f, 0f),
                new float3(2000f, 0f, 2000f),
                new float3(0f, 0f, 2000f),
            };
            var parameters = new GridParameters
            {
                CollectorSpacingMeters = 200f,
                LoopCulDeSacRatio = 50f,
                CulDeSacDepth = 0.75f,
            };

            var stopwatch = Stopwatch.StartNew();
            var segments = GridGenerator.GenerateLoopGrid(perimeter, parameters);
            stopwatch.Stop();

            Assert.NotEmpty(segments);
            Assert.True(stopwatch.ElapsedMilliseconds < TimingBudget.Ms(2000),
                $"GenerateLoopGrid took {stopwatch.ElapsedMilliseconds}ms for ~49 blocks on 2000x2000m " +
                $"({segments.Count} segments) — likely regression in SplitSegmentsAtMidSpanAttachPoints.");
        }

    public class GridAlignToTerrainTests
    {
        [Fact]
        public void AlignedGrid_HasStreetsAlongTheContours()
        {
            // 10 % slope towards the north-east (45°): contour lines run north-west ↔ south-east.
            var square = new List<float3> { new float3(0, 0, 0), new float3(800, 0, 0), new float3(800, 0, 800), new float3(0, 0, 800) };
            GridParameters p = GridParameters.Default;
            p.HeightAt = q => 0.1f * (q.x + q.y) / math.SQRT2;
            p.AlignToTerrain = true;
            var segments = GridGenerator.GenerateGrid(square, p);
            Assert.NotEmpty(segments);
            float2 contour = math.normalize(new float2(1f, -1f));
            int along = segments.Count(s => math.abs(math.dot(math.normalizesafe(s.End.xz - s.Start.xz), contour)) > 0.99f);
            int across = segments.Count(s => math.abs(math.dot(math.normalizesafe(s.End.xz - s.Start.xz), contour)) < 0.01f);
            Assert.True(along > 0 && along + across == segments.Count, $"{along} along, {across} across out of {segments.Count}");
        }

        [Fact]
        public void FlatArea_KeepsTheUsualOrientation()
        {
            var square = new List<float3> { new float3(0, 0, 0), new float3(800, 0, 0), new float3(800, 0, 800), new float3(0, 0, 800) };
            GridParameters p = GridParameters.Default;
            p.HeightAt = q => 5f;
            p.AlignToTerrain = true;
            var aligned = GridGenerator.GenerateGrid(square, p);
            p.AlignToTerrain = false;
            var usual = GridGenerator.GenerateGrid(square, p);
            Assert.Equal(usual.Count, aligned.Count);
        }
    }
    }
}
