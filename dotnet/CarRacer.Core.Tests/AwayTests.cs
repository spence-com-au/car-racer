using System.Collections.Generic;
using System.Linq;
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

        private long _now = 10_000;
        private readonly HostLink _hostLink = new HostLink();
        private readonly HostSession _host;
        private readonly GuestSession _guest;
        private readonly List<byte> _returned = new List<byte>();

        public AwayTests()
        {
            _host = new HostSession(_hostLink, "Host", Version, () => _now);
            _guest = _hostLink.Join(_host, "Guest", Version, () => _now);
            _host.PlayerReturned += _returned.Add;
        }

        private byte GuestId => _guest.PlayerId;

        private int RostersSent => _hostLink.Sent.OfType<RosterMessage>().Count();

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
            Assert.False(_host.IsBot(GuestId));

            Wait(1);
            Assert.True(_host.IsBot(GuestId));
            Assert.Contains(_guest.Roster.Players, p => p.PlayerId == GuestId && p.IsBot);

            int rosters = RostersSent;
            Wait(HostSession.AwayAfterMs);
            Assert.Equal(rosters, RostersSent);
        }

        [Fact]
        public void SilenceInTheLobbyIsFine()
        {
            Wait(HostSession.AwayAfterMs * 3);
            Assert.False(_host.IsBot(GuestId));
        }

        [Fact]
        public void AGuestWhoKeepsTalkingStaysInControl()
        {
            _host.EventInProgress = true;
            for (int i = 0; i < 5; i++)
            {
                Wait(HostSession.AwayAfterMs - 1000);
                Assert.False(_host.IsBot(GuestId));
                GuestSpeaks();
            }
        }

        [Fact]
        public void AGuestWhoComesBackGetsTheirCarBack()
        {
            _host.EventInProgress = true;
            Wait(HostSession.AwayAfterMs);
            Assert.True(_host.IsBot(GuestId));

            GuestSpeaks();
            Assert.Equal(new[] { GuestId }, _returned);
            Assert.False(_host.IsBot(GuestId));
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
            int rosters = RostersSent;
            _host.RemoveBots();
            var player = _host.Roster.Find(GuestId);
            Assert.NotNull(player);
            Assert.False(player.IsBot);
            Assert.False(player.Ready);
            Assert.Equal(rosters + 1, RostersSent);

            // No longer away, so hearing from them hands nothing back.
            GuestSpeaks();
            Assert.Empty(_returned);
        }

        [Fact]
        public void AnAwayGuestWhoDisconnectsLeavesTheBotDriving()
        {
            _host.EventInProgress = true;
            Wait(HostSession.AwayAfterMs);
            _host.OnDisconnected(GuestLink.Connection);

            Assert.True(_host.IsBot(GuestId));
            Assert.Empty(_returned);

            // Gone for good rather than away, so the bot leaves with the others when the lobby reopens.
            _host.EventInProgress = false;
            _host.RemoveBots();
            Assert.Null(_host.Roster.Find(GuestId));
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
