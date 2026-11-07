using System.Linq;
using CarRacer.Core.Net;
using CarRacer.Core.Session;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class GarageTests
    {
        [Fact]
        public void PlayersGetTheColourTheyPicked()
        {
            var roster = new LobbyRoster();
            Assert.Equal(5, roster.Add("Host", isHost: true, new CarChoice(2, 5)).ColourIndex);
            Assert.Equal(3, roster.Add("Alice", car: new CarChoice(0, 3)).ColourIndex);
        }

        [Fact]
        public void TheLaterJoinerGetsTheNextFreeColourAlong()
        {
            var roster = new LobbyRoster();
            roster.Add("Host", isHost: true, new CarChoice(0, 6));
            roster.Add("Alice", car: new CarChoice(0, 7));
            Assert.Equal(6, roster.Players[0].ColourIndex);

            // 6 and 7 are taken, so Bob's 6 wraps round to 0.
            Assert.Equal(0, roster.Add("Bob", car: new CarChoice(0, 6)).ColourIndex);
            Assert.Equal(roster.Count, roster.Players.Select(p => p.ColourIndex).Distinct().Count());
        }

        [Fact]
        public void BodiesAreKeptAndStrayOnesWrap()
        {
            var roster = new LobbyRoster();
            Assert.Equal(4, roster.Add("Host", isHost: true, new CarChoice(4, 0)).BodyIndex);
            Assert.Equal(1, roster.Add("Alice", car: new CarChoice(LobbyRoster.BodyCount + 1, 0)).BodyIndex);
        }

        [Fact]
        public void BotsTakeTurnsWithTheBodies()
        {
            var roster = new LobbyRoster();
            roster.Add("Host", isHost: true);
            var first = roster.AddBot();
            var second = roster.AddBot();
            Assert.NotEqual(first.BodyIndex, second.BodyIndex);
            Assert.NotEqual(first.ColourIndex, second.ColourIndex);
        }

        [Fact]
        public void TheChoiceTravelsInTheJoinRequestAndTheRoster()
        {
            var join = Assert.IsType<JoinRequestMessage>(MessageCodec.Decode(MessageCodec.Encode(
                new JoinRequestMessage { ProtocolVersion = 17, PlayerName = "Alice", BodyIndex = 3, ColourIndex = 5 })));
            Assert.Equal(3, join.BodyIndex);
            Assert.Equal(5, join.ColourIndex);

            var roster = new LobbyRoster();
            roster.Add("Host", isHost: true, new CarChoice(5, 1));
            var decoded = Assert.IsType<RosterMessage>(MessageCodec.Decode(MessageCodec.Encode(roster.ToMessage())));
            Assert.Equal(5, decoded.Players[0].BodyIndex);
            Assert.Equal(1, decoded.Players[0].ColourIndex);
        }

        [Fact]
        public void AnOlderGuestsJoinRequestStillReadsSoItCanBeTurnedAway()
        {
            // An older version's request ends after the name; the host must still read the version.
            byte[] full = MessageCodec.Encode(new JoinRequestMessage { ProtocolVersion = 13, PlayerName = "Old", BodyIndex = 9, ColourIndex = 9 });
            byte[] old = full.Take(full.Length - 2).ToArray();
            var join = Assert.IsType<JoinRequestMessage>(MessageCodec.Decode(old));
            Assert.Equal(13, join.ProtocolVersion);
            Assert.Equal(0, join.BodyIndex);
        }

        [Fact]
        public void ANewerPeersHandshakeStillReadsSoItCanBeTurnedAway()
        {
            // A newer version may append fields; the handshake must still read rather than be dropped as malformed.
            byte[] request = MessageCodec.Encode(new JoinRequestMessage { ProtocolVersion = 99, PlayerName = "New" });
            var join = Assert.IsType<JoinRequestMessage>(MessageCodec.Decode(request.Concat(new byte[] { 1, 2, 3 }).ToArray()));
            Assert.Equal(99, join.ProtocolVersion);

            byte[] reply = MessageCodec.Encode(new JoinReplyMessage { Result = JoinResult.VersionMismatch });
            var refused = Assert.IsType<JoinReplyMessage>(MessageCodec.Decode(reply.Concat(new byte[] { 1, 2, 3 }).ToArray()));
            Assert.Equal(JoinResult.VersionMismatch, refused.Result);
        }

        [Fact]
        public void AGuestsChoiceReachesTheHostsRoster()
        {
            var transport = new RecordingTransport();
            var host = new HostSession(transport, "Host", 17, () => 0, new CarChoice(1, 2));
            var link = new GuestLink { Host = host };
            var guest = new GuestSession(link, "Guest", 17, () => 0, new CarChoice(4, 2));
            guest.OnConnected();

            var player = host.Roster.Players.Single(p => !p.IsHost);
            Assert.Equal(4, player.BodyIndex);
            Assert.Equal(3, player.ColourIndex); // the host already has 2
            Assert.Equal(1, host.Roster.Players.Single(p => p.IsHost).BodyIndex);
        }

        [Fact]
        public void AFullGridOfBotsDrivesDifferentBodiesFromAcrossTheGarage()
        {
            var roster = new LobbyRoster();
            while (roster.AddBot() != null)
            {
            }

            var bodies = roster.Players.Select(p => (int)p.BodyIndex).ToList();
            Assert.Equal(bodies.Count, bodies.Distinct().Count());
            Assert.Contains(bodies, b => b >= 6);
            Assert.Contains(bodies, b => b < 6);
        }
    }
}
