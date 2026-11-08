using System;
using CarRacer.Core.Physics;

namespace CarRacer.Core.Driving
{
    /// <summary>
    /// A car's damage (design doc §4), 0 to 1, kept where the car is simulated. Bumps with other cars,
    /// hard hits on walls and item hits add to it; it takes away up to <see cref="MaxSlowdown"/> of
    /// top speed and acceleration, and mends by itself once the car has gone a while without a hit.
    /// At full damage the car is wrecked: it stops for <see cref="WreckSeconds"/>, then is good as new.
    /// </summary>
    public sealed class CarDamage
    {
        /// <summary>The share of top speed and acceleration a fully damaged car has lost.</summary>
        public const float MaxSlowdown = 0.15f;

        /// <summary>An item hit that lands (not soaked up by a Shield).</summary>
        public const float ItemHitDamage = 0.25f;

        /// <summary>A car-to-car bump does this much, from the gentlest to the hardest (by <see cref="BumpSettings.Severity"/>).</summary>
        public const float BumpMinDamage = 0.02f;
        public const float BumpMaxDamage = 0.12f;

        /// <summary>A wall hit slower than this (straight into it) does nothing; scraping along a wall is free.</summary>
        public const float WallMinSpeed = 6f;

        /// <summary>A wall hit at this speed or more does <see cref="WallMaxDamage"/>.</summary>
        public const float WallFullSpeed = CarTuning.BaseMaxSpeed;
        public const float WallMinDamage = 0.03f;
        public const float WallMaxDamage = 0.2f;

        /// <summary>Damage starts to mend after this long without a hit, at <see cref="RepairPerSecond"/>.</summary>
        public const float RepairDelaySeconds = 3f;
        public const float RepairPerSecond = 0.05f;

        public const float WreckSeconds = 2f;

        /// <summary>Damage at or above these shows light or heavy smoke on every phone.</summary>
        public const float LightSmoke = 1f / 3f;
        public const float HeavySmoke = 2f / 3f;

        private float _sinceHit = RepairDelaySeconds;

        /// <summary>0 (undamaged) to 1 (wrecked).</summary>
        public float Value { get; private set; }

        public float WreckSecondsLeft { get; private set; }

        public bool Wrecked => WreckSecondsLeft > 0f;

        /// <summary>The share of top speed and acceleration the car has left (1 undamaged; 0.85 at full damage).</summary>
        public float PerformanceFactor => 1f - MaxSlowdown * Value;

        /// <summary>0 for no smoke, 1 for light, 2 for heavy (a wreck is always heavy).</summary>
        public int SmokeLevel => Wrecked || Value >= HeavySmoke ? 2 : Value >= LightSmoke ? 1 : 0;

        /// <summary>
        /// A bump from another car of <paramref name="impulsePerMass"/>, its impulse per unit of the two cars' reduced
        /// mass: twice this car's change in speed when the cars weigh the same. True if it wrecked the car.
        /// </summary>
        public bool AddBump(float impulsePerMass, BumpSettings settings)
        {
            return Add(BumpMinDamage + (BumpMaxDamage - BumpMinDamage) * settings.Severity(impulsePerMass));
        }

        /// <summary>A wall hit at <paramref name="speedIntoWall"/> m/s, straight into it. True if it wrecked the car.</summary>
        public bool AddWallHit(float speedIntoWall)
        {
            if (speedIntoWall < WallMinSpeed)
            {
                return false;
            }

            float t = (speedIntoWall - WallMinSpeed) / (WallFullSpeed - WallMinSpeed);
            return Add(WallMinDamage + (WallMaxDamage - WallMinDamage) * Clamp01(t));
        }

        /// <summary>An item hit that landed. True if it wrecked the car.</summary>
        public bool AddItemHit() => Add(ItemHitDamage);

        /// <summary>Adds <paramref name="amount"/> of damage; nothing more lands on a wreck. True if this wrecked the car.</summary>
        public bool Add(float amount)
        {
            if (Wrecked || amount <= 0f)
            {
                return false;
            }

            _sinceHit = 0f;
            Value = Math.Min(1f, Value + amount);
            if (Value < 1f)
            {
                return false;
            }

            WreckSecondsLeft = WreckSeconds;
            return true;
        }

        /// <summary>Moves time on: a wreck counts down and comes back mended; damage otherwise mends after a quiet spell. True on the step a wreck is mended.</summary>
        public bool Tick(float deltaSeconds)
        {
            if (Wrecked)
            {
                WreckSecondsLeft = Math.Max(0f, WreckSecondsLeft - deltaSeconds);
                if (WreckSecondsLeft > 0f)
                {
                    return false;
                }

                Reset();
                return true;
            }

            // Only the part of this step after the quiet spell mends.
            _sinceHit += deltaSeconds;
            float mending = Math.Min(deltaSeconds, _sinceHit - RepairDelaySeconds);
            if (mending > 0f)
            {
                Value = Math.Max(0f, Value - RepairPerSecond * mending);
            }

            return false;
        }

        /// <summary>Good as new, e.g. at a race start.</summary>
        public void Reset()
        {
            Value = 0f;
            WreckSecondsLeft = 0f;
            _sinceHit = RepairDelaySeconds;
        }

        private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));
    }
}
