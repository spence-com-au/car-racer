using System;
using System.Collections.Generic;
using CarRacer.Core.Numerics;
using CarRacer.Core.Track;

namespace CarRacer.Core.Art
{
    /// <summary>One Racing Kit piece placed in the world.</summary>
    public struct PropPlacement
    {
        /// <summary>The model's file in <c>Resources/Models</c>, without <c>.obj.bytes</c>.</summary>
        public string Model;

        /// <summary>Where the middle of the model's footprint, at its base, goes.</summary>
        public Vec3 Position;

        /// <summary>Turn about the vertical, the way headings turn: the model's +z ends up facing (sin, 0, cos).</summary>
        public float Yaw;

        public float Scale;
    }

    /// <summary>
    /// Dresses a track with Kenney Racing Kit pieces (design doc §13): a gantry over the start line
    /// with banner towers beside it, grandstands facing the start straight, and trees scattered
    /// outside the walls. Nothing goes near any part of the road (a figure-eight's crossing, an
    /// oval's infield), and nothing overlaps. Scenery is for show only, behind the walls.
    /// </summary>
    public static class TrackScenery
    {
        public const string Gantry = "overhead";
        public const string StandModel = "grandStand";
        public const string TreeLarge = "treeLarge";
        public const string TreeSmall = "treeSmall";
        public const string TowerRed = "bannerTowerRed";
        public const string TowerGreen = "bannerTowerGreen";

        public static readonly string[] Models = { Gantry, StandModel, TreeLarge, TreeSmall, TowerRed, TowerGreen };

        /// <summary>The Racing Kit is built on 1 m tiles; a stand and a tree are scaled up to these sizes.</summary>
        public const float StandScale = 9f;

        public const float TreeMinScale = 6f;
        public const float TreeMaxScale = 9f;
        public const float TowerScale = 6f;

        /// <summary>How much room a stand (9 m square, kept side-on to the road) and a tower take.</summary>
        public const float StandRadius = StandScale / 2f;

        public const float TowerRadius = 1.5f;

        /// <summary>How far apart trees are tried, along each side.</summary>
        public const float TreeSpacing = 14f;

        /// <summary>Scenery keeps at least this far beyond the edge of any road.</summary>
        public const float Clearance = 3f;

        /// <summary>Scenery stays on the flat: none where the road is more than this far above or below the ground.</summary>
        public const float MaxRoadHeight = 1.5f;

        /// <param name="modelWidth">A model's width (x extent) before scaling, to fit the gantry across the road.</param>
        /// <param name="treeShare">The share of the trees to keep, from 0 to 1 (fewer on Low quality, design doc §14).</param>
        public static List<PropPlacement> Place(TrackSpline spline, Func<string, float> modelWidth, int seed = 1, float treeShare = 1f)
        {
            var placements = new List<PropPlacement>();
            var footprints = new List<(Vec3 Centre, float Radius)>();
            var road = spline.SampleEvenly(4f);
            var random = new Random(seed);

            // The gantry spans the start line at the road's height, its posts just outside the walls.
            var start = spline.SampleAtDistance(0f);
            float heading = Angles.Heading(start.Forward);
            float gantryWidth = modelWidth(Gantry);
            if (gantryWidth > 0f)
            {
                placements.Add(new PropPlacement { Model = Gantry, Position = start.Position, Yaw = heading, Scale = (start.Width + 2f) / gantryWidth });
            }

            // Banner towers beside the gantry, and grandstands along the straight, facing the road.
            foreach (int side in new[] { -1, 1 })
            {
                var spot = start.Position + start.Right * (side * (start.Width / 2f + Clearance + TowerRadius + 0.5f));
                TryAdd(placements, footprints, road, side < 0 ? TowerRed : TowerGreen, spot, heading, TowerScale, TowerRadius);
            }

            for (int i = -1; i <= 1; i++)
            {
                var s = spline.SampleAtDistance(spline.WrapDistance(i * (StandScale + 1f)));
                var spot = s.Position - s.Right * (s.Width / 2f + Clearance + StandRadius + 0.5f);

                // A stand's seats face its -z, so its +z points away from the road (to the left).
                TryAdd(placements, footprints, road, StandModel, spot, Angles.Heading(s.Forward) - MathF.PI / 2f, StandScale, StandRadius);
            }

            for (float d = TreeSpacing / 2f; d < spline.Length; d += TreeSpacing)
            {
                foreach (int side in new[] { -1, 1 })
                {
                    if (random.NextDouble() > 0.65)
                    {
                        continue;
                    }

                    var s = spline.SampleAtDistance(spline.WrapDistance(d + ((float)random.NextDouble() - 0.5f) * 8f));
                    float offset = s.Width / 2f + Clearance + 3f + (float)random.NextDouble() * 14f;
                    float scale = TreeMinScale + (float)random.NextDouble() * (TreeMaxScale - TreeMinScale);
                    string model = random.NextDouble() < 0.5 ? TreeLarge : TreeSmall;
                    float yaw = (float)random.NextDouble() * 2f * MathF.PI;
                    TryAdd(placements, footprints, road, model, s.Position + s.Right * (side * offset), yaw, scale, scale * 0.25f);
                }
            }

            return treeShare >= 1f ? placements : KeepShareOfTrees(placements, treeShare);
        }

        /// <summary>Keeps an even <paramref name="share"/> of the trees, so a lower quality's trees are some of the higher one's, in the same spots.</summary>
        private static List<PropPlacement> KeepShareOfTrees(List<PropPlacement> placements, float share)
        {
            var kept = new List<PropPlacement>();
            int tree = 0;
            foreach (var p in placements)
            {
                if (p.Model == TreeLarge || p.Model == TreeSmall)
                {
                    // Keeps tree k when the running total of the share passes a whole number.
                    bool keep = (int)MathF.Floor((tree + 1) * share) > (int)MathF.Floor(tree * share);
                    tree++;
                    if (!keep)
                    {
                        continue;
                    }
                }

                kept.Add(p);
            }

            return kept;
        }

        /// <summary>Adds a piece on the ground at <paramref name="spot"/> unless it would be near the road, on a slope, or on another piece.</summary>
        private static void TryAdd(List<PropPlacement> placements, List<(Vec3 Centre, float Radius)> footprints, List<TrackSample> road, string model, Vec3 spot, float yaw, float scale, float radius)
        {
            var ground = new Vec3(spot.X, 0f, spot.Z);
            foreach (var s in road)
            {
                float dx = s.Position.X - ground.X;
                float dz = s.Position.Z - ground.Z;
                float reach = s.Width / 2f + Clearance + radius;
                if (dx * dx + dz * dz < reach * reach)
                {
                    return;
                }

                // Near a raised or sunken stretch the flat ground would look wrong.
                float near = reach + 10f;
                if (Math.Abs(s.Position.Y) > MaxRoadHeight && dx * dx + dz * dz < near * near)
                {
                    return;
                }
            }

            foreach (var (centre, r) in footprints)
            {
                float dx = centre.X - ground.X;
                float dz = centre.Z - ground.Z;
                if (dx * dx + dz * dz < (r + radius) * (r + radius))
                {
                    return;
                }
            }

            footprints.Add((ground, radius));
            placements.Add(new PropPlacement { Model = model, Position = ground, Yaw = yaw, Scale = scale });
        }
    }

    /// <summary>
    /// Bakes placed pieces into one mesh per colour (design doc §13), so a whole track's scenery is
    /// a handful of draw calls. Each model is moved so its footprint's middle, at its base, sits at
    /// the placement, then scaled, turned and put in place.
    /// </summary>
    public static class SceneryMesh
    {
        public static List<(Vec3 Colour, MeshData Mesh)> Build(IEnumerable<PropPlacement> placements, IReadOnlyDictionary<string, ObjModel> models, IReadOnlyDictionary<string, Vec3> colours)
        {
            var byColour = new Dictionary<Vec3, MeshData>();
            var order = new List<Vec3>();
            foreach (var placement in placements)
            {
                if (!models.TryGetValue(placement.Model, out var model))
                {
                    continue;
                }

                var pivot = new Vec3((model.Min.X + model.Max.X) / 2f, model.Min.Y, (model.Min.Z + model.Max.Z) / 2f);
                float sin = MathF.Sin(placement.Yaw);
                float cos = MathF.Cos(placement.Yaw);
                foreach (var part in model.Parts)
                {
                    var colour = colours.TryGetValue(part.Material, out var c) ? c : new Vec3(1f, 1f, 1f);
                    if (!byColour.TryGetValue(colour, out var mesh))
                    {
                        mesh = new MeshData();
                        byColour[colour] = mesh;
                        order.Add(colour);
                    }

                    int start = mesh.Vertices.Count;
                    for (int i = 0; i < part.Positions.Count; i++)
                    {
                        var local = (part.Positions[i] - pivot) * placement.Scale;
                        mesh.Vertices.Add(placement.Position + Turn(local, sin, cos));
                        mesh.Normals.Add(Turn(part.Normals[i], sin, cos));
                        mesh.Uvs.Add(part.Uvs[i]);
                    }

                    foreach (int index in part.Triangles)
                    {
                        mesh.Triangles.Add(start + index);
                    }
                }
            }

            var result = new List<(Vec3, MeshData)>();
            foreach (var colour in order)
            {
                result.Add((colour, byColour[colour]));
            }

            return result;
        }

        /// <summary>Turns about the vertical so +z goes to (sin, 0, cos) and +x to (cos, 0, -sin).</summary>
        private static Vec3 Turn(Vec3 v, float sin, float cos) => new Vec3(v.X * cos + v.Z * sin, v.Y, -v.X * sin + v.Z * cos);
    }
}
