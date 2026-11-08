using System;

namespace CarRacer.Core.Driving
{
    /// <summary>
    /// A body's handling ratings (design doc §4), each 1 to <see cref="MaxRating"/>, shown as bars in
    /// the garage and lobby. Every body's ratings add up to <see cref="Total"/>, so each trades one
    /// strength for another and none is simply better. A rating of 3 is the base tuning; each step
    /// either side moves the matching handling number by a few percent, enough to feel and not
    /// enough to decide a race.
    /// </summary>
    public readonly struct CarStats
    {
        public const int MaxRating = 5;
        public const int MidRating = 3;
        public const int Total = 9;

        /// <summary>The share of base top speed each rating step adds or takes away.</summary>
        public const float TopSpeedStep = 0.04f;

        /// <summary>The share of base acceleration each rating step adds or takes away.</summary>
        public const float AccelerationStep = 0.1f;

        /// <summary>The share of base turn rate each rating step adds or takes away.</summary>
        public const float SteeringStep = 0.07f;

        private static readonly CarTuning Base = new CarTuning();

        public CarStats(int topSpeed, int steering, int acceleration)
        {
            TopSpeed = topSpeed;
            Steering = steering;
            Acceleration = acceleration;
        }

        public int TopSpeed { get; }
        public int Steering { get; }
        public int Acceleration { get; }

        /// <summary>Every rating at the middle: the base tuning.</summary>
        public static CarStats Balanced => new CarStats(MidRating, MidRating, MidRating);

        public int Sum => TopSpeed + Steering + Acceleration;

        /// <summary>
        /// Sets <paramref name="tuning"/>'s top speed, acceleration and turn rate for these ratings,
        /// from the base tuning, in place: drivers that already hold the tuning (a bot's) see the
        /// change when a car swaps body in the lobby.
        /// </summary>
        public void ApplyTo(CarTuning tuning)
        {
            tuning.MaxSpeed = Base.MaxSpeed * Factor(TopSpeed, TopSpeedStep);
            tuning.Acceleration = Base.Acceleration * Factor(Acceleration, AccelerationStep);
            tuning.SteerDegreesPerSecond = Base.SteerDegreesPerSecond * Factor(Steering, SteeringStep);
        }

        /// <summary>A tuning with these ratings applied to the base.</summary>
        public CarTuning Tuning()
        {
            var tuning = new CarTuning();
            ApplyTo(tuning);
            return tuning;
        }

        private static float Factor(int rating, float step) => 1f + step * (Math.Clamp(rating, 1, MaxRating) - MidRating);
    }
}
