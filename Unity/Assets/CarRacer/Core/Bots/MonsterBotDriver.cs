using System;
using System.Collections.Generic;
using CarRacer.Core.Arena;
using CarRacer.Core.Driving;
using CarRacer.Core.Numerics;

namespace CarRacer.Core.Bots
{
    /// <summary>
    /// A bot's driving in Monster Jam (design doc §7.0, §9): it works round the stadium's ramps in
    /// turn, lining up behind each and launching off it flat out for air time, and rams any rival
    /// truck right in front of it for a crush. Near the wall it turns back towards the middle.
    /// Difficulty scales its speed as on the track. Each bot starts at a different ramp.
    /// </summary>
    public sealed class MonsterBotDriver
    {
        /// <summary>The bot lines up this far behind a ramp's foot before going for it.</summary>
        public const float RunUp = 10f;

        /// <summary>Close enough to the run-up point to turn for the ramp.</summary>
        public const float RunUpReach = 5f;

        /// <summary>A rival this close and roughly ahead gets rammed.</summary>
        public const float RamRange = 12f;

        /// <summary>Turn back towards the middle once this close to the wall.</summary>
        public const float WallMargin = 6f;

        private readonly BotSkill _skill;
        private readonly CarTuning _tuning;
        private readonly IReadOnlyList<Ramp> _ramps;
        private readonly Vec3 _centre;
        private readonly float _radius;
        private int _ramp;
        private bool _launching;

        public MonsterBotDriver(BotSkill skill, CarTuning tuning, IReadOnlyList<Ramp> ramps, Vec3 centre, float radius, int seed)
        {
            _skill = skill;
            _tuning = tuning;
            _ramps = ramps;
            _centre = centre;
            _radius = radius;
            _ramp = ramps.Count > 0 ? ((seed % ramps.Count) + ramps.Count) % ramps.Count : 0;
        }

        /// <summary>The ramp the bot is going for, by index into the stadium's ramps.</summary>
        public int CurrentRamp => _ramp;

        /// <summary>True once the bot is lined up and heading up the current ramp.</summary>
        public bool Launching => _launching;

        public CarInput Drive(CarState state, Vec3 position, IReadOnlyList<Vec3> rivals)
        {
            Vec3 target = Target(state, position, rivals);
            float turn = Angles.WrapSigned(Angles.Heading(target - position) - state.Heading);
            float steer = Math.Max(-1f, Math.Min(1f, turn / BotDriver.FullLockAngle));

            // Flat out up a ramp and at a rival; otherwise ease off for a tight turn.
            float speed = Vec3.Dot(state.Velocity, state.Forward);
            float topSpeed = _tuning.MaxSpeed * _skill.TopSpeedFactor;
            if (!_launching && Math.Abs(turn) > 1f)
            {
                topSpeed = Math.Max(_tuning.FullSteerSpeed, topSpeed * 0.5f);
            }

            return new CarInput(steer, speed < topSpeed ? 1f : 0f);
        }

        private Vec3 Target(CarState state, Vec3 position, IReadOnlyList<Vec3> rivals)
        {
            var fromCentre = (position - _centre).Flattened;
            if (fromCentre.Length > _radius - WallMargin)
            {
                _launching = false;
                return _centre;
            }

            if (TryRivalAhead(state, position, rivals, out var rival))
            {
                return rival;
            }

            if (_ramps.Count == 0)
            {
                return _centre;
            }

            var ramp = _ramps[_ramp];
            if (_launching)
            {
                // Past the lip: on to the next ramp.
                if (Vec3.Dot((position - ramp.Lip).Flattened, ramp.Forward) > 2f)
                {
                    _launching = false;
                    _ramp = (_ramp + 1) % _ramps.Count;
                    return RunUpPoint(_ramps[_ramp]);
                }

                return ramp.Lip + ramp.Forward * 10f;
            }

            var runUp = RunUpPoint(ramp);
            if ((position - runUp).Flattened.Length < RunUpReach)
            {
                _launching = true;
                return ramp.Lip + ramp.Forward * 10f;
            }

            return runUp;
        }

        private static Vec3 RunUpPoint(Ramp ramp) => ramp.Foot - ramp.Forward * RunUp;

        private static bool TryRivalAhead(CarState state, Vec3 position, IReadOnlyList<Vec3> rivals, out Vec3 nearest)
        {
            nearest = default;
            float best = RamRange;
            if (rivals == null)
            {
                return false;
            }

            foreach (var rival in rivals)
            {
                var to = (rival - position).Flattened;
                float distance = to.Length;
                if (distance < best && distance > 1e-3f && Vec3.Dot(to / distance, state.Forward.Flattened.Normalized) > 0.6f)
                {
                    best = distance;
                    nearest = rival;
                }
            }

            return best < RamRange;
        }
    }
}
