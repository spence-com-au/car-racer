using System.Linq;
using CarRacer.Core.Art;
using CarRacer.Core.Driving;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class CarStatsTests
    {
        private static readonly CarTuning Base = new CarTuning();

        [Fact]
        public void EveryBodyTradesOneStrengthForAnotherAndNoneIsSimplyBetter()
        {
            foreach (var body in CarModels.Bodies)
            {
                var stats = body.Stats;
                Assert.Equal(CarStats.Total, stats.Sum);
                foreach (int rating in new[] { stats.TopSpeed, stats.Steering, stats.Acceleration })
                {
                    Assert.InRange(rating, 1, CarStats.MaxRating);
                }
            }

            // No two bodies handle the same, so the choice means something.
            var distinct = CarModels.Bodies.Select(b => (b.Stats.TopSpeed, b.Stats.Steering, b.Stats.Acceleration)).Distinct();
            Assert.Equal(CarModels.Bodies.Length, distinct.Count());
        }

        [Fact]
        public void TheMiddleRatingIsTheBaseTuning()
        {
            var tuning = CarStats.Balanced.Tuning();
            Assert.Equal(Base.MaxSpeed, tuning.MaxSpeed, 4);
            Assert.Equal(Base.Acceleration, tuning.Acceleration, 4);
            Assert.Equal(Base.SteerDegreesPerSecond, tuning.SteerDegreesPerSecond, 4);
        }

        [Fact]
        public void EachRatingMovesOnlyItsOwnNumberAndOnlyAFewPercent()
        {
            var fast = new CarStats(5, 3, 3).Tuning();
            Assert.Equal(Base.MaxSpeed * (1f + 2f * CarStats.TopSpeedStep), fast.MaxSpeed, 4);
            Assert.Equal(Base.Acceleration, fast.Acceleration, 4);
            Assert.Equal(Base.SteerDegreesPerSecond, fast.SteerDegreesPerSecond, 4);

            // Pinned to the percentages design doc §4 states, not to the step constants, so changing one fails here.
            var nimble = new CarStats(3, 1, 3).Tuning();
            Assert.Equal(Base.SteerDegreesPerSecond * 0.86f, nimble.SteerDegreesPerSecond, 4);
            var punchy = new CarStats(3, 3, 4).Tuning();
            Assert.Equal(Base.Acceleration * 1.1f, punchy.Acceleration, 4);

            // Even the extremes stay within 10% of base top speed, so no car runs away with a race.
            Assert.InRange(new CarStats(1, 3, 3).Tuning().MaxSpeed / Base.MaxSpeed, 0.9f, 1f);
            Assert.InRange(new CarStats(5, 3, 3).Tuning().MaxSpeed / Base.MaxSpeed, 1f, 1.1f);
        }

        [Fact]
        public void ApplyingInPlaceKeepsTheSameTuningAndOutOfRangeRatingsAreClamped()
        {
            var tuning = new CarTuning();
            new CarStats(9, 0, 3).ApplyTo(tuning);
            Assert.Equal(new CarStats(5, 1, 3).Tuning().MaxSpeed, tuning.MaxSpeed, 4);
            Assert.Equal(new CarStats(5, 1, 3).Tuning().SteerDegreesPerSecond, tuning.SteerDegreesPerSecond, 4);

            CarStats.Balanced.ApplyTo(tuning);
            Assert.Equal(Base.MaxSpeed, tuning.MaxSpeed, 4);
        }

        [Fact]
        public void AHigherAccelerationRatingReachesSpeedSooner()
        {
            float SpeedAfterOneSecond(CarStats stats)
            {
                var tuning = stats.Tuning();
                var state = new CarState();
                for (int i = 0; i < 50; i++)
                {
                    state = CarRacer.Core.Driving.CarModel.Step(state, new CarInput(0f, 1f), true, 0.02f, tuning);
                }

                return state.Velocity.Length;
            }

            Assert.True(SpeedAfterOneSecond(new CarStats(3, 3, 4)) > SpeedAfterOneSecond(new CarStats(3, 3, 2)));
        }
    }
}
