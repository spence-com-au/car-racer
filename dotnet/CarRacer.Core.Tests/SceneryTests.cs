using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CarRacer.Core.Art;
using CarRacer.Core.Numerics;
using CarRacer.Core.Track;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class SceneryTests
    {
        private static readonly Dictionary<string, ObjModel> Models = LoadModels();

        private static Dictionary<string, ObjModel> LoadModels()
        {
            var models = new Dictionary<string, ObjModel>();
            foreach (string name in TrackScenery.Models)
            {
                models[name] = ObjModel.Parse(File.ReadAllText(ModelPath(name + ".obj.bytes")));
            }

            return models;
        }

        private static string ModelPath(string file)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Unity")))
            {
                dir = dir.Parent;
            }

            return Path.Combine(dir.FullName, "Unity", "Assets", "CarRacer", "Resources", "Models", file);
        }

        private static float Width(string name) => Models[name].Max.X - Models[name].Min.X;

        public static IEnumerable<object[]> Tracks() => Enumerable.Range(0, TrackCatalog.Layouts.Count).Select(i => new object[] { i });

        [Theory]
        [MemberData(nameof(Tracks))]
        public void EveryTrackGetsAGantryAndPlentyOfScenery(int track)
        {
            var spline = TrackCatalog.Layouts[track].Spline;
            var placements = TrackScenery.Place(spline, Width);
            Assert.Single(placements, p => p.Model == TrackScenery.Gantry);
            Assert.True(placements.Count(p => p.Model == TrackScenery.TreeLarge || p.Model == TrackScenery.TreeSmall) > 20, $"track {track}");

            // The gantry spans the road at the start line, a metre past each wall.
            var gantry = placements.Single(p => p.Model == TrackScenery.Gantry);
            var start = spline.SampleAtDistance(0f);
            Assert.True(gantry.Position.ApproximatelyEquals(start.Position));
            Assert.Equal(start.Width + 2f, Width(TrackScenery.Gantry) * gantry.Scale, 3);
        }

        [Theory]
        [MemberData(nameof(Tracks))]
        public void SceneryStaysClearOfEveryPartOfTheRoad(int track)
        {
            var spline = TrackCatalog.Layouts[track].Spline;
            var road = spline.SampleEvenly(1f);
            foreach (var p in TrackScenery.Place(spline, Width).Where(p => p.Model != TrackScenery.Gantry))
            {
                foreach (var s in road)
                {
                    float dx = s.Position.X - p.Position.X;
                    float dz = s.Position.Z - p.Position.Z;
                    Assert.True(MathF.Sqrt(dx * dx + dz * dz) > s.Width / 2f + TrackScenery.Clearance, $"{p.Model} at {p.Position} on track {track}");
                }
            }
        }

        private static float Radius(PropPlacement p)
        {
            switch (p.Model)
            {
                case TrackScenery.StandModel:
                    return TrackScenery.StandRadius;
                case TrackScenery.TowerRed:
                case TrackScenery.TowerGreen:
                    return TrackScenery.TowerRadius;
                default:
                    return p.Scale * 0.25f;
            }
        }

        [Theory]
        [MemberData(nameof(Tracks))]
        public void SceneryNeverOverlapsOtherScenery(int track)
        {
            var pieces = TrackScenery.Place(TrackCatalog.Layouts[track].Spline, Width).Where(p => p.Model != TrackScenery.Gantry).ToList();
            for (int i = 0; i < pieces.Count; i++)
            {
                for (int j = i + 1; j < pieces.Count; j++)
                {
                    float dx = pieces[i].Position.X - pieces[j].Position.X;
                    float dz = pieces[i].Position.Z - pieces[j].Position.Z;
                    float reach = Radius(pieces[i]) + Radius(pieces[j]);
                    Assert.True(dx * dx + dz * dz >= reach * reach, $"{pieces[i].Model} at {pieces[i].Position} and {pieces[j].Model} at {pieces[j].Position} on track {track}");
                }
            }
        }

        [Fact]
        public void SomeTracksHaveRaisedOrSunkenRoad()
        {
            Assert.Contains(
                Enumerable.Range(0, TrackCatalog.Layouts.Count),
                track => TrackCatalog.Layouts[track].Spline.SampleEvenly(1f).Any(s => Math.Abs(s.Position.Y) > TrackScenery.MaxRoadHeight));
        }

        [Theory]
        [MemberData(nameof(Tracks))]
        public void SceneryStaysAwayFromRaisedAndSunkenRoad(int track)
        {
            var spline = TrackCatalog.Layouts[track].Spline;

            // Sampled every 4 m, as placement does; a finer sample finds slivers of height that it never saw.
            var uneven = spline.SampleEvenly(4f).Where(s => Math.Abs(s.Position.Y) > TrackScenery.MaxRoadHeight).ToList();
            foreach (var p in TrackScenery.Place(spline, Width).Where(p => p.Model != TrackScenery.Gantry))
            {
                foreach (var s in uneven)
                {
                    float dx = s.Position.X - p.Position.X;
                    float dz = s.Position.Z - p.Position.Z;
                    float near = s.Width / 2f + TrackScenery.Clearance + Radius(p) + 10f;
                    Assert.True(dx * dx + dz * dz >= near * near, $"{p.Model} at {p.Position} on track {track}");
                }
            }
        }

        [Fact]
        public void TheOvalHasGrandstandsFacingTheStartStraight()
        {
            var spline = TrackCatalog.Layouts[0].Spline;
            var stands = TrackScenery.Place(spline, Width).Where(p => p.Model == TrackScenery.StandModel).ToList();
            Assert.NotEmpty(stands);
            foreach (var stand in stands)
            {
                // Seats face the stand's -z; that should point at the road.
                var seatsFace = new Vec3(-MathF.Sin(stand.Yaw), 0f, -MathF.Cos(stand.Yaw));
                var near = spline.SampleAtDistance(spline.FindNearestDistance(stand.Position, 0f, 60f));
                var toRoad = (near.Position - stand.Position).Flattened.Normalized;
                Assert.True(seatsFace.X * toRoad.X + seatsFace.Z * toRoad.Z > 0.9f);
            }
        }

        [Theory]
        [MemberData(nameof(Tracks))]
        public void LowQualityGetsAboutHalfTheTrees(int track)
        {
            var spline = TrackCatalog.Layouts[track].Spline;
            int Trees(float share) => TrackScenery.Place(spline, Width, treeShare: share).Count(p => p.Model == TrackScenery.TreeLarge || p.Model == TrackScenery.TreeSmall);
            int all = Trees(1f);
            int low = Trees(CarRacer.Core.Platform.QualityPresets.For(CarRacer.Core.Platform.QualityLevel.Low).TreeShare);
            Assert.Equal(all / 2, low);

            // The gantry, towers and stands stay whatever the quality.
            Assert.Equal(
                TrackScenery.Place(spline, Width).Count(p => p.Model != TrackScenery.TreeLarge && p.Model != TrackScenery.TreeSmall),
                TrackScenery.Place(spline, Width, treeShare: 0.5f).Count(p => p.Model != TrackScenery.TreeLarge && p.Model != TrackScenery.TreeSmall));
        }

        [Fact]
        public void PlacementIsTheSameEveryTime()
        {
            var spline = TrackCatalog.Layouts[1].Spline;
            var a = TrackScenery.Place(spline, Width);
            var b = TrackScenery.Place(spline, Width);
            Assert.Equal(a.Select(p => (p.Model, p.Position)), b.Select(p => (p.Model, p.Position)));
        }

        [Fact]
        public void EveryPartOfTheSceneryModelsHasAColour()
        {
            var colours = new Dictionary<string, Vec3>();
            foreach (string name in TrackScenery.Models)
            {
                ObjMaterials.Parse(File.ReadAllText(ModelPath(name + ".mtl.bytes")), colours);
            }

            foreach (var model in Models.Values)
            {
                Assert.All(model.Parts.Where(p => p.Triangles.Count > 0), part => Assert.True(colours.ContainsKey(part.Material), part.Material));
            }

            Assert.Equal(new Vec3(0.9098039f, 0.3333333f, 0.3254902f), colours["red"]);
        }
    }

    public class SceneryMeshTests
    {
        private const string Box = @"mtllib box.mtl
g box
usemtl red
v 0 0 0
v 2 0 0
v 2 0 1
vn 0 1 0
vt 0 0
f 1/1/1 3/1/1 2/1/1
usemtl blue
f 1/1/1 2/1/1 3/1/1
";

        [Fact]
        public void ColoursBecomeSeparateMeshes()
        {
            var model = ObjModel.Parse(Box);
            Assert.Equal(new[] { "red", "blue" }, model.Parts.Select(p => p.Material));
            var colours = new Dictionary<string, Vec3> { ["red"] = new Vec3(1f, 0f, 0f), ["blue"] = new Vec3(0f, 0f, 1f) };
            var meshes = SceneryMesh.Build(
                new[] { new PropPlacement { Model = "box", Position = Vec3.Zero, Scale = 1f }, new PropPlacement { Model = "box", Position = new Vec3(10f, 0f, 0f), Scale = 1f } },
                new Dictionary<string, ObjModel> { ["box"] = model },
                colours);
            Assert.Equal(2, meshes.Count);
            Assert.Equal(6, meshes[0].Mesh.Vertices.Count); // three corners, twice
            Assert.Equal(new Vec3(1f, 0f, 0f), meshes[0].Colour);
        }

        [Fact]
        public void PiecesAreCentredScaledTurnedAndMoved()
        {
            // The triangle spans x -2..0 (mirrored) and z 0..1, so its footprint's middle is (-1, 0, 0.5).
            var model = ObjModel.Parse(Box);
            var meshes = SceneryMesh.Build(
                new[] { new PropPlacement { Model = "box", Position = new Vec3(100f, 5f, 0f), Yaw = MathF.PI / 2f, Scale = 2f } },
                new Dictionary<string, ObjModel> { ["box"] = model },
                new Dictionary<string, Vec3>());

            // Vertex (0,0,0) is (+1, 0, -0.5) from the middle; doubled to (2, 0, -1); a quarter turn
            // takes +z to +x and +x to -z, so (2, 0, -1) becomes (-1, 0, -2).
            var mesh = meshes[0].Mesh;
            Assert.Contains(mesh.Vertices, v => v.ApproximatelyEquals(new Vec3(99f, 5f, -2f)));
            Assert.All(mesh.Normals, n => Assert.True(n.ApproximatelyEquals(Vec3.Up)));
        }
    }
}
