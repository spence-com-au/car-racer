using System;
using System.Collections.Generic;
using CarRacer.Core.Numerics;

namespace CarRacer.Core.Track
{
    /// <summary>A checkpoint trigger: a vertical plane across the road at <see cref="Sample"/>.</summary>
    public struct CheckpointGate
    {
        public int Index;
        public TrackSample Sample;
    }

    public struct GridSlot
    {
        public int Slot;
        public Vec3 Position;
        public Vec3 Forward;
    }

    /// <summary>Gameplay placements derived from a track's spline: checkpoints, start grid, item boxes.</summary>
    public sealed class TrackLayout
    {
        public const float GridFirstRowOffset = 6f;
        public const float GridRowSpacing = 7f;

        private readonly float[] _checkpointDistances;

        public TrackLayout(TrackDefinition definition)
        {
            Definition = definition;
            Spline = new TrackSpline(definition);
            _checkpointDistances = new float[definition.CheckpointCount];
            for (int i = 0; i < definition.CheckpointCount; i++)
            {
                _checkpointDistances[i] = Spline.Length * i / definition.CheckpointCount;
            }
        }

        public TrackDefinition Definition { get; }
        public TrackSpline Spline { get; }
        public int CheckpointCount => _checkpointDistances.Length;

        public float CheckpointDistance(int index) => _checkpointDistances[index];

        public IReadOnlyList<CheckpointGate> CheckpointGates()
        {
            var gates = new List<CheckpointGate>(CheckpointCount);
            for (int i = 0; i < CheckpointCount; i++)
            {
                gates.Add(new CheckpointGate { Index = i, Sample = Spline.SampleAtDistance(_checkpointDistances[i]) });
            }

            return gates;
        }

        /// <summary>
        /// How far (0..1) a car at <paramref name="trackDistance"/> is between its previous
        /// checkpoint and <paramref name="nextCheckpoint"/>. Feeds RaceTracker standings.
        /// </summary>
        public float FractionToNextCheckpoint(float trackDistance, int nextCheckpoint)
        {
            int previous = (nextCheckpoint - 1 + CheckpointCount) % CheckpointCount;
            float segment = Spline.Length / CheckpointCount;
            float travelled = Spline.WrapDistance(trackDistance - _checkpointDistances[previous]);

            // Outside the segment the car is either past the next gate or pushed back behind the
            // previous one; trust whichever is nearer, so a car just behind its last gate counts as 0.
            if (travelled > segment)
            {
                return travelled - segment < Spline.Length - travelled ? 1f : 0f;
            }

            return travelled / segment;
        }

        /// <summary>
        /// Two staggered columns behind the start line. Slot 0 is pole (front, left).
        /// Championship reverse-grid ordering is applied by the caller choosing who gets which slot.
        /// </summary>
        public IReadOnlyList<GridSlot> StartGrid(int slotCount)
        {
            var slots = new List<GridSlot>(slotCount);
            for (int slot = 0; slot < slotCount; slot++)
            {
                int row = slot / 2;
                int column = slot % 2;
                float back = GridFirstRowOffset + row * GridRowSpacing + column * (GridRowSpacing / 2f);
                var sample = Spline.SampleAtDistance(-back);
                float lateral = (column == 0 ? -0.25f : 0.25f) * sample.Width;
                slots.Add(new GridSlot
                {
                    Slot = slot,
                    Position = sample.Position + sample.Right * lateral,
                    Forward = sample.Forward,
                });
            }

            return slots;
        }

        /// <summary>Positions of item boxes: each row places <paramref name="perRow"/> boxes evenly across the road.</summary>
        public IReadOnlyList<Vec3> ItemBoxPositions(int perRow = 4)
        {
            var positions = new List<Vec3>();
            foreach (float fraction in Definition.ItemBoxRows ?? Array.Empty<float>())
            {
                var sample = Spline.SampleAtDistance(fraction * Spline.Length);
                for (int k = 0; k < perRow; k++)
                {
                    float across = ((k + 0.5f) / perRow - 0.5f) * sample.Width * 0.8f;
                    positions.Add(sample.Position + sample.Right * across + sample.Up * 0.75f);
                }
            }

            return positions;
        }
    }
}
