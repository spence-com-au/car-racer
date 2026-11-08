using CarRacer.Core.Driving;
using CarRacer.Core.Numerics;
using CarRacer.Core.Physics;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class HapticsTests
    {
        private const float Dt = 0.02f;
        private static readonly BumpSettings Bumps = new BumpSettings();
        private static readonly CarTuning Tuning = new CarTuning();

        [Fact]
        public void HarderBumpsAreLongerAndStronger()
        {
            var light = HapticPatterns.ForBump(Bumps.MinImpulsePerMass, 1f, Bumps);
            var heavy = HapticPatterns.ForBump(Bumps.MaxImpulsePerMass, 1f, Bumps);
            Assert.Equal(HapticPatterns.MinBumpMs, light.Milliseconds);
            Assert.Equal(HapticPatterns.MinBumpAmplitude, light.Amplitude);
            Assert.Equal(HapticPatterns.MaxBumpMs, heavy.Milliseconds);
            Assert.Equal(HapticPatterns.MaxBumpAmplitude, heavy.Amplitude);

            var middle = HapticPatterns.ForBump((Bumps.MinImpulsePerMass + Bumps.MaxImpulsePerMass) / 2f, 1f, Bumps);
            Assert.InRange(middle.Milliseconds, light.Milliseconds + 1, heavy.Milliseconds - 1);
        }

        [Fact]
        public void BumpStrengthIsClampedToAndroidsRange()
        {
            Assert.Equal(HapticPatterns.MaxBumpAmplitude, HapticPatterns.ForBump(1000f, 1f, Bumps).Amplitude);
            Assert.Equal(HapticPatterns.MinBumpAmplitude, HapticPatterns.ForBump(0f, 1f, Bumps).Amplitude);
            Assert.InRange(HapticPatterns.ForBump(1000f, 1f, Bumps).Amplitude, 1, 255);
        }

        [Fact]
        public void BigBoostsKickHarder()
        {
            var t1 = HapticPatterns.ForBoost(1);
            var t3 = HapticPatterns.ForBoost(3);
            Assert.True(t3.Milliseconds > t1.Milliseconds && t3.Amplitude > t1.Amplitude);
            Assert.Equal(t3.Amplitude, HapticPatterns.ForBoost(7).Amplitude);
            Assert.Equal(t1.Amplitude, HapticPatterns.ForBoost(0).Amplitude);
        }

        private static CarState Drifting(float seconds) => new CarState
        {
            Velocity = new Vec3(0f, 0f, 20f),
            Drifting = true,
            DriftDirection = 1,
            DriftSeconds = seconds,
        };

        [Fact]
        public void ReleasingAChargedDriftIsABoost()
        {
            var before = Drifting(Tuning.DriftTierSeconds[1] + 0.1f); // tier 2
            var after = CarModel.Step(before, new CarInput(1f, 1f, drift: false), true, Dt, Tuning);
            Assert.Equal(2, HapticPatterns.ReleasedBoostTier(before, after, Dt, Tuning));
        }

        [Fact]
        public void DriftingOnIsNotABoost()
        {
            var before = Drifting(1f);
            var after = CarModel.Step(before, new CarInput(1f, 1f, drift: true), true, Dt, Tuning);
            Assert.Equal(0, HapticPatterns.ReleasedBoostTier(before, after, Dt, Tuning));
        }

        [Fact]
        public void ADriftLostInTheAirIsNotABoost()
        {
            var before = Drifting(Tuning.DriftTierSeconds[2] + 0.1f);
            before.BoostSecondsLeft = 0.5f; // an earlier boost still running
            before.BoostSpeed = Tuning.BoostSpeeds[0];
            var after = CarModel.Step(before, new CarInput(1f, 1f, drift: true), false, Dt, Tuning);
            Assert.False(after.Drifting);
            Assert.Equal(0, HapticPatterns.ReleasedBoostTier(before, after, Dt, Tuning));
        }

        [Fact]
        public void ADriftLostInTheAirWithNoBoostRunningIsNotABoost()
        {
            var before = Drifting(Tuning.DriftTierSeconds[2] + 0.1f);
            var after = CarModel.Step(before, new CarInput(1f, 1f, drift: true), false, Dt, Tuning);
            Assert.False(after.Drifting);
            Assert.Equal(0, HapticPatterns.ReleasedBoostTier(before, after, Dt, Tuning));
        }

        [Fact]
        public void ReleasingTooEarlyIsNotABoost()
        {
            var before = Drifting(Tuning.DriftTierSeconds[0] / 2f);
            var after = CarModel.Step(before, new CarInput(1f, 1f, drift: false), true, Dt, Tuning);
            Assert.Equal(0, HapticPatterns.ReleasedBoostTier(before, after, Dt, Tuning));
        }

        [Fact]
        public void TheLimiterSpacesPulsesOut()
        {
            var limiter = new HapticLimiter(gapMs: 80);
            Assert.True(limiter.TryPlay(1000));
            Assert.False(limiter.TryPlay(1040));
            Assert.True(limiter.TryPlay(1080));
        }
    }
}
