using System;
using System.Linq;
using CarRacer.Core.Numerics;
using CarRacer.Core.Track;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class TrackTests
    {
        private static readonly TrackLayout Oval = new TrackLayout(SampleTracks.Oval());

        [Fact]
        public void SampleTrackIsValid()
        {
            Assert.Empty(SampleTracks.Oval().Validate());
        }

        [Fact]
        public void ValidationCatchesBadTracks()
        {
            var bad = new TrackDefinition
            {
                Id = "",
                CheckpointCount = 2,
                Points = new[] { new TrackPoint(0, 0, 0, width: 2f), new TrackPoint(0, 0, 0.5f), new TrackPoint(10, 0, 10, bankDegrees: 60f) },
                ItemBoxRows = new[] { 1.5f },
            };

            var problems = bad.Validate();
            Assert.Contains(problems, p => p.Contains("Id"));
            Assert.Contains(problems, p => p.Contains("4 control points"));
            Assert.Contains(problems, p => p.Contains("checkpoints"));
            Assert.Contains(problems, p => p.Contains("Item box"));
            Assert.Throws<ArgumentException>(() => new TrackSpline(bad));
        }

        [Fact]
        public void ValidationCatchesBadControlPoints()
        {
            // Four points, so the per-point checks run rather than the "at least 4 control points" short-circuit.
            var bad = new TrackDefinition
            {
                Id = "bad",
                DefaultLaps = 0,
                Points = new[]
                {
                    new TrackPoint(0, 0, 0, width: 2f),
                    new TrackPoint(10, 0, 0, bankDegrees: 60f),
                    new TrackPoint(10, 0, 0.5f),
                    null,
                },
            };

            var problems = bad.Validate();
            Assert.Contains(problems, p => p.Contains("Point 0 width") && p.Contains("narrower than 4 m"));
            Assert.Contains(problems, p => p.Contains("Point 1 bank") && p.Contains("exceeds 45"));
            Assert.Contains(problems, p => p.Contains("Points 1 and 2") && p.Contains("closer than 1 m"));
            Assert.Contains(problems, p => p.Contains("Point 3 is missing"));
            Assert.Contains(problems, p => p.Contains("DefaultLaps must be at least 1"));
            Assert.DoesNotContain(problems, p => p.Contains("4 control points"));
        }

        [Fact]
        public void LengthIsPlausibleForTheOval()
        {
            // The chords between control points sum to about 380 m; the spline through them is
            // slightly longer (386.4 m when measured).
            Assert.InRange(Oval.Spline.Length, 383f, 390f);
        }

        [Fact]
        public void SplinePassesThroughControlPointsAndStartsAtTheFirst()
        {
            var start = Oval.Spline.SampleAtDistance(0f);
            Assert.True(start.Position.ApproximatelyEquals(Vec3.Zero, 1e-3f));
            Assert.True(Vec3.Dot(start.Forward, Vec3.Forward) > 0.95f, "oval starts heading +Z");
        }

        [Fact]
        public void FrameIsOrthonormalAndRightPointsRight()
        {
            foreach (var s in Oval.Spline.SampleEvenly(5f))
            {
                Assert.Equal(1f, s.Forward.Length, 3);
                Assert.Equal(1f, s.Right.Length, 3);
                Assert.Equal(1f, s.Up.Length, 3);
                Assert.Equal(0f, Vec3.Dot(s.Forward, s.Right), 3);
                Assert.Equal(0f, Vec3.Dot(s.Up, s.Right), 3);
                Assert.True(s.Up.Y > 0.7f, "road surface faces up");
            }

            var start = Oval.Spline.SampleAtDistance(0f);
            Assert.True(start.Right.X > 0.95f, "heading +Z, right is +X in Unity's left-handed frame");
        }

        [Fact]
        public void BankingRaisesTheLeftEdge()
        {
            var def = SampleTracks.Oval();
            foreach (var p in def.Points)
            {
                p.BankDegrees = 10f;
            }

            var s = new TrackSpline(def).SampleAtDistance(0f);
            Assert.True(s.Right.Y < -0.1f, "right edge is lower");
        }

        [Fact]
        public void DistanceWrapsAroundTheLap()
        {
            float length = Oval.Spline.Length;
            Assert.Equal(10f, Oval.Spline.WrapDistance(length + 10f), 3);
            Assert.Equal(length - 10f, Oval.Spline.WrapDistance(-10f), 3);
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(37.5f)]
        [InlineData(123f)]
        [InlineData(300f)]
        [InlineData(400f)]
        public void FindNearestRecoversDistanceOfPointsOnAndOffTheCentreLine(float distance)
        {
            var s = Oval.Spline.SampleAtDistance(distance);
            float d = Oval.Spline.FindNearestDistance(s.Position + s.Right * 3f);
            float error = Math.Abs(Oval.Spline.WrapDistance(d - distance + Oval.Spline.Length / 2f) - Oval.Spline.Length / 2f);
            Assert.True(error < 0.5f, $"expected ~{distance}, got {d}");

            float hinted = Oval.Spline.FindNearestDistance(s.Position, hintDistance: distance + 5f);
            float hintedError = Math.Abs(Oval.Spline.WrapDistance(hinted - distance + Oval.Spline.Length / 2f) - Oval.Spline.Length / 2f);
            Assert.True(hintedError < 0.5f, $"hinted: expected ~{distance}, got {hinted}");
        }

        [Fact]
        public void CheckpointsAreEvenlySpacedFromTheStartLine()
        {
            var gates = Oval.CheckpointGates();
            Assert.Equal(8, gates.Count);
            Assert.Equal(0f, gates[0].Sample.Distance, 3);
            float spacing = Oval.Spline.Length / 8f;
            for (int i = 0; i < gates.Count; i++)
            {
                Assert.Equal(i, gates[i].Index);
                Assert.Equal(i * spacing, gates[i].Sample.Distance, 2);
            }
        }

        [Fact]
        public void FractionToNextCheckpoint()
        {
            float spacing = Oval.Spline.Length / 8f;
            Assert.Equal(0.5f, Oval.FractionToNextCheckpoint(spacing * 2.5f, nextCheckpoint: 3), 3);
            // On the grid (behind the line) heading for checkpoint 0: nearly complete.
            Assert.Equal(1f - 5f / spacing, Oval.FractionToNextCheckpoint(Oval.Spline.Length - 5f, nextCheckpoint: 0), 3);
        }

        [Fact]
        public void StartGridIsBehindTheLineStaggeredAndClear()
        {
            var slots = Oval.StartGrid(6);
            Assert.Equal(6, slots.Count);

            float previousDistance = float.MaxValue;
            foreach (var slot in slots)
            {
                float d = Oval.Spline.FindNearestDistance(slot.Position);
                float behind = Oval.Spline.Length - d;
                Assert.InRange(behind, 1f, 40f);
                Assert.True(behind < previousDistance + 40f);
                previousDistance = behind;
            }

            Assert.True(slots[0].Position.X < slots[1].Position.X, "slot 0 left, slot 1 right");
            for (int i = 0; i < slots.Count; i++)
            {
                for (int j = i + 1; j < slots.Count; j++)
                {
                    Assert.True(Vec3.Distance(slots[i].Position, slots[j].Position) > 3f, $"slots {i} and {j} overlap");
                }
            }
        }

        [Fact]
        public void ItemBoxesSitAcrossTheRoad()
        {
            var boxes = Oval.ItemBoxPositions(perRow: 4);
            Assert.Equal(8, boxes.Count);
            foreach (var box in boxes)
            {
                float d = Oval.Spline.FindNearestDistance(box);
                var s = Oval.Spline.SampleAtDistance(d);
                Assert.True((box - s.Position).Flattened.Length < s.Width / 2f);
            }
        }

        [Fact]
        public void RoadMeshIsClosedWoundUpwardAndMatchesWidth()
        {
            var mesh = RoadMeshBuilder.Build(Oval.Spline, wallHeight: 1.2f, spacing: 2f);
            var road = mesh.Road;

            int rings = road.Vertices.Count / 2;
            Assert.Equal(road.Vertices.Count, road.Normals.Count);
            Assert.Equal(road.Vertices.Count, road.Uvs.Count);
            Assert.Equal((rings - 1) * 2, road.TriangleCount);
            Assert.True(road.Triangles.All(i => i >= 0 && i < road.Vertices.Count));

            // Last ring coincides with the first: the loop is closed.
            Assert.True(road.Vertices[0].ApproximatelyEquals(road.Vertices[road.Vertices.Count - 2], 1e-3f));
            Assert.True(road.Vertices[1].ApproximatelyEquals(road.Vertices[road.Vertices.Count - 1], 1e-3f));

            for (int t = 0; t < road.Triangles.Count; t += 3)
            {
                Vec3 a = road.Vertices[road.Triangles[t]];
                Vec3 b = road.Vertices[road.Triangles[t + 1]];
                Vec3 c = road.Vertices[road.Triangles[t + 2]];
                Assert.True(Vec3.Cross(b - a, c - a).Y > 0f, $"triangle {t / 3} faces down");
            }

            Assert.Equal(14f, Vec3.Distance(road.Vertices[0], road.Vertices[1]), 2);
        }

        [Fact]
        public void RoadTextureRunsForwardWhereWidthChanges()
        {
            // The oval's width changes between 14 m and 16 m, which must not make V run backwards.
            var road = RoadMeshBuilder.Build(Oval.Spline, wallHeight: 1.2f, spacing: 2f).Road;
            for (int i = 2; i < road.Uvs.Count; i += 2)
            {
                Assert.True(road.Uvs[i].Y > road.Uvs[i - 2].Y, $"V decreases at ring {i / 2}: {road.Uvs[i - 2].Y} -> {road.Uvs[i].Y}");
                Assert.Equal(road.Uvs[i].Y, road.Uvs[i + 1].Y);
            }
        }

        [Fact]
        public void WallsFaceTheRoad()
        {
            var mesh = RoadMeshBuilder.Build(Oval.Spline, wallHeight: 1.2f, spacing: 2f);
            CheckWall(mesh.LeftWall, towardsRight: true);
            CheckWall(mesh.RightWall, towardsRight: false);
            Assert.Equal(1.2f, mesh.LeftWall.Vertices[1].Y - mesh.LeftWall.Vertices[0].Y, 3);

            void CheckWall(MeshData wall, bool towardsRight)
            {
                for (int t = 0; t < wall.Triangles.Count; t += 3)
                {
                    Vec3 a = wall.Vertices[wall.Triangles[t]];
                    Vec3 b = wall.Vertices[wall.Triangles[t + 1]];
                    Vec3 c = wall.Vertices[wall.Triangles[t + 2]];
                    Vec3 faceNormal = Vec3.Cross(b - a, c - a);
                    Vec3 vertexNormal = wall.Normals[wall.Triangles[t]];
                    Assert.True(Vec3.Dot(faceNormal, vertexNormal) > 0f, $"wall triangle {t / 3} winding disagrees with its normal");
                }

                var start = Oval.Spline.SampleAtDistance(0f);
                float side = Vec3.Dot(wall.Normals[0], start.Right);
                Assert.True(towardsRight ? side > 0.99f : side < -0.99f);
            }
        }
    }
}
