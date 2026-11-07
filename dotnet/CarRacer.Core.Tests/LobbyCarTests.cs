using System.Linq;
using CarRacer.Core.Net;
using CarRacer.Core.Session;
using Xunit;

namespace CarRacer.Core.Tests
{
    /// <summary>Changing car in the lobby (design doc §11).</summary>
    public class LobbyCarTests
    {
        private const ushort Version = 17;

        [Fact]
        public void APlayerCanChangeBodyAndToAFreeColour()
        {
            var roster = new LobbyRoster();
            var host = roster.Add("Host", isHost: true, new CarChoice(0, 0));
            var alice = roster.Add("Alice", car: new CarChoice(1, 1));

            Assert.True(roster.ChangeCar(alice.PlayerId, new CarChoice(4, 5)));
            Assert.Equal(4, alice.BodyIndex);
            Assert.Equal(5, alice.ColourIndex);
            Assert.False(roster.ChangeCar(alice.PlayerId, new CarChoice(4, 5)));
            Assert.True(roster.ChangeCar(host.PlayerId, new CarChoice(2, 0)));
            Assert.Equal(2, host.BodyIndex);
        }

        [Fact]
        public void SomeoneElsesColourIsNotTakenButTheBodyStillChanges()
        {
            var roster = new LobbyRoster();
            roster.Add("Host", isHost: true, new CarChoice(0, 0));
            var alice = roster.Add("Alice", car: new CarChoice(1, 1));

            Assert.True(roster.ChangeCar(alice.PlayerId, new CarChoice(3, 0)));
            Assert.Equal(3, alice.BodyIndex);
            Assert.Equal(1, alice.ColourIndex);
            Assert.False(roster.ChangeCar(alice.PlayerId, new CarChoice(3, 0)));
        }

        [Fact]
        public void StrayIndicesWrapAndBotsKeepTheirCars()
        {
            var roster = new LobbyRoster();
            var host = roster.Add("Host", isHost: true);
            var bot = roster.AddBot();
            Assert.False(roster.ChangeCar(bot.PlayerId, new CarChoice(5, 7)));
            Assert.True(roster.ChangeCar(host.PlayerId, new CarChoice(LobbyRoster.BodyCount + 2, LobbyRoster.ColourCount + 6)));
            Assert.Equal(2, host.BodyIndex);
            Assert.Equal(6, host.ColourIndex);
            Assert.False(roster.ChangeCar(99, new CarChoice(1, 1)));
        }

        [Fact]
        public void TheMessageRoundTrips()
        {
            var change = Assert.IsType<ChangeCarMessage>(MessageCodec.Decode(MessageCodec.Encode(new ChangeCarMessage { BodyIndex = 3, ColourIndex = 6 })));
            Assert.Equal(3, change.BodyIndex);
            Assert.Equal(6, change.ColourIndex);
        }

        [Fact]
        public void AGuestsChangeReachesEveryoneInTheLobbyButNotMidEvent()
        {
            var (host, guest) = Connect();
            guest.ChangeCar(new CarChoice(5, 4));
            var mine = guest.Roster.Players.Single(p => p.PlayerId == guest.PlayerId);
            Assert.Equal(5, mine.BodyIndex);
            Assert.Equal(4, mine.ColourIndex);

            host.EventInProgress = true;
            guest.ChangeCar(new CarChoice(2, 6));
            Assert.Equal(5, host.Roster.Find(guest.PlayerId).BodyIndex);
            Assert.False(host.ChangeHostCar(new CarChoice(2, 6)));

            host.EventInProgress = false;
            Assert.True(host.ChangeHostCar(new CarChoice(2, 6)));
            Assert.Equal(2, guest.Roster.Players.Single(p => p.IsHost).BodyIndex);
        }

        [Fact]
        public void AGuestAsksForItsLatestPickWhenItConnects()
        {
            var link = new RecordingLink();
            var guest = new GuestSession(link, "Guest", Version, () => 0, new CarChoice(0, 0));
            guest.ChangeCar(new CarChoice(3, 2));
            guest.OnConnected();
            var request = Assert.IsType<JoinRequestMessage>(link.Last);
            Assert.Equal(3, request.BodyIndex);
            Assert.Equal(2, request.ColourIndex);
        }

        /// <summary>A host and a joined guest wired straight to each other.</summary>
        private static (HostSession Host, GuestSession Guest) Connect()
        {
            var toGuest = new Loopback();
            var host = new HostSession(toGuest, "Host", Version, () => 0, new CarChoice(0, 0));
            var toHost = new Loopback { Host = host };
            var guest = new GuestSession(toHost, "Guest", Version, () => 0, new CarChoice(1, 1));
            toGuest.Guest = guest;
            guest.OnConnected();
            Assert.Equal(GuestState.Joined, guest.State);
            return (host, guest);
        }

        private sealed class Loopback : ISessionTransport
        {
            public HostSession Host;
            public GuestSession Guest;

            public void Send(ulong connection, IGameMessage message, bool reliable)
            {
                if (Host != null)
                {
                    Host.OnMessage(7, message);
                }
                else
                {
                    Guest?.OnMessage(message);
                }
            }

            public void Disconnect(ulong connection)
            {
            }
        }

        private sealed class RecordingLink : ISessionTransport
        {
            public IGameMessage Last;

            public void Send(ulong connection, IGameMessage message, bool reliable) => Last = message;

            public void Disconnect(ulong connection)
            {
            }
        }
    }
}
