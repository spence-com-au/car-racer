using System.Linq;
using CarRacer.Core.Net;
using CarRacer.Core.Session;
using Xunit;

namespace CarRacer.Core.Tests
{
    /// <summary>Guests voting for the next track (design doc §11).</summary>
    public class TrackVoteTests
    {
        private const ushort Version = 17;
        private static readonly string[] Names = { "Oval", "Canyon", "Eight", "Sweepers", "Hill" };

        [Fact]
        public void GuestsVoteButTheHostAndBotsDoNot()
        {
            var roster = new LobbyRoster();
            var host = roster.Add("Host", isHost: true);
            var alice = roster.Add("Alice");
            var bot = roster.AddBot();

            Assert.True(roster.Vote(alice.PlayerId, 2));
            Assert.False(roster.Vote(alice.PlayerId, 2));
            Assert.False(roster.Vote(host.PlayerId, 1));
            Assert.False(roster.Vote(bot.PlayerId, 1));
            Assert.Equal(VoteTrackMessage.NoVote, host.TrackVote);

            Assert.True(roster.ClearVotes());
            Assert.Equal(VoteTrackMessage.NoVote, alice.TrackVote);
            Assert.False(roster.ClearVotes());
        }

        [Fact]
        public void VotesAreCountedAndStrayOnesIgnored()
        {
            var counts = TrackVotes.Count(new[] { Entry(1), Entry(1), Entry(3), Entry(VoteTrackMessage.NoVote), Entry(9) }, 5);
            Assert.Equal(new[] { 0, 2, 0, 1, 0 }, counts);
            Assert.Equal("Canyon 2 · Sweepers 1", TrackVotes.Summary(counts, Names));
            Assert.Equal(string.Empty, TrackVotes.Summary(new int[5], Names));
        }

        [Theory]
        [InlineData(new[] { 0, 2, 0, 1, 0 }, 0, 1)] // the most votes wins
        [InlineData(new[] { 0, 0, 0, 0, 0 }, 3, 3)] // nobody voted: the host's pick
        [InlineData(new[] { 1, 0, 1, 0, 0 }, 2, 2)] // the host's pick is tied for the most
        [InlineData(new[] { 0, 1, 0, 1, 0 }, 0, 1)] // other ties: the first in the catalog
        public void TheLeaderNeverOverridesTheHostOnATie(int[] counts, int current, int leader)
        {
            Assert.Equal(leader, TrackVotes.Leader(counts, current));
        }

        [Fact]
        public void AVoteReachesEveryoneAndIsClearedAfterTheEvent()
        {
            var toGuest = new HostLink();
            var host = new HostSession(toGuest, "Host", Version, () => 0);
            var guest = toGuest.Join(host, "Guest", Version, () => 0);

            guest.VoteTrack(4);
            Assert.Equal(4, guest.Roster.Players.Single(p => p.PlayerId == guest.PlayerId).TrackVote);
            Assert.Equal(new[] { 0, 0, 0, 0, 1 }, TrackVotes.Count(guest.Roster.Players, 5));

            host.EventInProgress = true;
            guest.VoteTrack(1);
            Assert.Equal(4, host.Roster.Find(guest.PlayerId).TrackVote);

            host.EventInProgress = false;
            host.ResetReady();
            Assert.Equal(VoteTrackMessage.NoVote, guest.Roster.Players.Single(p => p.PlayerId == guest.PlayerId).TrackVote);
        }

        [Fact]
        public void TheVoteTravelsInTheMessageAndTheRoster()
        {
            var vote = Assert.IsType<VoteTrackMessage>(MessageCodec.Decode(MessageCodec.Encode(new VoteTrackMessage { TrackIndex = 3 })));
            Assert.Equal(3, vote.TrackIndex);

            var roster = new LobbyRoster();
            roster.Add("Host", isHost: true);
            var alice = roster.Add("Alice");
            roster.Vote(alice.PlayerId, 2);
            var decoded = Assert.IsType<RosterMessage>(MessageCodec.Decode(MessageCodec.Encode(roster.ToMessage())));
            Assert.Equal(VoteTrackMessage.NoVote, decoded.Players[0].TrackVote);
            Assert.Equal(2, decoded.Players[1].TrackVote);
        }

        private static RosterEntry Entry(byte vote) => new RosterEntry { TrackVote = vote };
    }
}
