using System;
using CarRacer.Core.Numerics;
using CarRacer.Core.Track;

namespace CarRacer.Core.Race
{
    /// <summary>
    /// Follows one car along the track: where it is (distance along the spline) and whether it is
    /// facing the racing direction. Feeds the "Wrong way!" warning (design doc §6). Each update
    /// searches near the previous distance, so it stays cheap and doesn't jump to a nearby part
    /// of the track the car isn't on; if the car has moved far away (a respawn), it searches the
    /// whole track.
    /// </summary>
    public sealed class TrackProgress
    {
        /// <summary>How far either side of the last known distance to search, in metres.</summary>
        public const float SearchWindow = 30f;

        private readonly TrackSpline _spline;
        private readonly WrongWayDetector _wrongWay = new WrongWayDetector();
        private bool _hasDistance;

        public TrackProgress(TrackSpline spline)
        {
            _spline = spline;
        }

        public float Distance { get; private set; }

        /// <summary>dot(car forward, track direction) on the ground: 1 racing, -1 backwards, 0 across.</summary>
        public float Alignment { get; private set; } = 1f;

        public bool IsWrongWay => _wrongWay.IsWrongWay;

        /// <summary>Call every frame (or physics step) with the car's position, heading (radians, 0 = +Z) and speed.</summary>
        public void Update(Vec3 position, float heading, float speed, float deltaSeconds)
        {
            if (_hasDistance)
            {
                Distance = _spline.FindNearestDistance(position, Distance, SearchWindow);
            }

            var sample = _spline.SampleAtDistance(Distance);

            // A respawn or a reset to the grid can move the car further than the window: search the whole track.
            if (!_hasDistance || (sample.Position - position).Flattened.Length > SearchWindow)
            {
                Distance = _spline.FindNearestDistance(position);
                sample = _spline.SampleAtDistance(Distance);
            }

            _hasDistance = true;
            var tangent = sample.Forward.Flattened.Normalized;
            var facing = Angles.Forward(heading);
            Alignment = Vec3.Dot(facing, tangent);
            _wrongWay.Update(Alignment, Math.Abs(speed), deltaSeconds);
        }

        /// <summary>Forget the car's place, e.g. after a respawn or when lining up on the grid.</summary>
        public void Reset()
        {
            _hasDistance = false;
            _wrongWay.Reset();
            Alignment = 1f;
        }
    }
}
