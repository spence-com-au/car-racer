using System;
using System.Collections.Generic;
using System.Linq;
using CarRacer.Core.Driving;
using CarRacer.Core.Net;
using CarRacer.Core.Numerics;
using CarRacer.Core.Physics;
using CarRacer.Core.Session;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class BumpMonitorTests
    {
        private readonly BumpMonitor _monitor = new BumpMonitor();
        private readonly List<Bump> _bumps = new List<Bump>();

        private void Step(Vec3 selfPos, Vec3 selfVel, long now, params OtherCar[] others) =>
            _monitor.Step(1, selfPos, selfVel, others, now, _bumps);

        /// <summary>A physics step that sees car 2 close by but not touching, so it misses the contact.</summary>
        private void StepJustMissing(long now) =>
            Step(Vec3.Zero, Vec3.Zero, now, new OtherCar(2, new Vec3(0f, 0f, 2.5f), Vec3.Zero));

        [Fact]
        public void TouchingAndApproachingBumps()
        {
            // Self drives at 10 m/s into a stationary car 1.8 m ahead (contact radius 1 m each).
            Step(Vec3.Zero, new Vec3(0f, 0f, 10f), 1000, new OtherCar(2, new Vec3(0f, 0f, 1.8f), Vec3.Zero));

            var bump = Assert.Single(_bumps);
            Assert.Equal(2, bump.OtherPlayerId);
            Assert.True(bump.SelfImpulse.Z < 0f, "pushed back");
            Assert.True(bump.SelfImpulse.Y > 0f, "with a small pop");
        }

        [Fact]
        public void TheFallbackCarriesTheOtherCarsEqualAndOppositeShare()
        {
            Step(Vec3.Zero, new Vec3(0f, 0f, 10f), 1000, new OtherCar(2, new Vec3(0.5f, 0f, 1.8f), new Vec3(1f, 0f, 0f)));

            var bump = Assert.Single(_bumps);
            var hit = bump.Fallback;
            Assert.Equal(1, hit.AttackerId);
            Assert.Equal(2, hit.TargetId);
            Assert.False(hit.IsItemHit);
            Assert.Equal(1000, hit.SentAtMs);
            Assert.True(hit.Impulse.Flattened.ApproximatelyEquals(-bump.SelfImpulse.Flattened, 1e-4f));
            Assert.Equal(bump.SelfImpulse.Y, hit.Impulse.Y, 4);
        }

        [Fact]
        public void CarsApartOrSeparatingDoNotBump()
        {
            Step(Vec3.Zero, new Vec3(0f, 0f, 10f), 1000, new OtherCar(2, new Vec3(0f, 0f, 2.5f), Vec3.Zero));
            Step(Vec3.Zero, new Vec3(0f, 0f, -10f), 1000, new OtherCar(2, new Vec3(0f, 0f, 1.5f), Vec3.Zero));
            Assert.Empty(_bumps);
        }

        [Fact]
        public void OneContactIsOneBump()
        {
            var other = new OtherCar(2, new Vec3(0f, 0f, 1.8f), Vec3.Zero);
            Step(Vec3.Zero, new Vec3(0f, 0f, 10f), 1000, other);
            Step(Vec3.Zero, new Vec3(0f, 0f, 10f), 1020, other); // next physics step, still overlapping
            Assert.Single(_bumps);

            Step(Vec3.Zero, new Vec3(0f, 0f, 10f), 1000 + HitDeduplicator.DefaultWindowMs, other);
            Assert.Equal(2, _bumps.Count);
        }

        [Fact]
        public void SeveralCarsCanBeHitAtOnce()
        {
            Step(Vec3.Zero, new Vec3(0f, 0f, 10f), 1000,
                new OtherCar(2, new Vec3(-0.6f, 0f, 1.7f), Vec3.Zero),
                new OtherCar(3, new Vec3(0.6f, 0f, 1.7f), Vec3.Zero));
            Assert.Equal(new byte[] { 2, 3 }, _bumps.Select(b => b.OtherPlayerId));
        }

        [Fact]
        public void AFallbackHitIsIgnoredIfThisPhoneAlreadyHandledTheContact()
        {
            Step(Vec3.Zero, new Vec3(0f, 0f, 10f), 1000, new OtherCar(2, new Vec3(0f, 0f, 1.8f), Vec3.Zero));
            var fromOther = new HitMessage { AttackerId = 2, TargetId = 1, Impulse = new Vec3(0f, 1f, -5f) };
            Assert.False(_monitor.OnHit(fromOther, 1, 1040, out _));
        }

        [Fact]
        public void AFallbackHitAppliesWhenThisPhoneMissedTheContact()
        {
            StepJustMissing(1020);
            Assert.Empty(_bumps);
            var fromOther = new HitMessage { AttackerId = 2, TargetId = 1, Impulse = new Vec3(0f, 1f, -5f) };
            Assert.True(_monitor.OnHit(fromOther, 1, 1040, out var impulse));
            Assert.Equal(fromOther.Impulse, impulse);

            // ...and then this phone's own late view of the same contact doesn't push again.
            Step(Vec3.Zero, new Vec3(0f, 0f, 10f), 1060, new OtherCar(2, new Vec3(0f, 0f, 1.8f), Vec3.Zero));
            Assert.Empty(_bumps);
        }

        [Fact]
        public void AFallbackHitIsClampedToWhatABumpCouldProduce()
        {
            var settings = new BumpSettings();
            float maxHorizontal = settings.MaxImpulsePerMass * BumpMonitor.ReducedMass;
            var forged = new HitMessage { AttackerId = 2, TargetId = 1, Impulse = new Vec3(3e4f, 1e4f, -4e4f) };

            StepJustMissing(1020);
            Assert.True(_monitor.OnHit(forged, 1, 1040, out var impulse));
            Assert.Equal(maxHorizontal, impulse.Flattened.Length, 3);
            Assert.True(impulse.Flattened.Normalized.ApproximatelyEquals(forged.Impulse.Flattened.Normalized));
            Assert.Equal(maxHorizontal * settings.LiftFraction, impulse.Y, 4);
        }

        [Fact]
        public void AnHonestFallbackHitIsNotClamped()
        {
            var self = new CarBody(Vec3.Zero, new Vec3(0f, 0f, 40f));
            var other = new CarBody(new Vec3(0f, 0f, 1.8f), new Vec3(0f, 0f, -40f));
            var honest = new HitMessage { AttackerId = 2, TargetId = 1, Impulse = BumpResolver.ComputeSelfImpulse(self, other, new BumpSettings()) };

            StepJustMissing(1020);
            Assert.True(_monitor.OnHit(honest, 1, 1040, out var impulse));
            Assert.True(impulse.ApproximatelyEquals(honest.Impulse));
        }

        [Fact]
        public void ANonFiniteHitIsDroppedWithoutUsingUpTheBump()
        {
            StepJustMissing(1020);
            var bad = new HitMessage { AttackerId = 2, TargetId = 1, Impulse = new Vec3(float.NaN, 0f, float.PositiveInfinity) };
            Assert.False(_monitor.OnHit(bad, 1, 1040, out _));

            var good = new HitMessage { AttackerId = 2, TargetId = 1, Impulse = new Vec3(0f, 1f, -5f) };
            Assert.True(_monitor.OnHit(good, 1, 1050, out _));
        }

        [Fact]
        public void AFallbackHitFromACarThisPhoneDoesNotSeeNearbyIsDropped()
        {
            var far = new OtherCar(2, new Vec3(0f, 0f, BumpMonitor.FallbackReach + 0.5f), Vec3.Zero);
            var forged = new HitMessage { AttackerId = 2, TargetId = 1, Impulse = new Vec3(0f, 1e4f, -4e4f) };

            var unseen = new HitMessage { AttackerId = 3, TargetId = 1, Impulse = forged.Impulse };

            Step(Vec3.Zero, Vec3.Zero, 1000, far);
            Assert.False(_monitor.OnHit(forged, 1, 1000, out _));
            Assert.False(_monitor.OnHit(unseen, 1, 1000, out _));

            // Once car 2 is close, its next hit applies: the dropped one didn't use up the bump...
            StepJustMissing(1020);
            Assert.True(_monitor.OnHit(forged, 1, 1020, out _));

            // ...and once it has driven off again, it can't bump from there.
            Step(Vec3.Zero, Vec3.Zero, 1020 + BumpMonitor.FallbackCooldownMs, far);
            Assert.False(_monitor.OnHit(forged, 1, 1020 + BumpMonitor.FallbackCooldownMs, out _));
        }

        [Fact]
        public void ACarTakesOneFallbackHitFromAnotherPerCooldown()
        {
            var fromTwo = new HitMessage { AttackerId = 2, TargetId = 1, Impulse = new Vec3(0f, 1f, -5f) };
            var fromThree = new HitMessage { AttackerId = 3, TargetId = 1, Impulse = new Vec3(0f, 1f, 5f) };
            var three = new OtherCar(3, new Vec3(0f, 0f, -2.5f), Vec3.Zero);

            // A hit this phone had already seen for itself doesn't start the cooldown.
            Step(Vec3.Zero, new Vec3(0f, 0f, 10f), 1000, new OtherCar(2, new Vec3(0f, 0f, 1.8f), Vec3.Zero));
            Assert.False(_monitor.OnHit(fromTwo, 1, 1040, out _));
            Step(Vec3.Zero, Vec3.Zero, 1200, new OtherCar(2, new Vec3(0f, 0f, 2.5f), Vec3.Zero), three);
            Assert.True(_monitor.OnHit(fromTwo, 1, 1200, out _));

            // Past the dedupe window but within the cooldown, car 2's next is dropped; car 3 has a cooldown of its own.
            Assert.False(_monitor.OnHit(fromTwo, 1, 1300, out _));
            Assert.True(_monitor.OnHit(fromThree, 1, 1300, out _));

            // The dropped one didn't open a dedupe window, so this phone's own view of that contact still applies.
            Step(Vec3.Zero, new Vec3(0f, 0f, 10f), 1320, new OtherCar(2, new Vec3(0f, 0f, 1.8f), Vec3.Zero));
            Assert.Equal(2, _bumps.Count);

            StepJustMissing(1200 + BumpMonitor.FallbackCooldownMs - 1);
            Assert.False(_monitor.OnHit(fromTwo, 1, 1200 + BumpMonitor.FallbackCooldownMs - 1, out _));
            Assert.True(_monitor.OnHit(fromTwo, 1, 1200 + BumpMonitor.FallbackCooldownMs, out _));
        }

        [Fact]
        public void AModifiedPhoneSendingItsHardestBumpOverAndOverCannotBuildUpSpeed()
        {
            // Car 2 keeps close and sends a bump straight ahead every dedupe window, at a car already at top speed with
            // the gas held (auto-accelerate), on the ground, where its handling can wear each push off.
            var tuning = new CarTuning();
            var state = new CarState { Velocity = new Vec3(0f, 0f, tuning.MaxSpeed) };
            var forged = new HitMessage { AttackerId = 2, TargetId = 1, Impulse = new Vec3(0f, 0f, 1e4f) };
            float hardest = new BumpSettings().MaxImpulsePerMass * BumpMonitor.ReducedMass / BumpMonitor.CarMass;
            const long stepMs = 20;
            float fastest = 0f;
            int taken = 0;
            for (long now = 1000; now < 21_000; now += stepMs)
            {
                StepJustMissing(now);
                if (now % HitDeduplicator.DefaultWindowMs == 0 && _monitor.OnHit(forged, 1, now, out var impulse))
                {
                    state.Velocity += impulse.Flattened / BumpMonitor.CarMass;
                    taken++;
                }

                state = CarModel.Step(state, new CarInput(0f, 1f), grounded: true, stepMs / 1000f, tuning);
                fastest = Math.Max(fastest, state.Velocity.Length);
            }

            Assert.True(taken > 1);
            Assert.True(fastest <= tuning.MaxSpeed + hardest + 1e-3f, $"reached {fastest} m/s");
        }

        [Fact]
        public void ItemHitsAlwaysApplyAndHitsForOthersNever()
        {
            Step(Vec3.Zero, new Vec3(0f, 0f, 10f), 1000, new OtherCar(0, new Vec3(0f, 0f, 1.8f), Vec3.Zero));
            Assert.True(_monitor.OnHit(new HitMessage { AttackerId = 0, TargetId = 1, IsItemHit = true }, 1, 1010, out _));
            Assert.False(_monitor.OnHit(new HitMessage { AttackerId = 0, TargetId = 3 }, 1, 5000, out _));
        }
    }

    public class HitRoutingTests
    {
        private readonly RecordingTransport _transport = new RecordingTransport();
        private readonly HostSession _host;

        public HitRoutingTests()
        {
            _host = new HostSession(_transport, "Host", 3, () => 0);
            _host.OnMessage(7, new JoinRequestMessage { ProtocolVersion = 3, PlayerName = "A" }); // player 1
            _host.OnMessage(9, new JoinRequestMessage { ProtocolVersion = 3, PlayerName = "B" }); // player 2
            _transport.Sent.Clear();
        }

        [Fact]
        public void AGuestsBumpOnAnotherGuestIsForwardedToThatGuestOnly()
        {
            var hit = new HitMessage { AttackerId = 1, TargetId = 2 };
            _host.OnMessage(7, hit);
            var sent = Assert.Single(_transport.Sent);
            Assert.Equal(9ul, sent.To);
            Assert.Same(hit, sent.Message);
            Assert.True(sent.Reliable);
        }

        [Fact]
        public void AGuestsBumpOnTheHostIsRaisedLocally()
        {
            HitMessage received = null;
            _host.HitReceived += h => received = h;
            _host.OnMessage(7, new HitMessage { AttackerId = 1, TargetId = 0 });
            Assert.NotNull(received);
            Assert.Empty(_transport.Sent);
        }

        [Fact]
        public void TheHostsOwnBumpGoesToTheTargetGuest()
        {
            _host.SendHit(new HitMessage { AttackerId = 0, TargetId = 1 });
            Assert.Equal(7ul, Assert.Single(_transport.Sent).To);
        }

        [Fact]
        public void GuestsCannotForgeHits()
        {
            _host.OnMessage(7, new HitMessage { AttackerId = 2, TargetId = 0 }); // pretending to be player 2
            _host.OnMessage(7, new HitMessage { AttackerId = 1, TargetId = 2, IsItemHit = true }); // items come from the host
            _host.OnMessage(42, new HitMessage { AttackerId = 1, TargetId = 2 }); // not joined
            Assert.Empty(_transport.Sent);
        }

        [Fact]
        public void AGuestRaisesOnlyHitsAddressedToIt()
        {
            var link = new RecordingTransport();
            var guest = new GuestSession(link, "A", 3, () => 0);
            guest.OnMessage(new JoinReplyMessage { Result = JoinResult.Accepted, PlayerId = 1 });
            var received = new List<byte>();
            guest.HitReceived += h => received.Add(h.TargetId);

            guest.OnMessage(new HitMessage { AttackerId = 2, TargetId = 1 });
            guest.OnMessage(new HitMessage { AttackerId = 2, TargetId = 3 });
            Assert.Equal(new byte[] { 1 }, received);
        }
    }
}
