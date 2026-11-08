using System;
using CarRacer.Core.Physics;

namespace CarRacer.Core.Driving
{
    /// <summary>One vibration: how long, and how strong (1..255, Android's amplitude scale).</summary>
    public struct HapticPulse
    {
        public int Milliseconds;
        public int Amplitude;

        public HapticPulse(int milliseconds, int amplitude)
        {
            Milliseconds = milliseconds;
            Amplitude = amplitude;
        }
    }

    /// <summary>
    /// What the phone feels (design doc §5, haptics): a bump in proportion to how hard it was, and
    /// a kick for each drift boost that grows with the tier. Short and sharp, so they read as
    /// impacts rather than a buzz.
    /// </summary>
    public static class HapticPatterns
    {
        public const int MinBumpMs = 20;
        public const int MaxBumpMs = 70;
        public const int MinBumpAmplitude = 70;
        public const int MaxBumpAmplitude = 255;

        private static readonly HapticPulse[] BoostPulses =
        {
            new HapticPulse(25, 110),
            new HapticPulse(40, 180),
            new HapticPulse(60, 255),
        };

        /// <summary>
        /// A car-to-car bump. <paramref name="impulse"/> is the horizontal impulse this car took (mass × Δv), sized per
        /// unit of the two cars' <paramref name="reducedMass"/> as <see cref="BumpResolver"/> clamps it.
        /// </summary>
        public static HapticPulse ForBump(float impulse, float reducedMass, BumpSettings settings)
        {
            float perMass = reducedMass > 0f ? impulse / reducedMass : 0f;
            float range = settings.MaxImpulsePerMass - settings.MinImpulsePerMass;
            float t = range > 0f ? (perMass - settings.MinImpulsePerMass) / range : 1f;
            t = Math.Max(0f, Math.Min(1f, t));
            return new HapticPulse(
                (int)Math.Round(MinBumpMs + (MaxBumpMs - MinBumpMs) * t),
                (int)Math.Round(MinBumpAmplitude + (MaxBumpAmplitude - MinBumpAmplitude) * t));
        }

        /// <summary>A drift boost of tier 1..3 (higher tiers clamp to the strongest).</summary>
        public static HapticPulse ForBoost(int tier) => BoostPulses[Math.Max(1, Math.Min(tier, BoostPulses.Length)) - 1];

        /// <summary>
        /// The boost tier a physics step released, or 0. A release sets the boost timer to at least
        /// that tier's duration, so it rises instead of counting down; a drift cancelled without a
        /// boost, or a weaker release under a longer boost, leaves it counting down.
        /// </summary>
        public static int ReleasedBoostTier(CarState before, CarState after, float deltaSeconds, CarTuning tuning)
        {
            if (!before.Drifting || after.Drifting)
            {
                return 0;
            }

            float countedDown = Math.Max(0f, before.BoostSecondsLeft - deltaSeconds);
            bool restarted = after.BoostSecondsLeft > countedDown + 1e-4f;
            return restarted ? before.DriftTier(tuning) : 0;
        }
    }

    /// <summary>Drops pulses that come too close together (a scrape against another car), so they don't run into one long buzz.</summary>
    public sealed class HapticLimiter
    {
        public const long DefaultGapMs = 80;

        private readonly long _gapMs;
        private long _lastMs = long.MinValue;

        public HapticLimiter(long gapMs = DefaultGapMs)
        {
            _gapMs = gapMs;
        }

        public bool TryPlay(long nowMs)
        {
            if (_lastMs != long.MinValue && nowMs - _lastMs < _gapMs)
            {
                return false;
            }

            _lastMs = nowMs;
            return true;
        }
    }
}
