using System;
using System.Collections.Generic;
using CarRacer.Core.Numerics;

namespace CarRacer.Core.Track
{
    /// <summary>A point on the track centre line with its local frame.</summary>
    public struct TrackSample
    {
        /// <summary>Distance along the centre line from the start/finish line, in metres.</summary>
        public float Distance;

        public Vec3 Position;

        /// <summary>Unit racing direction.</summary>
        public Vec3 Forward;

        /// <summary>Unit vector across the road towards the right edge (includes banking).</summary>
        public Vec3 Right;

        /// <summary>Unit road-surface normal (includes banking).</summary>
        public Vec3 Up;

        public float Width;
    }

    /// <summary>
    /// Closed Catmull-Rom spline through a track's control points, re-parameterised by arc
    /// length so callers work in metres along the track.
    /// </summary>
    public sealed class TrackSpline
    {
        private const int SamplesPerSegment = 32;

        private readonly TrackPoint[] _points;
        private readonly float[] _tableT;
        private readonly float[] _tableDistance;
        private readonly Vec3[] _tablePosition;

        public TrackSpline(TrackDefinition definition)
        {
            var problems = definition.Validate();
            if (problems.Count > 0)
            {
                throw new ArgumentException($"Invalid track '{definition.Id}': {string.Join(" ", problems)}");
            }

            _points = definition.Points;
            int count = _points.Length * SamplesPerSegment;
            _tableT = new float[count + 1];
            _tableDistance = new float[count + 1];
            _tablePosition = new Vec3[count + 1];

            Vec3 previous = EvaluatePosition(0f);
            _tablePosition[0] = previous;
            float total = 0f;
            for (int i = 1; i <= count; i++)
            {
                float t = (float)i / SamplesPerSegment;
                Vec3 p = EvaluatePosition(t);
                total += Vec3.Distance(previous, p);
                _tableT[i] = t;
                _tableDistance[i] = total;
                _tablePosition[i] = p;
                previous = p;
            }

            Length = total;
        }

        /// <summary>Lap length in metres along the centre line.</summary>
        public float Length { get; }

        public float WrapDistance(float distance)
        {
            float d = distance % Length;
            return d < 0f ? d + Length : d;
        }

        public TrackSample SampleAtDistance(float distance)
        {
            float d = WrapDistance(distance);
            float t = DistanceToT(d);
            Vec3 position = EvaluatePosition(t);
            Vec3 forward = EvaluateTangent(t).Normalized;
            float width = InterpolateScalar(t, p => p.Width);
            float bankRadians = InterpolateScalar(t, p => p.BankDegrees) * (MathF.PI / 180f);

            Vec3 flatRight = Vec3.Cross(Vec3.Up, forward).Normalized;
            Vec3 right = (flatRight * MathF.Cos(bankRadians) - Vec3.Up * MathF.Sin(bankRadians)).Normalized;
            Vec3 up = Vec3.Cross(forward, right).Normalized;

            return new TrackSample
            {
                Distance = d,
                Position = position,
                Forward = forward,
                Right = right,
                Up = up,
                Width = width,
            };
        }

        /// <summary>
        /// Distance along the track of the centre-line point nearest to <paramref name="position"/>,
        /// measured in 3D so a car under a bridge matches the road it is on, not the one overhead.
        /// Pass the car's previous distance as <paramref name="hintDistance"/> to search only
        /// nearby, which is faster.
        /// </summary>
        public float FindNearestDistance(Vec3 position, float? hintDistance = null, float searchWindow = 40f)
        {
            int count = _tableDistance.Length - 1;
            int best = 0;
            float bestSq = float.MaxValue;

            if (hintDistance.HasValue && searchWindow * 2f < Length)
            {
                int center = IndexAtDistance(WrapDistance(hintDistance.Value));
                int span = (int)MathF.Ceiling(searchWindow / Length * count) + 1;
                for (int k = -span; k <= span; k++)
                {
                    int i = ((center + k) % count + count) % count;
                    Consider(i);
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    Consider(i);
                }
            }

            // Refine on the segments either side of the best table entry.
            float bestDistance = _tableDistance[best];
            int prev = (best - 1 + count) % count;
            int next = (best + 1) % count;
            foreach (var (a, b) in new[] { (prev, best), (best, next) })
            {
                Vec3 pa = _tablePosition[a];
                Vec3 pb = _tablePosition[b];
                Vec3 seg = pb - pa;
                float segLenSq = seg.LengthSquared;
                if (segLenSq < 1e-8f)
                {
                    continue;
                }

                float u = Math.Clamp(Vec3.Dot(position - pa, seg) / segLenSq, 0f, 1f);
                Vec3 p = pa + seg * u;
                float sq = (position - p).LengthSquared;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    float da = _tableDistance[a];
                    float db = b == 0 ? Length : _tableDistance[b];
                    bestDistance = da + (db - da) * u;
                }
            }

            return WrapDistance(bestDistance);

            void Consider(int i)
            {
                float sq = (position - _tablePosition[i]).LengthSquared;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = i;
                }
            }
        }

        /// <summary>Evenly spaced samples around the lap (the last one stops short of wrapping to the first).</summary>
        public List<TrackSample> SampleEvenly(float spacing)
        {
            int count = Math.Max(8, (int)MathF.Ceiling(Length / spacing));
            var samples = new List<TrackSample>(count);
            for (int i = 0; i < count; i++)
            {
                samples.Add(SampleAtDistance(Length * i / count));
            }

            return samples;
        }

        private int IndexAtDistance(float d)
        {
            int index = Array.BinarySearch(_tableDistance, d);
            if (index < 0)
            {
                index = ~index - 1;
            }

            return Math.Max(0, Math.Min(index, _tableDistance.Length - 2));
        }

        private float DistanceToT(float d)
        {
            int i = IndexAtDistance(d);
            float d0 = _tableDistance[i];
            float d1 = _tableDistance[i + 1];
            float u = d1 > d0 ? (d - d0) / (d1 - d0) : 0f;
            return _tableT[i] + (_tableT[i + 1] - _tableT[i]) * u;
        }

        private Vec3 EvaluatePosition(float t)
        {
            GetSegment(t, out var p0, out var p1, out var p2, out var p3, out float u);
            float u2 = u * u;
            float u3 = u2 * u;
            return 0.5f * (
                2f * p1.Position
                + (p2.Position - p0.Position) * u
                + (2f * p0.Position - 5f * p1.Position + 4f * p2.Position - p3.Position) * u2
                + (3f * p1.Position - p0.Position - 3f * p2.Position + p3.Position) * u3);
        }

        private Vec3 EvaluateTangent(float t)
        {
            GetSegment(t, out var p0, out var p1, out var p2, out var p3, out float u);
            float u2 = u * u;
            return 0.5f * (
                (p2.Position - p0.Position)
                + 2f * (2f * p0.Position - 5f * p1.Position + 4f * p2.Position - p3.Position) * u
                + 3f * (3f * p1.Position - p0.Position - 3f * p2.Position + p3.Position) * u2);
        }

        /// <summary>Smoothstep between the two control points of the current segment (no Catmull-Rom overshoot).</summary>
        private float InterpolateScalar(float t, Func<TrackPoint, float> selector)
        {
            GetSegment(t, out _, out var p1, out var p2, out _, out float u);
            float s = u * u * (3f - 2f * u);
            return selector(p1) + (selector(p2) - selector(p1)) * s;
        }

        private void GetSegment(float t, out TrackPoint p0, out TrackPoint p1, out TrackPoint p2, out TrackPoint p3, out float u)
        {
            int n = _points.Length;
            float wrapped = t % n;
            if (wrapped < 0f)
            {
                wrapped += n;
            }

            int i = (int)MathF.Floor(wrapped);
            if (i >= n)
            {
                i = n - 1;
            }

            u = wrapped - i;
            p0 = _points[(i - 1 + n) % n];
            p1 = _points[i];
            p2 = _points[(i + 1) % n];
            p3 = _points[(i + 2) % n];
        }
    }
}
