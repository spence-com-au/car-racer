using System;
using System.Linq;
using CarRacer.Core.Audio;
using CarRacer.Core.Numerics;
using CarRacer.Core.Physics;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class SoundTests
    {
        private static readonly BumpSettings Bumps = new BumpSettings();

        [Fact]
        public void TheEngineRisesWithSpeedAndMoreWhenBoosting()
        {
            Assert.Equal(SoundRules.IdlePitch, SoundRules.EnginePitch(0f, 26f, false));
            Assert.Equal(SoundRules.TopSpeedPitch, SoundRules.EnginePitch(26f, 26f, false));
            Assert.Equal(SoundRules.TopSpeedPitch, SoundRules.EnginePitch(40f, 26f, false));
            Assert.Equal(SoundRules.EnginePitch(13f, 26f, false), SoundRules.EnginePitch(-13f, 26f, false));
            Assert.Equal(SoundRules.TopSpeedPitch + SoundRules.BoostPitch, SoundRules.EnginePitch(30f, 26f, true), 4);
            Assert.True(SoundRules.EngineVolume(20f, 26f) > SoundRules.EngineVolume(5f, 26f));
        }

        [Fact]
        public void HarderBumpsCrunchBiggerAndLouder()
        {
            var gentle = SoundRules.Crunch(Bumps.MinImpulsePerMass, 1f, Bumps);
            var middling = SoundRules.Crunch((Bumps.MinImpulsePerMass + Bumps.MaxImpulsePerMass) / 2f, 1f, Bumps);
            var hard = SoundRules.Crunch(Bumps.MaxImpulsePerMass * 2f, 1f, Bumps);

            Assert.Equal(CrunchSize.Light, gentle.Size);
            Assert.Equal(CrunchSize.Medium, middling.Size);
            Assert.Equal(CrunchSize.Heavy, hard.Size);
            Assert.Equal(0.4f, gentle.Volume, 4);
            Assert.Equal(1f, hard.Volume, 4);
        }

        [Fact]
        public void TheHardestCarToCarBumpCrunchesHeavy()
        {
            var self = new CarBody(Vec3.Zero, new Vec3(30f, 0f, 0f), BumpMonitor.CarMass);
            var other = new CarBody(new Vec3(1f, 0f, 0f), new Vec3(-30f, 0f, 0f), BumpMonitor.CarMass);
            Vec3 impulse = BumpResolver.ComputeSelfImpulse(self, other, Bumps);

            var crunch = SoundRules.Crunch(impulse.Flattened.Length, BumpMonitor.CarMass / 2f, Bumps);
            Assert.Equal(CrunchSize.Heavy, crunch.Size);
            Assert.Equal(1f, crunch.Volume, 4);
        }

        [Fact]
        public void TheVolumeSettingCyclesFromOffToHighAndBack()
        {
            Assert.Equal("High", SoundRules.VolumeName(SoundRules.DefaultVolumeStep));
            Assert.Equal(0, SoundRules.NextVolumeStep(SoundRules.DefaultVolumeStep));
            Assert.Equal(0f, SoundRules.VolumeSteps[0]);
            Assert.Equal(1, SoundRules.NextVolumeStep(0));
            Assert.Equal("Off", SoundRules.VolumeName(-3));
            Assert.Equal("High", SoundRules.VolumeName(99));
        }

        [Fact]
        public void TheEngineToneLoopsWithoutAClick()
        {
            float[] loop = EngineTone.Loop();
            Assert.Equal(EngineTone.Cycles * EngineTone.SampleRate / EngineTone.Frequency, loop.Length, 0);
            Assert.Equal(EngineTone.Peak, loop.Max(Math.Abs), 4);

            // The jump from the last sample back to the first is no bigger than the largest step inside the loop.
            float largestStep = 0f;
            for (int i = 1; i < loop.Length; i++)
            {
                largestStep = Math.Max(largestStep, Math.Abs(loop[i] - loop[i - 1]));
            }

            Assert.True(Math.Abs(loop[0] - loop[loop.Length - 1]) <= largestStep);
        }
    }
}
