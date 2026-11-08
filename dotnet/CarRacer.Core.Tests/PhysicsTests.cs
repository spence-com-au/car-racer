using CarRacer.Core.Net;
using CarRacer.Core.Numerics;
using CarRacer.Core.Physics;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class PhysicsTests
    {
        private static readonly BumpSettings Settings = new BumpSettings();

        [Fact]
        public void HeadOnBumpPushesBothCarsApartEqually()
        {
            var a = new CarBody(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 10f));
            var b = new CarBody(new Vec3(0f, 0f, 1.9f), new Vec3(0f, 0f, -10f));

            Vec3 onA = BumpResolver.ComputeSelfImpulse(a, b, Settings);
            Vec3 onB = BumpResolver.ComputeSelfImpulse(b, a, Settings);

            Assert.True(onA.Z < 0f, "A is pushed back");
            Assert.True(onB.Z > 0f, "B is pushed back");
            Assert.True(onA.Flattened.ApproximatelyEquals(-onB.Flattened));
            Assert.Equal(onA.Y, onB.Y, 5);
            Assert.True(onA.Y > 0f, "bumps pop cars slightly upward");
        }

        [Theory]
        [InlineData(0f, 0f, 5f, 3f, 0.5f, 1f, -2f, 1f, 1f, 2f)]
        [InlineData(1f, 0f, 2f, -8f, 0f, 9f, 3f, 0.2f, 1f, 1f)]
        [InlineData(-3f, 1f, 0f, 20f, 0.7f, -0.2f, -15f, 0f, 2f, 1f)]
        public void ImpulseIsAntisymmetricOnTheGroundPlane(
            float ax, float az, float avz, float avx, float bx, float bz, float bvx, float bvz, float massA, float massB)
        {
            var a = new CarBody(new Vec3(ax, 0f, az), new Vec3(avx, 0f, avz), massA);
            var b = new CarBody(new Vec3(bx, 0f, bz), new Vec3(bvx, 0f, bvz), massB);

            Vec3 onA = BumpResolver.ComputeSelfImpulse(a, b, Settings);
            Vec3 onB = BumpResolver.ComputeSelfImpulse(b, a, Settings);

            Assert.True(onA.Flattened.ApproximatelyEquals(-onB.Flattened, 1e-3f), $"{onA} vs {onB}");
            Assert.Equal(onA.Y, onB.Y, 4);
        }

        [Fact]
        public void SeparatingCarsGetNoImpulse()
        {
            var a = new CarBody(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, -5f));
            var b = new CarBody(new Vec3(0f, 0f, 1.5f), new Vec3(0f, 0f, 5f));
            Assert.Equal(Vec3.Zero, BumpResolver.ComputeSelfImpulse(a, b, Settings));
        }

        [Fact]
        public void GentleTouchStillNudges()
        {
            var a = new CarBody(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 0.01f));
            var b = new CarBody(new Vec3(0f, 0f, 2f), Vec3.Zero);
            Vec3 impulse = BumpResolver.ComputeSelfImpulse(a, b, Settings);
            float reducedMass = 0.5f;
            Assert.Equal(Settings.MinImpulsePerMass * reducedMass, impulse.Flattened.Length, 4);
        }

        [Fact]
        public void HugeImpactIsClamped()
        {
            var a = new CarBody(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 500f));
            var b = new CarBody(new Vec3(0f, 0f, 2f), new Vec3(0f, 0f, -500f));
            Vec3 impulse = BumpResolver.ComputeSelfImpulse(a, b, Settings);
            Assert.Equal(Settings.MaxImpulsePerMass * 0.5f, impulse.Flattened.Length, 3);
        }

        [Fact]
        public void HeavierCarIsDeflectedLess()
        {
            var light = new CarBody(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 5f), mass: 1f);
            var heavy = new CarBody(new Vec3(0f, 0f, 2f), new Vec3(0f, 0f, -5f), mass: 3f);

            float lightDeltaV = BumpResolver.ComputeSelfImpulse(light, heavy, Settings).Flattened.Length / light.Mass;
            float heavyDeltaV = BumpResolver.ComputeSelfImpulse(heavy, light, Settings).Flattened.Length / heavy.Mass;
            Assert.True(lightDeltaV > heavyDeltaV);
        }

        [Fact]
        public void CoincidentCentresStillSeparate()
        {
            var a = new CarBody(Vec3.Zero, new Vec3(1f, 0f, 0f));
            var b = new CarBody(Vec3.Zero, new Vec3(-1f, 0f, 0f));
            Vec3 onA = BumpResolver.ComputeSelfImpulse(a, b, Settings);
            Vec3 onB = BumpResolver.ComputeSelfImpulse(b, a, Settings);
            Assert.True(onA.X < 0f);
            Assert.True(onA.Flattened.ApproximatelyEquals(-onB.Flattened));
        }

        [Fact]
        public void TouchingUsesRadiiOnTheGroundPlane()
        {
            var a = new CarBody(Vec3.Zero, Vec3.Zero, radius: 1f);
            var b = new CarBody(new Vec3(1.9f, 5f, 0f), Vec3.Zero, radius: 1f);
            Assert.True(BumpResolver.AreTouching(a, b));
            Assert.False(BumpResolver.AreTouching(a, new CarBody(new Vec3(2.1f, 0f, 0f), Vec3.Zero, radius: 1f)));
        }

        [Fact]
        public void DeduplicatorAppliesEachBumpOnce()
        {
            var dedupe = new HitDeduplicator(windowMs: 100);

            // Local detection first, then the other phone's fallback message for the same bump.
            Assert.True(dedupe.TryApplyLocalContact(2, 1000));
            Assert.False(dedupe.TryApplyRemoteHit(2, 1040));

            // Fallback first (we missed it locally), then a late local detection.
            Assert.True(dedupe.TryApplyRemoteHit(3, 1000));
            Assert.False(dedupe.TryApplyLocalContact(3, 1090));

            // A new bump after the window applies again; other opponents are independent.
            Assert.True(dedupe.TryApplyLocalContact(2, 1200));
            Assert.True(dedupe.TryApplyLocalContact(4, 1200));
        }

        private static CarStateMessage State(uint seq, Vec3 position, Vec3 velocity) =>
            new CarStateMessage { Sequence = seq, Position = position, Velocity = velocity };

        [Fact]
        public void PredictorExtrapolatesByAgeAndLatency()
        {
            var predictor = new RemoteCarPredictor();
            predictor.OnState(State(1, Vec3.Zero, new Vec3(0f, 0f, 20f)), receivedAtMs: 1000, oneWayLatencyMs: 10f);

            // 40 ms since receipt + 10 ms latency = 50 ms ahead at 20 m/s = 1 m.
            Assert.True(predictor.PredictRaw(1040, 10f).ApproximatelyEquals(new Vec3(0f, 0f, 1f)));
        }

        [Fact]
        public void PredictorCapsExtrapolation()
        {
            var predictor = new RemoteCarPredictor(new PredictionSettings { MaxExtrapolationMs = 250f });
            predictor.OnState(State(1, Vec3.Zero, new Vec3(0f, 0f, 20f)), 0, 0f);
            Assert.True(predictor.PredictRaw(10_000, 0f).ApproximatelyEquals(new Vec3(0f, 0f, 5f)));
        }

        [Fact]
        public void PredictorIgnoresStaleStatesAcrossSequenceWrap()
        {
            var predictor = new RemoteCarPredictor();
            Assert.True(predictor.OnState(State(uint.MaxValue, Vec3.Zero, Vec3.Zero), 0, 0f));
            Assert.True(predictor.OnState(State(0, new Vec3(1f, 0f, 0f), Vec3.Zero), 10, 0f)); // wrapped: newer
            Assert.False(predictor.OnState(State(uint.MaxValue, Vec3.Zero, Vec3.Zero), 20, 0f));
            Assert.Equal(0u, predictor.Latest.Sequence);
        }

        [Fact]
        public void PredictorFollowsTheCarWhenABotTakesOverOrHandsBack()
        {
            var predictor = new RemoteCarPredictor();
            Assert.True(predictor.OnState(State(5000, Vec3.Zero, Vec3.Zero), 0, 0f));

            var bot = State(1, new Vec3(1f, 0f, 0f), Vec3.Zero);
            bot.Flags = CarStateFlags.BotControlled;
            Assert.True(predictor.OnState(bot, 10, 0f));
            Assert.Equal(1u, predictor.Latest.Sequence);

            // The bot counts past the guest's sequence, so the guest's hand-back state is older by sequence alone.
            var botLater = State(6000, new Vec3(2f, 0f, 0f), Vec3.Zero);
            botLater.Flags = CarStateFlags.BotControlled;
            Assert.True(predictor.OnState(botLater, 20, 0f));
            Assert.False(predictor.OnState(bot, 30, 0f));

            Assert.True(predictor.OnState(State(5001, new Vec3(3f, 0f, 0f), Vec3.Zero), 40, 0f));
            Assert.Equal(5001u, predictor.Latest.Sequence);
        }

        [Fact]
        public void CorrectionsAreSmoothedNotSnapped()
        {
            var predictor = new RemoteCarPredictor(new PredictionSettings { SmoothingSeconds = 0.1f });
            predictor.OnState(State(1, Vec3.Zero, Vec3.Zero), 0, 0f);
            predictor.UpdateDisplayPosition(0, 0f, 0.016f);

            predictor.OnState(State(2, new Vec3(1f, 0f, 0f), Vec3.Zero), 33, 0f);
            Vec3 justAfter = predictor.UpdateDisplayPosition(33, 0f, 0f);
            Assert.True(justAfter.ApproximatelyEquals(Vec3.Zero), "no visible jump at the moment of correction");

            Vec3 later = predictor.UpdateDisplayPosition(533, 0f, 0.5f);
            Assert.True(later.ApproximatelyEquals(new Vec3(1f, 0f, 0f), 0.01f), $"converged, got {later}");
        }

        [Fact]
        public void LargeCorrectionsSnap()
        {
            var predictor = new RemoteCarPredictor(new PredictionSettings { SnapDistance = 5f });
            predictor.OnState(State(1, Vec3.Zero, Vec3.Zero), 0, 0f);
            predictor.OnState(State(2, new Vec3(50f, 0f, 0f), Vec3.Zero), 33, 0f);
            Assert.True(predictor.UpdateDisplayPosition(33, 0f, 0f).ApproximatelyEquals(new Vec3(50f, 0f, 0f)));
        }
    }
}
