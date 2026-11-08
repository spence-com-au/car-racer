using System.Collections.Generic;
using System.Linq;
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
            float maxHorizontal = settings.MaxImpulsePerMass * BumpMonitor.CarMass / 2f;
            var forged = new HitMessage { AttackerId = 2, TargetId = 1, Impulse = new Vec3(3e4f, 1e4f, -4e4f) };

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

            Assert.True(_monitor.OnHit(honest, 1, 1040, out var impulse));
            Assert.True(impulse.ApproximatelyEquals(honest.Impulse));
        }

        [Fact]
        public void ANonFiniteHitIsDroppedWithoutUsingUpTheBump()
        {
            var bad = new HitMessage { AttackerId = 2, TargetId = 1, Impulse = new Vec3(float.NaN, 0f, float.PositiveInfinity) };
            Assert.False(_monitor.OnHit(bad, 1, 1040, out _));

            var good = new HitMessage { AttackerId = 2, TargetId = 1, Impulse = new Vec3(0f, 1f, -5f) };
            Assert.True(_monitor.OnHit(good, 1, 1050, out _));
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
        private sealed class Transport : ISessionTransport
        {
            public readonly List<(ulong To, IGameMessage Message, bool Reliable)> Sent = new List<(ulong, IGameMessage, bool)>();

            public void Send(ulong connection, IGameMessage message, bool reliable) => Sent.Add((connection, message, reliable));

            public void Disconnect(ulong connection)
            {
            }
        }

        private readonly Transport _transport = new Transport();
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
            var link = new Transport();
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
