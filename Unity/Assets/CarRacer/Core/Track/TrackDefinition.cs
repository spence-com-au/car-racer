using System;
using System.Collections.Generic;
using CarRacer.Core.Numerics;

namespace CarRacer.Core.Track
{
    /// <summary>One control point of a track's centre-line spline.</summary>
    [Serializable]
    public sealed class TrackPoint
    {
        public Vec3 Position;

        /// <summary>Drivable road width in metres at this point.</summary>
        public float Width = 14f;

        /// <summary>Road roll in degrees. Positive raises the left edge (banks into a right-hand turn).</summary>
        public float BankDegrees;

        public TrackPoint()
        {
        }

        public TrackPoint(float x, float y, float z, float width = 14f, float bankDegrees = 0f)
        {
            Position = new Vec3(x, y, z);
            Width = width;
            BankDegrees = bankDegrees;
        }
    }

    /// <summary>
    /// Data-only description of a closed race track. Everything else (road and wall meshes,
    /// checkpoint gates, start grid, bot racing line) is generated from this. Public fields
    /// so Unity's JsonUtility can load it from a TextAsset.
    /// </summary>
    [Serializable]
    public sealed class TrackDefinition
    {
        public string Id = string.Empty;
        public string DisplayName = string.Empty;

        /// <summary>Centre-line control points in racing order. The loop closes from the last point back to the first.</summary>
        public TrackPoint[] Points = Array.Empty<TrackPoint>();

        /// <summary>Evenly spaced checkpoints; index 0 is the start/finish line at the first control point.</summary>
        public int CheckpointCount = 8;

        public float WallHeight = 1.2f;

        /// <summary>Item box rows, as fractions (0..1) of the lap length.</summary>
        public float[] ItemBoxRows = Array.Empty<float>();

        /// <summary>Returns a list of problems; empty means the track is usable.</summary>
        public IReadOnlyList<string> Validate()
        {
            var problems = new List<string>();
            if (string.IsNullOrWhiteSpace(Id))
            {
                problems.Add("Id is required.");
            }

            if (Points == null || Points.Length < 4)
            {
                problems.Add("A track needs at least 4 control points.");
            }
            else
            {
                for (int i = 0; i < Points.Length; i++)
                {
                    if (Points[i] == null)
                    {
                        problems.Add($"Point {i} is missing.");
                        continue;
                    }

                    if (Points[i].Width < 4f)
                    {
                        problems.Add($"Point {i} width {Points[i].Width} is narrower than 4 m.");
                    }

                    if (Math.Abs(Points[i].BankDegrees) > 45f)
                    {
                        problems.Add($"Point {i} bank {Points[i].BankDegrees}° exceeds 45°.");
                    }

                    var next = Points[(i + 1) % Points.Length];
                    if (next != null && Vec3.Distance(Points[i].Position, next.Position) < 1f)
                    {
                        problems.Add($"Points {i} and {(i + 1) % Points.Length} are closer than 1 m.");
                    }
                }
            }

            if (CheckpointCount < 3)
            {
                problems.Add("At least 3 checkpoints are required so laps can't be short-cut.");
            }

            if (ItemBoxRows != null)
            {
                foreach (float f in ItemBoxRows)
                {
                    if (f < 0f || f >= 1f)
                    {
                        problems.Add($"Item box row {f} must be in [0, 1).");
                    }
                }
            }

            return problems;
        }
    }
}
