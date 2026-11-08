using System;
using System.Collections.Generic;
using CarRacer.Core.Numerics;

namespace CarRacer.Core.Track
{
    /// <summary>
    /// The built-in tracks as they sit in the game's one scene (design doc §13): each is moved to
    /// its own patch of ground, clear of the others and of the arenas, so the scene builds them all
    /// once and an event only has to say which one it uses (by index, in the countdown).
    /// </summary>
    public static class TrackCatalog
    {
        /// <summary>Each track's grass reaches this far beyond its road.</summary>
        public const float GroundMargin = 60f;

        /// <summary>Where each of <see cref="SampleTracks.All"/> is moved to, in the same order.</summary>
        private static readonly Vec3[] Offsets =
        {
            Vec3.Zero,
            new Vec3(-450f, 0f, 0f),
            new Vec3(550f, 0f, 30f),

            // North of the Battle bowl, side by side.
            new Vec3(-20f, 0f, 520f),
            new Vec3(-450f, 0f, 520f),

            // South of the Sumo platform, and east of the bowl.
            new Vec3(-450f, 0f, -520f),
            new Vec3(-30f, 0f, -560f),
            new Vec3(550f, 0f, 520f),
        };

        /// <summary>
        /// Built on first use, and read from more than one thread (the TV viewer renders on its own), so
        /// <see cref="Lazy{T}"/> publishes it only once it is complete.
        /// </summary>
        private static readonly Lazy<IReadOnlyList<TrackLayout>> BuiltLayouts =
            new Lazy<IReadOnlyList<TrackLayout>>(BuildLayouts);

        public static int Count => Offsets.Length;

        /// <summary>The tracks, placed in the scene, by index.</summary>
        public static IReadOnlyList<TrackLayout> Layouts => BuiltLayouts.Value;

        private static IReadOnlyList<TrackLayout> BuildLayouts()
        {
            var definitions = SampleTracks.All();
            var layouts = new List<TrackLayout>(definitions.Length);
            for (int i = 0; i < definitions.Length; i++)
            {
                layouts.Add(new TrackLayout(Moved(definitions[i], Offsets[i])));
            }

            return layouts;
        }

        /// <summary>
        /// The patch of grass the scene lays under <paramref name="layout"/>: the extent of its road's
        /// centre line, <see cref="GroundMargin"/> wider all round. It stays clear of every other
        /// track's ground and of the arenas'.
        /// </summary>
        public static (float MinX, float MaxX, float MinZ, float MaxZ) Ground(TrackLayout layout)
        {
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var sample in layout.Spline.SampleEvenly(4f))
            {
                minX = Math.Min(minX, sample.Position.X);
                maxX = Math.Max(maxX, sample.Position.X);
                minZ = Math.Min(minZ, sample.Position.Z);
                maxZ = Math.Max(maxZ, sample.Position.Z);
            }

            return (minX - GroundMargin, maxX + GroundMargin, minZ - GroundMargin, maxZ + GroundMargin);
        }

        /// <summary>A valid track index: <paramref name="index"/>, or 0 if it is out of range (e.g. from a newer host).</summary>
        public static int Clamp(int index) => index >= 0 && index < Count ? index : 0;

        /// <summary>A copy of <paramref name="definition"/> with every control point moved by <paramref name="offset"/>.</summary>
        public static TrackDefinition Moved(TrackDefinition definition, Vec3 offset)
        {
            var points = new TrackPoint[definition.Points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                var p = definition.Points[i];
                points[i] = new TrackPoint { Position = p.Position + offset, Width = p.Width, BankDegrees = p.BankDegrees };
            }

            return new TrackDefinition
            {
                Id = definition.Id,
                DisplayName = definition.DisplayName,
                Points = points,
                CheckpointCount = definition.CheckpointCount,
                WallHeight = definition.WallHeight,
                ItemBoxRows = definition.ItemBoxRows,
            };
        }
    }
}
