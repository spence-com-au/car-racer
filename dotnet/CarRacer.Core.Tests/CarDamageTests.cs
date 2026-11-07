using CarRacer.Core.Driving;
using CarRacer.Core.Net;
using CarRacer.Core.Numerics;
using CarRacer.Core.Physics;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class CarDamageTests
    {
        private static readonly BumpSettings Bumps = new BumpSettings();

        [Fact]
        public void HarderBumpsAndWallHitsDoMoreDamage()
        {
            var gentle = new CarDamage();
            gentle.AddBump(Bumps.MinImpulsePerMass, Bumps);
            var hard = new CarDamage();
            hard.AddBump(Bumps.MaxImpulsePerMass * 2f, Bumps);
            Assert.Equal(CarDamage.BumpMinDamage, gentle.Value, 4);
            Assert.Equal(CarDamage.BumpMaxDamage, hard.Value, 4);

            var scrape = new CarDamage();
            Assert.False(scrape.AddWallHit(CarDamage.WallMinSpeed - 0.1f));
            Assert.Equal(0f, scrape.Value);
            var slam = new CarDamage();
            slam.AddWallHit(CarDamage.WallFullSpeed + 10f);
            Assert.Equal(CarDamage.WallMaxDamage, slam.Value, 4);
        }

        [Fact]
        public void DamageSlowsTheCarByUpToItsMaximum()
        {
            var damage = new CarDamage();
            Assert.Equal(1f, damage.PerformanceFactor);
            damage.Add(0.5f);
            Assert.Equal(1f - CarDamage.MaxSlowdown * 0.5f, damage.PerformanceFactor, 4);
            Assert.Equal(1, damage.SmokeLevel);
            damage.Add(0.3f);
            Assert.Equal(2, damage.SmokeLevel);
        }

        [Fact]
        public void FullDamageWrecksTheCarThenItComesBackMended()
        {
            var damage = new CarDamage();
            Assert.False(damage.AddItemHit());
            Assert.False(damage.AddItemHit());
            Assert.False(damage.AddItemHit());
            Assert.True(damage.AddItemHit(), "the fourth item hit wrecks it");
            Assert.True(damage.Wrecked);
            Assert.Equal(2, damage.SmokeLevel);

            // Nothing more lands on a wreck.
            Assert.False(damage.AddItemHit());

            Assert.False(damage.Tick(CarDamage.WreckSeconds - 0.1f));
            Assert.True(damage.Wrecked);
            Assert.True(damage.Tick(0.2f), "mended on the step the wreck ends");
            Assert.False(damage.Wrecked);
            Assert.Equal(0f, damage.Value);
        }

        [Fact]
        public void DamageMendsOnlyAfterAQuietSpell()
        {
            var damage = new CarDamage();
            damage.Add(0.5f);
            damage.Tick(CarDamage.RepairDelaySeconds - 0.5f);
            Assert.Equal(0.5f, damage.Value, 4);
            damage.Tick(0.5f);
            damage.Tick(2f);
            Assert.Equal(0.5f - CarDamage.RepairPerSecond * 2f, damage.Value, 3);

            // A new hit starts the wait again.
            damage.Add(0.1f);
            float before = damage.Value;
            damage.Tick(1f);
            Assert.Equal(before, damage.Value, 4);

            damage.Tick(100f);
            Assert.Equal(0f, damage.Value);
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(1, false)]
        [InlineData(2, false)]
        [InlineData(2, true)]
        public void DamageTravelsInTheCarStateFlags(int smoke, bool wrecked)
        {
            var sent = new CarStateMessage { PlayerId = 3, Flags = CarStateFlags.Drifting | DamageFlags.For(smoke, wrecked), Position = new Vec3(1f, 2f, 3f) };
            var received = Assert.IsType<CarStateMessage>(MessageCodec.Decode(MessageCodec.Encode(sent)));
            Assert.Equal(smoke, DamageFlags.SmokeLevel(received.Flags));
            Assert.Equal(wrecked, DamageFlags.Wrecked(received.Flags));
            Assert.True((received.Flags & CarStateFlags.Drifting) != 0, "the other flags are untouched");
        }

        [Fact]
        public void ACarsFlagsCarryItsHandlingShieldAndDamage()
        {
            var damage = new CarDamage();
            Assert.Equal(CarStateFlags.None, CarFlags.For(new CarState(), false, damage));

            damage.AddItemHit();
            damage.AddItemHit();
            var state = new CarState { Drifting = true, BoostSecondsLeft = 1f, SpinSecondsLeft = 1f };
            var flags = CarFlags.For(state, true, damage);
            Assert.Equal(CarStateFlags.Drifting | CarStateFlags.Boosting | CarStateFlags.Shielded | CarStateFlags.SpunOut | CarStateFlags.SmokeLight, flags);
        }

        [Fact]
        public void AWreckEndsADriftWithoutItsBoostAndCutsAnyBoost()
        {
            var tuning = new CarTuning();
            var drifting = new CarState { Drifting = true, DriftDirection = 1, DriftSeconds = tuning.DriftTierSeconds[2] + 1f };
            var wrecked = CarModel.Wreck(drifting, tuning);
            Assert.False(wrecked.Drifting);
            Assert.Equal(0f, wrecked.BoostSecondsLeft);

            var boosting = CarModel.Boost(new CarState(), 10f, 2f);
            Assert.Equal(0f, CarModel.Wreck(boosting, tuning).BoostSpeed);

            // With no input after it, the wreck only slows down.
            var state = CarModel.Wreck(new CarState { Velocity = new Vec3(0f, 0f, 20f), Drifting = true, DriftDirection = 1, DriftSeconds = 3f }, tuning);
            for (int i = 0; i < 10; i++)
            {
                float before = state.Velocity.Length;
                state = CarModel.Step(state, default, true, 0.02f, tuning);
                Assert.True(state.Velocity.Length <= before + 1e-4f);
            }
        }

        [Fact]
        public void ADamagedCarIsSlowerAndPullsAwayMoreSlowly()
        {
            var tuning = new CarTuning();
            float SpeedAfter(float seconds, float performance)
            {
                var state = new CarState();
                for (float t = 0f; t < seconds; t += 0.02f)
                {
                    state = CarModel.Step(state, new CarInput(0f, 1f), true, 0.02f, tuning, performance);
                }

                return state.Velocity.Length;
            }

            Assert.Equal(tuning.MaxSpeed * 0.85f, SpeedAfter(10f, 0.85f), 2);
            Assert.True(SpeedAfter(0.5f, 0.85f) < SpeedAfter(0.5f, 1f));
            Assert.Equal(tuning.MaxSpeed, SpeedAfter(10f, 1f), 2);
        }
    }
}
