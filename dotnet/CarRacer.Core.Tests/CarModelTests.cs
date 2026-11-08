using System;
using CarRacer.Core.Driving;
using CarRacer.Core.Numerics;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class CarModelTests
    {
        private const float Dt = 0.02f; // fixed 50 Hz physics

        private static readonly CarTuning Tuning = new CarTuning();

        private static CarState Run(CarState state, CarInput input, float seconds, bool grounded = true, float dt = Dt)
        {
            int steps = (int)MathF.Round(seconds / dt);
            for (int i = 0; i < steps; i++)
            {
                state = CarModel.Step(state, input, grounded, dt, Tuning);
            }

            return state;
        }

        private static CarState Moving(float speed) => new CarState { Velocity = new Vec3(0f, 0f, speed) };

        private static float ForwardSpeed(CarState s) => Vec3.Dot(s.Velocity, s.Forward);

        [Fact]
        public void AcceleratesToTopSpeedAndNoFurther()
        {
            var s = Run(default, new CarInput(0f, 1f), 1f);
            Assert.Equal(Tuning.Acceleration, ForwardSpeed(s), 1);

            s = Run(s, new CarInput(0f, 1f), 10f);
            Assert.Equal(Tuning.MaxSpeed, ForwardSpeed(s), 3);
            Assert.Equal(0f, s.Heading, 5);
        }

        [Fact]
        public void BrakesToAStopThenReverses()
        {
            var s = Run(Moving(20f), new CarInput(0f, -1f), 20f / Tuning.Braking);
            Assert.InRange(ForwardSpeed(s), -0.5f, 0.5f);

            s = Run(s, new CarInput(0f, -1f), 10f);
            Assert.Equal(-Tuning.MaxReverseSpeed, ForwardSpeed(s), 3);
        }

        [Fact]
        public void CoastsToAStopWithoutThrottle()
        {
            var s = Run(Moving(10f), new CarInput(0f, 0f), 10f / Tuning.Coasting + 0.1f);
            Assert.Equal(0f, ForwardSpeed(s), 3);
        }

        [Fact]
        public void PositiveSteerTurnsRightAndCannotSpinOnTheSpot()
        {
            var stopped = Run(default, new CarInput(1f, 0f), 1f);
            Assert.Equal(0f, stopped.Heading, 5);

            var s = Run(Moving(10f), new CarInput(1f, 1f), 0.5f);
            Assert.InRange(s.Heading, 0.1f, MathF.PI / 2f);
            Assert.True(s.Velocity.X > 0f, "moving towards +X after turning right");

            var left = Run(Moving(10f), new CarInput(-1f, 1f), 0.5f);
            Assert.InRange(left.Heading, 3f * MathF.PI / 2f, 2f * MathF.PI - 0.1f);
        }

        [Fact]
        public void SteeringIsReducedAtTopSpeed()
        {
            var mid = CarModel.Step(Moving(Tuning.FullSteerSpeed), new CarInput(1f, 0f), true, Dt, Tuning);
            var fast = CarModel.Step(Moving(Tuning.MaxSpeed), new CarInput(1f, 0f), true, Dt, Tuning);
            Assert.True(fast.Heading < mid.Heading);
            // Coasting slows the car a little within the step, so the ratio is slightly above the factor.
            Assert.InRange(fast.Heading / mid.Heading, Tuning.TopSpeedSteerFactor, Tuning.TopSpeedSteerFactor + 0.03f);
        }

        [Fact]
        public void ReversingSteersTheOtherWay()
        {
            var s = Run(Moving(-6f), new CarInput(1f, 0f), 0.2f);
            Assert.True(s.Heading > MathF.PI, "heading decreased (wrapped) while reversing with right steer");
        }

        [Fact]
        public void GripRemovesSidewaysSlide()
        {
            // A sideways shove, e.g. from a bump, decays instead of persisting.
            var s = new CarState { Velocity = new Vec3(8f, 0f, 10f) };
            s = Run(s, new CarInput(0f, 0f), 0.5f);
            Assert.InRange(s.Velocity.X, 0f, 0.1f);
            Assert.True(s.Velocity.Z > 5f, "forward momentum mostly kept");
        }

        [Fact]
        public void DriftNeedsSpeedSteerAndButton()
        {
            Assert.False(Run(Moving(5f), new CarInput(1f, 0f, drift: true), 0.1f).Drifting, "too slow");
            Assert.False(Run(Moving(15f), new CarInput(0.1f, 1f, drift: true), 0.1f).Drifting, "not steering");
            Assert.False(Run(Moving(15f), new CarInput(1f, 1f, drift: false), 0.1f).Drifting, "button not held");

            var s = Run(Moving(15f), new CarInput(-1f, 1f, drift: true), 0.1f);
            Assert.True(s.Drifting);
            Assert.Equal(-1, s.DriftDirection);
        }

        [Fact]
        public void DriftKeepsTurningIntoTheDriftEvenWhenCounterSteering()
        {
            var s = Run(Moving(15f), new CarInput(1f, 1f, drift: true), 0.1f);
            float before = s.Heading;
            s = Run(s, new CarInput(-1f, 1f, drift: true), 0.3f);
            Assert.True(s.Drifting);
            Assert.True(s.Heading > before, "still turning right, just more gently");
        }

        [Theory]
        [InlineData(0.5f, 0)]
        [InlineData(1.0f, 1)]
        [InlineData(1.8f, 2)]
        [InlineData(3.0f, 3)]
        public void ReleasingADriftGivesTheEarnedBoostTier(float driftSeconds, int expectedTier)
        {
            var s = Run(Moving(20f), new CarInput(1f, 1f, drift: true), driftSeconds);
            Assert.Equal(expectedTier, s.DriftTier(Tuning));

            s = CarModel.Step(s, new CarInput(0f, 1f), true, Dt, Tuning);
            Assert.False(s.Drifting);
            if (expectedTier == 0)
            {
                Assert.Equal(0f, s.BoostSecondsLeft);
            }
            else
            {
                Assert.Equal(Tuning.BoostSpeeds[expectedTier - 1], s.BoostSpeed);
                Assert.InRange(s.BoostSecondsLeft, Tuning.BoostDurations[expectedTier - 1] - Dt * 2, Tuning.BoostDurations[expectedTier - 1]);
            }
        }

        [Fact]
        public void BoostRaisesTopSpeedThenSettlesBack()
        {
            var s = Moving(Tuning.MaxSpeed);
            s.BoostSpeed = 8f;
            s.BoostSecondsLeft = 1f;
            s = Run(s, new CarInput(0f, 1f), 0.9f);
            Assert.Equal(Tuning.MaxSpeed + 8f, ForwardSpeed(s), 2);

            s = Run(s, new CarInput(0f, 1f), 5f);
            Assert.Equal(0f, s.BoostSecondsLeft);
            Assert.Equal(Tuning.MaxSpeed, ForwardSpeed(s), 2);
        }

        [Fact]
        public void AnItemBoostStartsAtOnceAndAWeakerBoostNeverCutsItShort()
        {
            var s = CarModel.Boost(new CarState(), Tuning.ItemBoostSpeed, Tuning.ItemBoostDuration);
            Assert.Equal(Tuning.ItemBoostSpeed, s.BoostSpeed);
            Assert.Equal(Tuning.ItemBoostDuration, s.BoostSecondsLeft);

            var weaker = CarModel.Boost(s, Tuning.BoostSpeeds[0], Tuning.BoostDurations[0]);
            Assert.Equal(Tuning.ItemBoostSpeed, weaker.BoostSpeed);
            Assert.Equal(Tuning.ItemBoostDuration, weaker.BoostSecondsLeft);

            var expired = s;
            expired.BoostSecondsLeft = 0f;
            Assert.Equal(Tuning.BoostSpeeds[0], CarModel.Boost(expired, Tuning.BoostSpeeds[0], 1f).BoostSpeed);
        }

        [Fact]
        public void ASpinOutTakesControlAwayThenGivesItBack()
        {
            var s = new CarState { Velocity = new Vec3(0f, 0f, 20f), BoostSpeed = 8f, BoostSecondsLeft = 1f, Drifting = true, DriftDirection = 1, DriftSeconds = 2f };
            s = CarModel.SpinOut(s, Tuning);
            Assert.True(s.SpunOut);
            Assert.False(s.Drifting);
            Assert.Equal(0f, s.BoostSecondsLeft);

            // Full throttle and steering do nothing; the car turns and slows on its own.
            float headingBefore = s.Heading;
            var input = new CarInput(1f, 1f);
            int steps = (int)(Tuning.SpinOutSeconds / Dt) - 1;
            for (int i = 0; i < steps; i++)
            {
                s = CarModel.Step(s, input, grounded: true, Dt, Tuning);
            }

            Assert.True(s.SpunOut);
            Assert.True(s.Velocity.Length < 20f * 0.4f);
            Assert.NotEqual(headingBefore, s.Heading);

            s = CarModel.Step(s, input, grounded: true, Dt, Tuning);
            s = CarModel.Step(s, input, grounded: true, Dt, Tuning);
            Assert.False(s.SpunOut);
            float speedBefore = Vec3.Dot(s.Velocity, s.Forward);
            s = CarModel.Step(s, input, grounded: true, Dt, Tuning);
            Assert.True(Vec3.Dot(s.Velocity, s.Forward) > speedBefore, "accelerates again once back in control");
        }

        [Fact]
        public void BeingHitAgainDoesNotShortenASpin()
        {
            var s = CarModel.SpinOut(new CarState(), Tuning);
            s.SpinSecondsLeft = 2f;
            Assert.Equal(2f, CarModel.SpinOut(s, Tuning).SpinSecondsLeft);
        }

        [Fact]
        public void GasPullsAwayFromACarRollingBackSlowly()
        {
            // A slope (or a wall pushing back) takes a little forward speed away every physics step.
            var s = new CarState { Velocity = new Vec3(0f, 0f, -0.3f) };
            var gas = new CarInput(0f, 1f);
            for (int i = 0; i < 50; i++)
            {
                s = CarModel.Step(s, gas, grounded: true, Dt, Tuning);
                s.Velocity += new Vec3(0f, 0f, -0.1f);
            }

            Assert.True(s.Velocity.Z > 5f, $"expected the car to pull away, but its speed is {s.Velocity.Z}");
        }

        [Fact]
        public void BrakeReversesACarCreepingForwardSlowly()
        {
            var s = new CarState { Velocity = new Vec3(0f, 0f, 0.3f) };
            var reverse = new CarInput(0f, -1f);
            for (int i = 0; i < 50; i++)
            {
                s = CarModel.Step(s, reverse, grounded: true, Dt, Tuning);
                s.Velocity += new Vec3(0f, 0f, 0.1f);
            }

            Assert.True(s.Velocity.Z < -2f, $"expected the car to reverse, but its speed is {s.Velocity.Z}");
        }

        [Fact]
        public void BrakingWorksDuringABoost()
        {
            var s = Moving(Tuning.MaxSpeed);
            s.BoostSpeed = 8f;
            s.BoostSecondsLeft = 1.5f;
            s = Run(s, new CarInput(0f, -1f), 0.5f);
            Assert.Equal(Tuning.MaxSpeed - Tuning.Braking * 0.5f, ForwardSpeed(s), 1);
        }

        [Fact]
        public void BrakingAboveTopSpeedUsesFullBraking()
        {
            // Just after a boost ends: over the normal top speed with no boost left.
            var s = Moving(Tuning.MaxSpeed + 8f);
            for (int i = 0; i < 10; i++)
            {
                s = CarModel.Step(s, new CarInput(0f, -1f), true, Dt, Tuning);
            }

            Assert.Equal(Tuning.MaxSpeed + 8f - Tuning.Braking * Dt * 10, ForwardSpeed(s), 2);
        }

        [Fact]
        public void AWeakerBoostDoesNotCutAStrongerOneShort()
        {
            // Mid tier-3 boost at its top speed, the player releases a short (tier-1) drift.
            var s = Moving(Tuning.MaxSpeed + Tuning.BoostSpeeds[2]);
            s.BoostSpeed = Tuning.BoostSpeeds[2];
            s.BoostSecondsLeft = 1.2f;
            s = Run(s, new CarInput(1f, 1f, drift: true), Tuning.DriftTierSeconds[0] + 0.1f);
            Assert.Equal(1, s.DriftTier(Tuning));

            float before = ForwardSpeed(s);
            s = CarModel.Step(s, new CarInput(0f, 1f), true, Dt, Tuning);
            Assert.Equal(Tuning.BoostSpeeds[2], s.BoostSpeed);
            Assert.True(before - ForwardSpeed(s) < 0.5f, $"no sudden drop: {before} -> {ForwardSpeed(s)}");
        }

        [Fact]
        public void SpeedAboveABoostEasesOffRatherThanSnapping()
        {
            var s = Moving(Tuning.MaxSpeed + 11f);
            s.BoostSpeed = 5f;
            s.BoostSecondsLeft = 1f;
            var next = CarModel.Step(s, new CarInput(0f, 1f), true, Dt, Tuning);
            Assert.Equal(Tuning.MaxSpeed + 11f - Tuning.OverSpeedDeceleration * Dt, ForwardSpeed(next), 3);
        }

        [Fact]
        public void ReverseSpeedPastTheCapEasesOffRatherThanSnapping()
        {
            // A head-on bump knocks a car held on brake backwards faster than it can reverse.
            var brake = new CarInput(0f, -1f);
            var s = CarModel.Step(Moving(-12.5f), brake, true, Dt, Tuning);
            Assert.Equal(-12.5f + Tuning.OverSpeedDeceleration * Dt, ForwardSpeed(s), 3);

            s = Run(s, brake, 1f);
            Assert.Equal(-Tuning.MaxReverseSpeed, ForwardSpeed(s), 3);
        }

        [Fact]
        public void SlowingDownCancelsADriftWithoutBoost()
        {
            var s = Run(Moving(15f), new CarInput(1f, 0f, drift: true), 0.1f);
            Assert.True(s.Drifting);
            s = Run(s, new CarInput(1f, -1f, drift: true), 1.5f);
            Assert.False(s.Drifting);
            Assert.Equal(0f, s.BoostSecondsLeft);
        }

        [Fact]
        public void NoControlInTheAir()
        {
            var start = Moving(15f);
            start.Velocity = new Vec3(3f, 0f, 15f);
            var s = Run(start, new CarInput(1f, -1f), 0.5f, grounded: false);
            Assert.Equal(0f, s.Heading);
            Assert.Equal(start.Velocity, s.Velocity);

            var drifting = Run(Moving(15f), new CarInput(1f, 1f, drift: true), 1f);
            drifting = CarModel.Step(drifting, new CarInput(1f, 1f, drift: true), false, Dt, Tuning);
            Assert.False(drifting.Drifting);
            Assert.Equal(0f, drifting.BoostSecondsLeft);
        }

        [Fact]
        public void HandlingIsConsistentAcrossStepSizes()
        {
            var input = new CarInput(0.6f, 1f);
            var at50 = Run(Moving(10f), input, 2f, dt: 0.02f);
            var at100 = Run(Moving(10f), input, 2f, dt: 0.01f);
            Assert.True(Vec3.Distance(at50.Velocity, at100.Velocity) < 0.5f, $"{at50.Velocity} vs {at100.Velocity}");
            Assert.Equal(at50.Heading, at100.Heading, 1);
        }

        [Fact]
        public void HeadingStaysWrapped()
        {
            var s = Run(Moving(10f), new CarInput(1f, 1f), 10f);
            Assert.InRange(s.Heading, 0f, 2f * MathF.PI);
        }
    }
}
