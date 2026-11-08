using System;
using System.Collections.Generic;
using CarRacer.Core.Arena;
using CarRacer.Core.Driving;
using CarRacer.Core.Numerics;

namespace CarRacer.Core.Bots
{
    /// <summary>
    /// A bot's driving in Sumo and Battle (design doc §9: "chase opponents and avoid edges"). It
    /// hunts the nearest rival; in Sumo it aims just past the rival's outer side, so a hit pushes
    /// them towards the edge. Whatever it is doing, it turns back towards the middle once it gets within
    /// a speed-dependent distance of the edge, and lifts off if it is heading out fast. With no one
    /// to chase it circles the middle. Difficulty scales its speed as on the track.
    /// </summary>
    public sealed class ArenaBotDriver
    {
        /// <summary>Always keep at least this far from the edge, plus <see cref="EdgeSecondsAhead"/> of travel.</summary>
        public const float EdgeMargin = 3f;

        /// <summary>How many seconds of travel ahead the bot looks for the edge.</summary>
        public const float EdgeSecondsAhead = 0.5f;

        /// <summary>In Sumo, aim this far past the rival on its outer side (away from the centre), so the push carries them outwards.</summary>
        public const float PushOffset = 1.5f;

        /// <summary>Top speed (m/s) allowed per metre of arena radius.</summary>
        public const float SpeedPerMetreOfRadius = 0.6f;

        private readonly BotSkill _skill;
        private readonly CarTuning _tuning;
        private readonly GameMode _mode;
        private readonly float _orbitDirection;

        public ArenaBotDriver(BotSkill skill, CarTuning tuning, GameMode mode, int seed)
        {
            _skill = skill;
            _tuning = tuning;
            _mode = mode;
            _orbitDirection = seed % 2 == 0 ? 1f : -1f;
        }

        /// <param name="radius">The arena's current radius (Sumo's shrinks).</param>
        /// <param name="rivals">Other cars still in the event.</param>
        public CarInput Drive(CarState state, Vec3 position, Vec3 centre, float radius, IReadOnlyList<Vec3> rivals)
        {
            float speed = Vec3.Dot(state.Velocity, state.Forward);
            var fromCentre = (position - centre).Flattened;
            float distance = fromCentre.Length;
            float safe = Math.Max(1f, radius - EdgeMargin - Math.Max(0f, speed) * EdgeSecondsAhead);

            Vec3 target;
            if (distance > safe)
            {
                // Too close to the edge: head for the middle.
                target = centre;
            }
            else if (TryNearest(position, rivals, out var rival))
            {
                target = rival;
                if (_mode == GameMode.Sumo)
                {
                    var rivalOut = (rival - centre).Flattened;
                    if (rivalOut.LengthSquared > 1e-4f)
                    {
                        target = rival + rivalOut.Normalized * PushOffset;
                    }
                }
            }
            else
            {
                // Nobody to chase: circle the middle at half the safe distance.
                var around = fromCentre.LengthSquared > 1e-4f ? fromCentre.Normalized : Vec3.Forward;
                var tangent = new Vec3(around.Z, 0f, -around.X) * _orbitDirection;
                target = centre + around * (safe * 0.5f) + tangent * 6f;
            }

            float turn = Angles.WrapSigned(Angles.Heading(target - position) - state.Heading);
            float steer = Math.Max(-1f, Math.Min(1f, turn / BotDriver.FullLockAngle));

            // Heading outwards near the edge at speed: lift off and let the turn bring it round.
            bool headingOut = distance > 1e-3f && Vec3.Dot(state.Forward, fromCentre / distance) > 0.3f;
            // Keep the turning circle (about speed / 2 rad/s) well inside the arena, so a small platform is driven slowly.
            float topSpeed = Math.Min(
                _tuning.MaxSpeed * _skill.TopSpeedFactor * (_mode == GameMode.Sumo ? 0.8f : 1f),
                Math.Max(_tuning.FullSteerSpeed, radius * SpeedPerMetreOfRadius));
            float throttle = distance > safe && headingOut ? -1f : speed < topSpeed ? 1f : 0f;
            return new CarInput(steer, throttle);
        }

        private static bool TryNearest(Vec3 position, IReadOnlyList<Vec3> rivals, out Vec3 nearest)
        {
            nearest = default;
            float best = float.MaxValue;
            if (rivals == null)
            {
                return false;
            }

            foreach (var rival in rivals)
            {
                float d = (rival - position).Flattened.LengthSquared;
                if (d < best)
                {
                    best = d;
                    nearest = rival;
                }
            }

            return best < float.MaxValue;
        }
    }
}
