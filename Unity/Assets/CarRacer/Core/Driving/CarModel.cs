using System;
using CarRacer.Core.Numerics;

namespace CarRacer.Core.Driving
{
    /// <summary>One physics step's worth of driver input.</summary>
    public struct CarInput
    {
        /// <summary>-1 (full left) to 1 (full right).</summary>
        public float Steer;

        /// <summary>-1 (brake, then reverse) to 1 (full throttle).</summary>
        public float Throttle;

        public bool Drift;

        public CarInput(float steer, float throttle, bool drift = false)
        {
            Steer = steer;
            Throttle = throttle;
            Drift = drift;
        }
    }

    /// <summary>Handling numbers. Defaults are a starting point for tuning on real phones.</summary>
    [Serializable]
    public sealed class CarTuning
    {
        /// <summary>The base tuning's top speed (m/s), which the camera zoom, engine note and wall damage scale to.</summary>
        public const float BaseMaxSpeed = 26f;

        public float MaxSpeed = BaseMaxSpeed;
        public float MaxReverseSpeed = 8f;
        public float Acceleration = 16f;
        public float ReverseAcceleration = 9.6f;
        public float Braking = 32f;

        /// <summary>Deceleration with no throttle.</summary>
        public float Coasting = 5f;

        /// <summary>Deceleration while above top speed, e.g. when a boost ends.</summary>
        public float OverSpeedDeceleration = 10f;

        /// <summary>Peak turn rate, reached at <see cref="FullSteerSpeed"/>.</summary>
        public float SteerDegreesPerSecond = 150f;

        /// <summary>Below this speed turning scales down, so the car can't spin on the spot.</summary>
        public float FullSteerSpeed = 6f;

        /// <summary>Fraction of the peak turn rate left at top speed (high-speed stability).</summary>
        public float TopSpeedSteerFactor = 0.6f;

        /// <summary>How quickly sideways sliding is removed (per second). Higher is grippier.</summary>
        public float Grip = 10f;

        public float DriftGrip = 2.5f;

        /// <summary>While drifting the car always turns into the drift; steering varies how tightly, between these fractions of full lock.</summary>
        public float DriftMinTurn = 0.35f;
        public float DriftMaxTurn = 1.25f;

        public float DriftMinSpeed = 9f;

        /// <summary>A drift in progress ends, without a boost, if the car slows below this.</summary>
        public float DriftCancelSpeed = 5.4f;
        public float DriftMinSteer = 0.3f;

        /// <summary>Seconds of drifting needed for each boost tier (ascending).</summary>
        public float[] DriftTierSeconds = { 0.7f, 1.4f, 2.2f };

        /// <summary>Extra top speed for each boost tier, in m/s.</summary>
        public float[] BoostSpeeds = { 5f, 8f, 11f };

        /// <summary>Duration of each boost tier, in seconds.</summary>
        public float[] BoostDurations = { 0.6f, 1.0f, 1.5f };

        public float BoostAcceleration = 30f;

        /// <summary>The Boost item's extra top speed (m/s) and duration (s): a little stronger and longer than a top-tier drift boost.</summary>
        public float ItemBoostSpeed = 12f;
        public float ItemBoostDuration = 1.8f;

        /// <summary>How long an item hit spins the car out, during which the driver has no control.</summary>
        public float SpinOutSeconds = 1f;

        /// <summary>How fast a spun-out car turns, in full turns per second.</summary>
        public float SpinTurnsPerSecond = 1.5f;

        /// <summary>How quickly a spun-out car's slide dies away (per second, exponential).</summary>
        public float SpinDrag = 1.5f;
    }

    /// <summary>The handling state that persists between steps (and is sent over the network as flags).</summary>
    public struct CarState
    {
        /// <summary>Facing direction in radians: 0 faces +Z, π/2 faces +X (clockwise from above, like Unity's yaw).</summary>
        public float Heading;

        /// <summary>Horizontal velocity in m/s (Y is ignored; gravity is the physics engine's job).</summary>
        public Vec3 Velocity;

        public bool Drifting;

        /// <summary>-1 for a left drift, 1 for a right drift, 0 when not drifting.</summary>
        public int DriftDirection;

        public float DriftSeconds;
        public float BoostSecondsLeft;
        public float BoostSpeed;

        /// <summary>Seconds left of an item hit's spin-out; 0 when in control.</summary>
        public float SpinSecondsLeft;

        public bool SpunOut => SpinSecondsLeft > 0f;

        public Vec3 Forward => Angles.Forward(Heading);

        /// <summary>
        /// How far the current drift has charged, for the HUD meter: 0 with no drift, rising to 1
        /// when the top boost tier is reached. Tier <c>n</c> is reached at
        /// <c>DriftTierSeconds[n-1] / DriftTierSeconds[last]</c>.
        /// </summary>
        public float DriftCharge(CarTuning tuning)
        {
            if (!Drifting || tuning.DriftTierSeconds.Length == 0)
            {
                return 0f;
            }

            float full = tuning.DriftTierSeconds[tuning.DriftTierSeconds.Length - 1];
            return full <= 0f ? 1f : Math.Min(1f, DriftSeconds / full);
        }

        /// <summary>Boost tier the current drift would release: 0 for none, then 1..3.</summary>
        public int DriftTier(CarTuning tuning)
        {
            if (!Drifting)
            {
                return 0;
            }

            int tier = 0;
            for (int i = 0; i < tuning.DriftTierSeconds.Length; i++)
            {
                if (DriftSeconds >= tuning.DriftTierSeconds[i])
                {
                    tier = i + 1;
                }
            }

            return tier;
        }
    }

    /// <summary>
    /// Arcade handling for the sphere-based car (design doc §4). Each fixed physics step the Unity
    /// layer feeds in the rigidbody's current horizontal velocity, applies the returned velocity
    /// and heading, and leaves vertical motion to gravity. Velocity is read back every step, so
    /// bumps and collisions change it naturally and grip then pulls the car straight again.
    /// </summary>
    public static class CarModel
    {
        /// <param name="performance">
        /// The share of top speed and acceleration the car has left: 1 when undamaged, less as
        /// damage builds (<see cref="CarDamage.PerformanceFactor"/>). Boosts still add their full speed.
        /// </param>
        public static CarState Step(CarState state, CarInput input, bool grounded, float deltaSeconds, CarTuning tuning, float performance = 1f)
        {
            float steer = Clamp(input.Steer, -1f, 1f);
            float throttle = Clamp(input.Throttle, -1f, 1f);
            Vec3 velocity = state.Velocity.Flattened;

            state.BoostSecondsLeft = Math.Max(0f, state.BoostSecondsLeft - deltaSeconds);
            if (state.BoostSecondsLeft <= 0f)
            {
                state.BoostSpeed = 0f;
            }

            if (state.SpinSecondsLeft > 0f)
            {
                // Spun out: no control, the car turns on the spot of its slide and slows down.
                state.SpinSecondsLeft = Math.Max(0f, state.SpinSecondsLeft - deltaSeconds);
                state.Heading = Angles.WrapPositive(state.Heading + tuning.SpinTurnsPerSecond * 2f * MathF.PI * deltaSeconds);
                state.Velocity = velocity * MathF.Exp(-tuning.SpinDrag * deltaSeconds);
                return state;
            }

            if (!grounded)
            {
                // No traction in the air: keep momentum and heading; a drift in progress is lost.
                EndDrift(ref state, tuning, releaseBoost: false);
                state.Velocity = velocity;
                return state;
            }

            Vec3 forward = state.Forward;
            Vec3 right = new Vec3(forward.Z, 0f, -forward.X);
            float forwardSpeed = Vec3.Dot(velocity, forward);
            float sideSpeed = Vec3.Dot(velocity, right);

            UpdateDrift(ref state, input.Drift, steer, forwardSpeed, deltaSeconds, tuning);

            forwardSpeed = ApplyThrottle(forwardSpeed, throttle, state, deltaSeconds, tuning, performance);

            float turn = state.Drifting
                ? state.DriftDirection * Lerp(tuning.DriftMinTurn, tuning.DriftMaxTurn, (steer * state.DriftDirection + 1f) / 2f)
                : steer;
            float yawRate = turn * tuning.SteerDegreesPerSecond * (MathF.PI / 180f) * SteerFactor(forwardSpeed, tuning);
            state.Heading = Angles.WrapPositive(state.Heading + yawRate * Math.Sign(forwardSpeed) * deltaSeconds);

            float grip = state.Drifting ? tuning.DriftGrip : tuning.Grip;
            sideSpeed *= MathF.Exp(-grip * deltaSeconds);

            // Re-express the kept momentum along the new heading so the car carves rather than skids.
            Vec3 newForward = state.Forward;
            Vec3 newRight = new Vec3(newForward.Z, 0f, -newForward.X);
            state.Velocity = newForward * forwardSpeed + newRight * sideSpeed;
            return state;
        }

        private static float ApplyThrottle(float speed, float throttle, CarState state, float dt, CarTuning t, float performance)
        {
            float topSpeed = t.MaxSpeed * performance + state.BoostSpeed;
            float acceleration = t.Acceleration * performance;
            // A boost drives the car up to the boosted top speed unless the player brakes, which always
            // takes priority. Anything above that top speed eases off below, rather than snapping down,
            // unless the player brakes, which uses the full braking rate.
            if (state.BoostSecondsLeft > 0f && throttle >= 0f && speed <= topSpeed)
            {
                return Math.Min(topSpeed, speed + t.BoostAcceleration * dt);
            }

            if (speed > topSpeed && throttle >= 0f)
            {
                return Math.Max(topSpeed, speed - t.OverSpeedDeceleration * dt);
            }

            // Gas while rolling backwards (or brake while rolling forwards) first brakes to a stop, and
            // may pull away in the same step. Stopping at exactly zero instead would leave a car that is
            // pushed back a little every step, by a slope or a wall, stuck: braked to zero, pushed back,
            // braked to zero again, never moving off.
            if (throttle > 0f)
            {
                if (speed < 0f)
                {
                    float braked = speed + t.Braking * throttle * dt;
                    return braked <= 0f ? braked : Math.Min(braked, acceleration * throttle * dt);
                }

                return Math.Min(topSpeed, speed + acceleration * throttle * dt);
            }

            if (throttle < 0f)
            {
                if (speed > 0f)
                {
                    float braked = speed + t.Braking * throttle * dt;
                    return braked >= 0f ? braked : Math.Max(braked, t.ReverseAcceleration * throttle * dt);
                }

                // A bump can knock a car backwards past its reverse top speed. That eases off like
                // forward over-speed does, so holding brake doesn't snap away the knock-back.
                if (speed < -t.MaxReverseSpeed)
                {
                    return Math.Min(-t.MaxReverseSpeed, speed + t.OverSpeedDeceleration * dt);
                }

                return Math.Max(-t.MaxReverseSpeed, speed + t.ReverseAcceleration * throttle * dt);
            }

            return speed > 0f
                ? Math.Max(0f, speed - t.Coasting * dt)
                : Math.Min(0f, speed + t.Coasting * dt);
        }

        private static void UpdateDrift(ref CarState state, bool driftHeld, float steer, float forwardSpeed, float dt, CarTuning t)
        {
            if (state.Drifting)
            {
                if (!driftHeld)
                {
                    EndDrift(ref state, t, releaseBoost: true);
                }
                else if (forwardSpeed < t.DriftCancelSpeed)
                {
                    EndDrift(ref state, t, releaseBoost: false);
                }
                else
                {
                    state.DriftSeconds += dt;
                }

                return;
            }

            if (driftHeld && forwardSpeed >= t.DriftMinSpeed && Math.Abs(steer) >= t.DriftMinSteer)
            {
                state.Drifting = true;
                state.DriftDirection = Math.Sign(steer);
                state.DriftSeconds = 0f;
            }
        }

        /// <summary>
        /// Starts a boost of <paramref name="speed"/> extra top speed for <paramref name="seconds"/>,
        /// from a drift or the Boost item. A weaker boost never cuts short a stronger one still running.
        /// </summary>
        public static CarState Boost(CarState state, float speed, float seconds)
        {
            bool boosting = state.BoostSecondsLeft > 0f;
            state.BoostSpeed = boosting ? Math.Max(state.BoostSpeed, speed) : speed;
            state.BoostSecondsLeft = Math.Max(state.BoostSecondsLeft, seconds);
            return state;
        }

        /// <summary>
        /// A wreck (design doc §4): any drift ends without its boost, and any boost is cut, so the car
        /// can only slide to a stop. Without this, the drift a wreck ends would release a boost that
        /// keeps driving the car.
        /// </summary>
        public static CarState Wreck(CarState state, CarTuning tuning)
        {
            EndDrift(ref state, tuning, releaseBoost: false);
            state.BoostSecondsLeft = 0f;
            state.BoostSpeed = 0f;
            return state;
        }

        /// <summary>Spins the car out (an item hit): any drift and boost are lost.</summary>
        public static CarState SpinOut(CarState state, CarTuning tuning)
        {
            EndDrift(ref state, tuning, releaseBoost: false);
            state.BoostSecondsLeft = 0f;
            state.BoostSpeed = 0f;
            state.SpinSecondsLeft = Math.Max(state.SpinSecondsLeft, tuning.SpinOutSeconds);
            return state;
        }

        private static void EndDrift(ref CarState state, CarTuning t, bool releaseBoost)
        {
            if (state.Drifting && releaseBoost)
            {
                int tier = state.DriftTier(t);
                if (tier > 0)
                {
                    state = Boost(state, t.BoostSpeeds[tier - 1], t.BoostDurations[tier - 1]);
                }
            }

            state.Drifting = false;
            state.DriftDirection = 0;
            state.DriftSeconds = 0f;
        }

        /// <summary>0 at a standstill, rising to 1 at <see cref="CarTuning.FullSteerSpeed"/>, easing to the top-speed factor.</summary>
        private static float SteerFactor(float forwardSpeed, CarTuning t)
        {
            float speed = Math.Abs(forwardSpeed);
            if (speed < t.FullSteerSpeed)
            {
                return speed / t.FullSteerSpeed;
            }

            float fast = Clamp((speed - t.FullSteerSpeed) / Math.Max(1f, t.MaxSpeed - t.FullSteerSpeed), 0f, 1f);
            return Lerp(1f, t.TopSpeedSteerFactor, fast);
        }

        private static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }
}
