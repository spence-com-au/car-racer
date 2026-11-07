using System.Collections.Generic;
using System.Linq;
using CarRacer.Core.Net;
using CarRacer.Core.Session;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class SessionFlowTests
    {
        private const ushort Version = 2;

        private long _now = 1000;
        private readonly RecordingTransport _hostSide = new RecordingTransport();
        private readonly HostSession _host;

        public SessionFlowTests()
        {
            _host = new HostSession(_hostSide, "Host", Version, () => _now);
        }

        private byte Join(ulong connection, string name)
        {
            _host.OnMessage(connection, new JoinRequestMessage { ProtocolVersion = Version, PlayerName = name });
            var reply = _hostSide.To<JoinReplyMessage>(connection).Last();
            Assert.Equal(JoinResult.Accepted, reply.Result);
            return reply.PlayerId;
        }

        [Fact]
        public void TheHostIsPlayerZero()
        {
            Assert.Equal(0, _host.HostPlayerId);
            Assert.Single(_host.Roster.Players);
            Assert.True(_host.Roster.Players[0].IsHost);
        }

        [Fact]
        public void AcceptedGuestsGetAnIdAndEveryoneGetsTheRoster()
        {
            byte a = Join(7, "Alice");
            byte b = Join(9, "Bob");
            Assert.Equal(1, a);
            Assert.Equal(2, b);

            var aliceRoster = _hostSide.To<RosterMessage>(7).Last();
            Assert.Equal(new[] { "Host", "Alice", "Bob" }, aliceRoster.Players.Select(p => p.Name));
            Assert.True(_hostSide.Sent.Where(s => s.Message is RosterMessage || s.Message is JoinReplyMessage).All(s => s.Reliable));
        }

        [Fact]
        public void RefusedGuestsAreToldWhyAndDisconnected()
        {
            _host.OnMessage(3, new JoinRequestMessage { ProtocolVersion = Version - 1, PlayerName = "Old" });
            Assert.Equal(JoinResult.VersionMismatch, _hostSide.To<JoinReplyMessage>(3).Single().Result);
            Assert.Equal(new ulong[] { 3 }, _hostSide.Disconnected);

            _host.EventInProgress = true;
            _host.OnMessage(4, new JoinRequestMessage { ProtocolVersion = Version, PlayerName = "Late" });
            Assert.Equal(JoinResult.EventInProgress, _hostSide.To<JoinReplyMessage>(4).Single().Result);
            Assert.Single(_host.Roster.Players);
        }

        [Fact]
        public void TheSessionFillsAtSix()
        {
            for (ulong c = 1; c <= 5; c++)
            {
                Join(c, "P" + c);
            }

            _host.OnMessage(6, new JoinRequestMessage { ProtocolVersion = Version, PlayerName = "Seventh" });
            Assert.Equal(JoinResult.SessionFull, _hostSide.To<JoinReplyMessage>(6).Single().Result);
        }

        [Fact]
        public void LeavingFreesTheSlotAndUpdatesTheRoster()
        {
            Join(7, "Alice");
            Join(9, "Bob");
            int changes = 0;
            _host.RosterChanged += () => changes++;

            _host.OnDisconnected(7);
            Assert.Equal(1, changes);
            Assert.Equal(new[] { "Host", "Bob" }, _hostSide.To<RosterMessage>(9).Last().Players.Select(p => p.Name));
            Assert.Equal(1, Join(11, "Carol"));

            _host.OnDisconnected(42); // never joined: nothing happens
            Assert.Equal(2, changes);
        }

        [Fact]
        public void ReadyChangesAreBroadcast()
        {
            byte a = Join(7, "Alice");
            _host.OnMessage(7, new SetReadyMessage { Ready = true });
            Assert.True(_host.Roster.Find(a).Ready);
            Assert.True(_hostSide.To<RosterMessage>(7).Last().Players.Single(p => p.PlayerId == a).Ready);
            Assert.True(_host.Roster.EveryoneReady);
        }

        [Fact]
        public void TheRosterMessageIsKeptUntilTheRosterChanges()
        {
            byte a = Join(7, "Alice");
            var before = _host.RosterMessage();
            Assert.False(_host.CanStart);
            Assert.Same(before, _host.RosterMessage());

            // The guests get the new roster, not the one kept from before the change.
            _host.OnMessage(7, new SetReadyMessage { Ready = true });
            var after = _host.RosterMessage();
            Assert.NotSame(before, after);
            Assert.Same(after, _hostSide.To<RosterMessage>(7).Last());
            Assert.True(after.Players.Single(p => p.PlayerId == a).Ready);
            Assert.True(_host.CanStart);
        }

        [Fact]
        public void MessagesBeforeJoiningAreIgnored()
        {
            _host.OnMessage(7, new SetReadyMessage { Ready = true });
            _host.OnMessage(7, new CarStateMessage { PlayerId = 1 });
            Assert.Empty(_hostSide.Sent);
        }

        [Fact]
        public void ASecondJoinRequestIsIgnored()
        {
            Join(7, "Alice");
            int sent = _hostSide.Sent.Count;
            _host.OnMessage(7, new JoinRequestMessage { ProtocolVersion = Version, PlayerName = "Again" });
            Assert.Equal(sent, _hostSide.Sent.Count);
            Assert.Equal(2, _host.Roster.Count);
        }

        [Fact]
        public void PingsAreAnsweredWithTheHostClock()
        {
            _now = 5555;
            _host.OnMessage(7, new ClockPingMessage { GuestSentMs = 123 });
            var pong = _hostSide.To<ClockPongMessage>(7).Single();
            Assert.Equal(123, pong.GuestSentMs);
            Assert.Equal(5555, pong.HostMs);
        }

        [Fact]
        public void CarStatesAreRelayedToTheOtherGuestsOnly()
        {
            byte a = Join(7, "Alice");
            Join(9, "Bob");
            Join(11, "Carol");
            CarStateMessage received = null;
            _host.CarStateReceived += s => received = s;

            var state = new CarStateMessage { PlayerId = a, Sequence = 5 };
            _host.OnMessage(7, state);
            Assert.Same(state, received);
            Assert.Empty(_hostSide.To<CarStateMessage>(7));
            Assert.Single(_hostSide.To<CarStateMessage>(9));
            Assert.Single(_hostSide.To<CarStateMessage>(11));
            Assert.False(_hostSide.Sent.Single(s => s.To == 9 && s.Message is CarStateMessage).Reliable);
        }

        [Fact]
        public void AGuestCannotSendAnotherPlayersCar()
        {
            Join(7, "Alice");
            Join(9, "Bob");
            bool received = false;
            _host.CarStateReceived += _ => received = true;

            _host.OnMessage(7, new CarStateMessage { PlayerId = 0 }); // pretending to be the host
            Assert.False(received);
            Assert.Empty(_hostSide.To<CarStateMessage>(9));
        }

        [Fact]
        public void TheHostsOwnCarGoesToEveryGuest()
        {
            Join(7, "Alice");
            Join(9, "Bob");
            _host.BroadcastCarState(new CarStateMessage { PlayerId = 0 });
            Assert.Single(_hostSide.To<CarStateMessage>(7));
            Assert.Single(_hostSide.To<CarStateMessage>(9));
        }

        // --- Guest side, wired to the host through two fake transports ---

        private sealed class Link : ISessionTransport
        {
            public HostSession Host;
            public ulong Connection;
            public readonly List<IGameMessage> ToHost = new List<IGameMessage>();

            public void Send(ulong connection, IGameMessage message, bool reliable)
            {
                Assert.Equal(GuestSession.HostConnection, connection);
                ToHost.Add(message);
                Host.OnMessage(Connection, message);
            }

            public void Disconnect(ulong connection)
            {
            }
        }

        private GuestSession Guest(ulong connection, string name, out Link link)
        {
            link = new Link { Host = _host, Connection = connection };
            var guest = new GuestSession(link, name, Version, () => _now);
            guest.OnConnected();
            foreach (var message in _hostSide.To<IGameMessage>(connection))
            {
                guest.OnMessage(message);
            }

            return guest;
        }

        [Fact]
        public void AGuestJoinsAndLearnsTheRoster()
        {
            var guest = Guest(7, "  Alice  ", out var link);
            Assert.Equal("Alice", Assert.IsType<JoinRequestMessage>(link.ToHost[0]).PlayerName);
            Assert.Equal(GuestState.Joined, guest.State);
            Assert.Equal(1, guest.PlayerId);
            Assert.Equal(2, guest.Roster.Players.Count);
        }

        [Fact]
        public void ARefusedGuestKnowsWhy()
        {
            _host.EventInProgress = true;
            var guest = Guest(7, "Alice", out _);
            Assert.Equal(GuestState.Refused, guest.State);
            Assert.Equal(JoinResult.EventInProgress, guest.RefusalReason);
        }

        [Fact]
        public void TheGuestPingsQuicklyThenOnceASecond()
        {
            var guest = Guest(7, "Alice", out var link);
            var pingTimes = new List<long>();
            for (int i = 0; i < 300; i++)
            {
                int before = link.ToHost.Count;
                guest.Tick();
                if (link.ToHost.Count > before)
                {
                    pingTimes.Add(_now);
                }

                _now += 10;
            }

            Assert.Equal(new long[] { 1000, 1200, 1400, 1600, 1800, 2800, 3800 }, pingTimes);
        }

        [Fact]
        public void PongsUpdateTheClockEstimate()
        {
            var guest = Guest(7, "Alice", out _);
            _now = 2000;
            guest.OnMessage(new ClockPongMessage { GuestSentMs = 1960, HostMs = 9000 });
            Assert.True(guest.Clock.HasEstimate);
            Assert.Equal(9000 - 1980, guest.Clock.OffsetMs);
        }

        [Fact]
        public void TheGuestIgnoresEchoesOfItsOwnCar()
        {
            var guest = Guest(7, "Alice", out _);
            var seen = new List<byte>();
            guest.CarStateReceived += s => seen.Add(s.PlayerId);
            guest.OnMessage(new CarStateMessage { PlayerId = guest.PlayerId });
            guest.OnMessage(new CarStateMessage { PlayerId = 0 });
            Assert.Equal(new byte[] { 0 }, seen);
        }

        [Fact]
        public void NothingIsSentBeforeJoining()
        {
            var link = new Link { Host = _host, Connection = 7 };
            var guest = new GuestSession(link, "Alice", Version, () => _now);
            guest.SetReady(true);
            guest.SendCarState(new CarStateMessage());
            guest.Tick();
            Assert.Empty(link.ToHost);
        }

        [Fact]
        public void ReadyReachesTheHost()
        {
            var guest = Guest(7, "Alice", out _);
            guest.SetReady(true);
            Assert.True(_host.Roster.Find(guest.PlayerId).Ready);
        }
    }
}
