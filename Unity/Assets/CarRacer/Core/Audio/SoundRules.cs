using System;
using CarRacer.Core.Physics;

namespace CarRacer.Core.Audio
{
    public enum CrunchSize
    {
        Light,
        Medium,
        Heavy,
    }

    /// <summary>
    /// How the game sounds (design doc §13): engine pitch and loudness from speed, which crunch a
    /// bump makes and how loud, and the sound-effects and music volume settings.
    /// </summary>
    public static class SoundRules
    {
        public const float IdlePitch = 0.7f;
        public const float TopSpeedPitch = 1.5f;

        /// <summary>Extra pitch while a boost is pushing the car past its top speed.</summary>
        public const float BoostPitch = 0.2f;

        public const float IdleVolume = 0.3f;
        public const float TopSpeedVolume = 0.6f;

        /// <summary>The volume setting's steps: Off, Low, Medium and High.</summary>
        public static readonly float[] VolumeSteps = { 0f, 0.35f, 0.7f, 1f };

        private static readonly string[] VolumeNames = { "Off", "Low", "Medium", "High" };

        public const int DefaultVolumeStep = 3;

        /// <summary>Music starts at Medium, under the sound effects.</summary>
        public const int DefaultMusicStep = 2;

        /// <summary>Seconds to fade one tune out (and the next in) when the tune changes.</summary>
        public const float MusicFadeSeconds = 0.6f;

        public static float EnginePitch(float speed, float topSpeed, bool boosting) =>
            IdlePitch + (TopSpeedPitch - IdlePitch) * SpeedShare(speed, topSpeed) + (boosting ? BoostPitch : 0f);

        public static float EngineVolume(float speed, float topSpeed) =>
            IdleVolume + (TopSpeedVolume - IdleVolume) * SpeedShare(speed, topSpeed);

        /// <summary>
        /// The crunch a bump of <paramref name="impulse"/> makes: bigger and louder the harder it was,
        /// by <see cref="BumpSettings.Severity"/> per unit of the two cars' <paramref name="reducedMass"/>, like its
        /// haptic pulse.
        /// </summary>
        public static (CrunchSize Size, float Volume) Crunch(float impulse, float reducedMass, BumpSettings settings)
        {
            float t = settings.Severity(reducedMass > 0f ? impulse / reducedMass : 0f);
            var size = t < 1f / 3f ? CrunchSize.Light : t < 2f / 3f ? CrunchSize.Medium : CrunchSize.Heavy;
            return (size, 0.4f + 0.6f * t);
        }

        public static int ClampVolumeStep(int step) => Math.Max(0, Math.Min(VolumeSteps.Length - 1, step));

        /// <summary>The step after <paramref name="step"/>, wrapping from High back to Off.</summary>
        public static int NextVolumeStep(int step) => (ClampVolumeStep(step) + 1) % VolumeSteps.Length;

        public static string VolumeName(int step) => VolumeNames[ClampVolumeStep(step)];

        private static float SpeedShare(float speed, float topSpeed) =>
            topSpeed > 0f ? Math.Min(1f, Math.Abs(speed) / topSpeed) : 0f;
    }

    /// <summary>
    /// The engine's note, made in code rather than recorded so it loops without a click: a buzzy
    /// stack of harmonics, gently pulsing like an engine's firing. The car's speed bends its pitch.
    /// </summary>
    public static class EngineTone
    {
        public const int SampleRate = 22050;
        public const float Frequency = 70f;

        /// <summary>Whole periods in the loop; even, so the half-rate pulse also lines up at the seam.</summary>
        public const int Cycles = 28;

        public const int Harmonics = 6;
        public const float Peak = 0.8f;

        /// <summary>One seamless loop: exactly <see cref="Cycles"/> periods, peaking at <see cref="Peak"/>.</summary>
        public static float[] Loop()
        {
            int length = (int)Math.Round(Cycles * SampleRate / Frequency);
            var samples = new float[length];
            float peak = 0f;
            for (int i = 0; i < length; i++)
            {
                // The phase runs through exactly Cycles periods, so the last sample leads back into the first.
                double phase = 2.0 * Math.PI * Cycles * i / length;
                double value = 0.0;
                for (int n = 1; n <= Harmonics; n++)
                {
                    value += Math.Sin(n * phase) / n;
                }

                value *= 0.8 + 0.2 * Math.Sin(phase / 2.0);
                samples[i] = (float)value;
                peak = Math.Max(peak, Math.Abs(samples[i]));
            }

            for (int i = 0; i < length; i++)
            {
                samples[i] *= Peak / peak;
            }

            return samples;
        }
    }
}
