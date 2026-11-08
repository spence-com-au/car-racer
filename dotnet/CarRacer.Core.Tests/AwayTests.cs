using System.Collections.Generic;
using CarRacer.Core.Net;
using CarRacer.Core.Numerics;
using CarRacer.Core.Session;
using Xunit;

namespace CarRacer.Core.Tests
{
    /// <summary>A guest whose app goes to the background mid-event (design doc §12): a bot drives after 10 s, and they get the car back on return.</summary>
    public class AwayTests
    {
        private const ushort Version = 17;
        private const ulong GuestConnection = 7;

        private sealed class HostLink : ISessionTransport
        {
            public GuestSession Guest;

            public void Send(ulong connection, IGameMessage message, bool reliable) => Guest?.OnMessage(message);

            public void Disconnect(ulong connection)
            {
            }
        }

        private sealed class GuestLink : ISessionTransport
        {
            public HostSession Host;

            public void Send(ulong connection, IGameMessage message, bool reliable) => Host.OnMessage(GuestConnection, message);

            public void Disconnect(ulong connection)
            {
            }
        }

        private long _now = 10_000;
        private readonly HostSession _host;
        private readonly GuestSession _guest;
        private readonly List<byte> _takenOver = new List<byte>();
        private readonly List<byte> _returned = new List<byte>();

        public AwayTests()
        {
            var hostLink = new HostLink();
            _host = new HostSession(hostLink, "Host", Version, () => _now);
            _guest = new GuestSession(new GuestLink { Host = _host }, "Guest", Version, () => _now);
            hostLink.Guest = _guest;
            _guest.OnConnected();
            _host.PlayerTakenOver += _takenOver.Add;
            _host.PlayerReturned += _returned.Add;
        }

        private byte GuestId => _guest.PlayerId;

        /// <summary>Anything from the guest counts as a sign of life; a car state is the usual one.</summary>
        private void GuestSpeaks() => _guest.SendCarState(new CarStateMessage { PlayerId = GuestId, SentAtMs = _now });

        private void Wait(long ms)
        {
            _now += ms;
            _host.Tick();
        }

        [Fact]
        public void AGuestSilentForTenSecondsMidEventIsTakenOver()
        {
            _host.EventInProgress = true;
            GuestSpeaks();
            Wait(HostSession.AwayAfterMs - 1);
            Assert.Empty(_takenOver);

            Wait(1);
            Assert.Equal(new[] { GuestId }, _takenOver);
            Assert.True(_host.IsBot(GuestId));
            Assert.True(_host.IsAway(GuestId));
            Assert.Contains(_guest.Roster.Players, p => p.PlayerId == GuestId && p.IsBot);

            Wait(HostSession.AwayAfterMs);
            Assert.Single(_takenOver);
        }

        [Fact]
        public void SilenceInTheLobbyIsFine()
        {
            Wait(HostSession.AwayAfterMs * 3);
            Assert.Empty(_takenOver);
            Assert.False(_host.IsBot(GuestId));
        }

        [Fact]
        public void AGuestWhoKeepsTalkingStaysInControl()
        {
            _host.EventInProgress = true;
            for (int i = 0; i < 5; i++)
            {
                Wait(HostSession.AwayAfterMs - 1000);
                GuestSpeaks();
            }

            Assert.Empty(_takenOver);
        }

        [Fact]
        public void AGuestWhoComesBackGetsTheirCarBack()
        {
            _host.EventInProgress = true;
            Wait(HostSession.AwayAfterMs);
            Assert.True(_host.IsAway(GuestId));

            GuestSpeaks();
            Assert.Equal(new[] { GuestId }, _returned);
            Assert.False(_host.IsBot(GuestId));
            Assert.False(_host.IsAway(GuestId));
            Assert.Contains(_guest.Roster.Players, p => p.PlayerId == GuestId && !p.IsBot);

            GuestSpeaks();
            Assert.Single(_returned);
        }

        [Fact]
        public void AGuestStillAwayWhenTheEventEndsIsBackInTheLobbyAsAPlayer()
        {
            _host.EventInProgress = true;
            Wait(HostSession.AwayAfterMs);

            _host.EventInProgress = false;
            int changes = 0;
            _host.RosterChanged += () => changes++;
            _host.RemoveBots();
            var player = _host.Roster.Find(GuestId);
            Assert.NotNull(player);
            Assert.False(player.IsBot);
            Assert.False(player.Ready);
            Assert.False(_host.IsAway(GuestId));
            Assert.Equal(1, changes);
        }

        [Fact]
        public void AnAwayGuestWhoDisconnectsLeavesTheBotDriving()
        {
            _host.EventInProgress = true;
            Wait(HostSession.AwayAfterMs);
            _host.OnDisconnected(GuestConnection);

            Assert.Single(_takenOver);
            Assert.True(_host.IsBot(GuestId));
            Assert.False(_host.IsAway(GuestId));
            Assert.Empty(_returned);
        }

        [Fact]
        public void TheReturningGuestIsToldWhereTheirCarGotTo()
        {
            ControlReturnedMessage got = null;
            _guest.ControlReturned += m => got = m;
            _host.SendControlReturned(new ControlReturnedMessage { PlayerId = GuestId, Position = new Vec3(1f, 2f, 3f), Velocity = new Vec3(0f, 0f, 9f), HeadingRadians = 1.5f, CheckpointsPassed = 7 });

            Assert.NotNull(got);
            Assert.Equal(new Vec3(1f, 2f, 3f), got.Position);
            Assert.Equal(7, got.CheckpointsPassed);

            var wire = Assert.IsType<ControlReturnedMessage>(MessageCodec.Decode(MessageCodec.Encode(got)));
            Assert.Equal(new Vec3(0f, 0f, 9f), wire.Velocity);
            Assert.Equal(1.5f, wire.HeadingRadians);
            Assert.Equal(7, wire.CheckpointsPassed);
        }
    }
}
