using System;
using System.Collections.Generic;
using CarRacer.Core.Driving;
using CarRacer.Core.Numerics;
using CarRacer.Core.Track;

namespace CarRacer.Core.Bots
{
    /// <summary>The host's choice of bot difficulty in the lobby (design doc §9). The values travel in network messages.</summary>
    public enum BotDifficulty : byte
    {
        Easy = 0,
        Normal = 1,
        Hard = 2,
    }

    /// <summary>What difficulty changes about a bot (design doc §9): top speed, how hard it corners, how far it wanders off its line, and how quickly it uses items.</summary>
    public sealed class BotSkill
    {
        /// <summary>The share of the car's top speed the bot drives at on straights.</summary>
        public float TopSpeedFactor;

        /// <summary>The sideways acceleration (m/s²) the bot is willing to corner at; it slows down to keep under it.</summary>
        public float CornerAcceleration;

        /// <summary>How far (m) the bot drifts either side of the centre line as it drives.</summary>
        public float Wander;

        /// <summary>How long the bot holds an item before it will use it.</summary>
        public float ItemDelaySeconds;

        public static BotSkill For(BotDifficulty difficulty)
        {
            switch (difficulty)
            {
                case BotDifficulty.Easy:
                    return new BotSkill { TopSpeedFactor = 0.82f, CornerAcceleration = 10f, Wander = 2f, ItemDelaySeconds = 2.5f };
                case BotDifficulty.Hard:
                    return new BotSkill { TopSpeedFactor = 1f, CornerAcceleration = 16f, Wander = 0.5f, ItemDelaySeconds = 0.4f };
                default:
                    return new BotSkill { TopSpeedFactor = 0.92f, CornerAcceleration = 13f, Wander = 1.2f, ItemDelaySeconds = 1.2f };
            }
        }
    }

    /// <summary>
    /// A bot's driving (design doc §9): it chases a point on the track a speed-dependent distance
    /// ahead, wandering a little either side of the centre line; slows down for the bends coming
    /// up so it corners within its skill; and swerves into a rival close in front of it to ram.
    /// It produces the same <see cref="CarInput"/> a player's controls do, so a bot's car is driven
    /// by the same model and physics as everyone else's.
    /// </summary>
    public sealed class BotDriver
    {
        /// <summary>A rival this close, and roughly in front, gets rammed.</summary>
        public const float RamRange = 10f;

        /// <summary>How far either side of straight ahead (radians) a rival can be and still be rammed.</summary>
        public const float RamCone = 0.6f;

        /// <summary>The steering angle (radians) to the target that means full lock.</summary>
        public const float FullLockAngle = 0.5f;

        /// <summary>How many seconds of driving ahead the bot looks to judge the next bend.</summary>
        public const float BrakeLookSeconds = 1.2f;

        /// <summary>Stay at least this far from the edge of the road when wandering.</summary>
        public const float EdgeMargin = 2f;

        private readonly TrackSpline _spline;
        private readonly BotSkill _skill;
        private readonly CarTuning _tuning;
        private readonly float _wanderPhase;
        private float _trackDistance;
        private bool _hasTrackDistance;
        private float _clock;

        /// <param name="seed">Varies the bot's wander, so bots don't all drive the same line.</param>
        public BotDriver(TrackSpline spline, BotSkill skill, CarTuning tuning, int seed)
        {
            _spline = spline;
            _skill = skill;
            _tuning = tuning;
            _wanderPhase = (seed * 2.399963f) % (2f * MathF.PI);
        }

        /// <summary>How far round the lap the bot last was, in metres.</summary>
        public float TrackDistance => _trackDistance;

        /// <summary>How sharp the track is just ahead of the bot (radians per metre); low means a straight.</summary>
        public float CurvatureAhead { get; private set; }

        /// <summary>Forgets where the bot was on the track, after it is put back on the grid or respawns.</summary>
        public void Reset()
        {
            _hasTrackDistance = false;
        }

        /// <summary>
        /// The controls for one physics step. <paramref name="rivals"/> are the other cars'
        /// positions, for ramming.
        /// </summary>
        public CarInput Drive(CarState state, Vec3 position, float deltaSeconds, IReadOnlyList<Vec3> rivals)
        {
            _clock += deltaSeconds;
            _trackDistance = _hasTrackDistance ? _spline.FindNearestDistance(position, _trackDistance) : _spline.FindNearestDistance(position);
            _hasTrackDistance = true;

            float speed = Vec3.Dot(state.Velocity, state.Forward);
            float lookahead = 6f + Math.Max(0f, speed) * 0.45f;
            var ahead = _spline.SampleAtDistance(_trackDistance + lookahead);
            float maxWander = Math.Max(0f, ahead.Width / 2f - EdgeMargin);
            float wander = Math.Min(_skill.Wander, maxWander) * MathF.Sin(_wanderPhase + _clock * 0.35f);
            var target = ahead.Position + ahead.Right * wander;

            if (TryFindRamTarget(state, position, rivals, out var rival))
            {
                target = rival;
            }

            float turn = Angles.WrapSigned(Angles.Heading(target - position) - state.Heading);
            float steer = Math.Max(-1f, Math.Min(1f, turn / FullLockAngle));

            float targetSpeed = TargetSpeed(state, speed);
            float throttle = speed < targetSpeed ? 1f : speed > targetSpeed + 2f ? -1f : 0f;
            return new CarInput(steer, throttle);
        }

        /// <summary>The speed the bot wants now: its share of top speed, or slower for the bend coming up.</summary>
        private float TargetSpeed(CarState state, float speed)
        {
            var here = _spline.SampleAtDistance(_trackDistance);
            float span = 10f + Math.Max(0f, speed) * BrakeLookSeconds;
            var there = _spline.SampleAtDistance(_trackDistance + span);
            CurvatureAhead = Math.Abs(Angles.WrapSigned(Angles.Heading(there.Forward) - Angles.Heading(here.Forward))) / span;
            float straight = _tuning.MaxSpeed * _skill.TopSpeedFactor;
            if (CurvatureAhead < 1e-4f)
            {
                return straight;
            }

            // Cornering at speed v round curvature k needs v²k of sideways acceleration.
            float corner = MathF.Sqrt(_skill.CornerAcceleration / CurvatureAhead);
            return Math.Min(straight, corner);
        }

        private static bool TryFindRamTarget(CarState state, Vec3 position, IReadOnlyList<Vec3> rivals, out Vec3 target)
        {
            target = default;
            float best = RamRange * RamRange;
            bool found = false;
            if (rivals == null)
            {
                return false;
            }

            foreach (var rival in rivals)
            {
                var offset = (rival - position).Flattened;
                float distance = offset.LengthSquared;
                if (distance < best && distance > 1e-4f && Math.Abs(Angles.WrapSigned(Angles.Heading(offset) - state.Heading)) <= RamCone)
                {
                    best = distance;
                    target = rival;
                    found = true;
                }
            }

            return found;
        }
    }
}
