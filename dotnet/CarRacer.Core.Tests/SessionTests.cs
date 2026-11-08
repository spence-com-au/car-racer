using CarRacer.Core.Net;
using CarRacer.Core.Session;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class SessionMessageTests
    {
        [Fact]
        public void JoinMessagesRoundTrip()
        {
            var request = Assert.IsType<JoinRequestMessage>(MessageCodec.Decode(MessageCodec.Encode(new JoinRequestMessage
            {
                ProtocolVersion = 2,
                PlayerName = "Zoë's Pixel",
            })));
            Assert.Equal(2, request.ProtocolVersion);
            Assert.Equal("Zoë's Pixel", request.PlayerName);

            var reply = Assert.IsType<JoinReplyMessage>(MessageCodec.Decode(MessageCodec.Encode(new JoinReplyMessage
            {
                Result = JoinResult.SessionFull,
                PlayerId = 4,
            })));
            Assert.Equal(JoinResult.SessionFull, reply.Result);
            Assert.Equal(4, reply.PlayerId);
        }

        [Fact]
        public void RosterRoundTrips()
        {
            var roster = new LobbyRoster();
            roster.Add("Host", isHost: true);
            roster.Add("Guest");
            roster.SetReady(1, true);

            var decoded = Assert.IsType<RosterMessage>(MessageCodec.Decode(MessageCodec.Encode(roster.ToMessage())));
            Assert.Equal(2, decoded.Players.Count);
            Assert.Equal("Host", decoded.Players[0].Name);
            Assert.True(decoded.Players[0].IsHost);
            Assert.Equal(1, decoded.Players[1].PlayerId);
            Assert.Equal(1, decoded.Players[1].ColourIndex);
            Assert.True(decoded.Players[1].Ready);
            Assert.False(decoded.Players[1].IsHost);
        }

        [Fact]
        public void EmptyRosterRoundTrips()
        {
            var decoded = Assert.IsType<RosterMessage>(MessageCodec.Decode(MessageCodec.Encode(new RosterMessage())));
            Assert.Empty(decoded.Players);
        }

        [Fact]
        public void ClockAndReadyMessagesRoundTrip()
        {
            var ping = Assert.IsType<ClockPingMessage>(MessageCodec.Decode(MessageCodec.Encode(new ClockPingMessage { GuestSentMs = 123456789012 })));
            Assert.Equal(123456789012, ping.GuestSentMs);

            var pong = Assert.IsType<ClockPongMessage>(MessageCodec.Decode(MessageCodec.Encode(new ClockPongMessage { GuestSentMs = 5, HostMs = -7 })));
            Assert.Equal(5, pong.GuestSentMs);
            Assert.Equal(-7, pong.HostMs);

            Assert.True(Assert.IsType<SetReadyMessage>(MessageCodec.Decode(MessageCodec.Encode(new SetReadyMessage { Ready = true }))).Ready);
        }
    }

    public class ClockSyncTests
    {
        [Fact]
        public void NoEstimateUntilTheFirstSample()
        {
            var sync = new ClockSync();
            Assert.False(sync.HasEstimate);
            Assert.Equal(100, sync.ToHostTime(100));
        }

        [Fact]
        public void SymmetricRoundTripGivesTheExactOffset()
        {
            // Host clock runs 5000 ms ahead; 40 ms each way.
            var sync = new ClockSync();
            Assert.True(sync.AddSample(localSentMs: 1000, hostMs: 1000 + 40 + 5000, localReceivedMs: 1080));
            Assert.Equal(5000, sync.OffsetMs);
            Assert.Equal(80, sync.RoundTripMs);
            Assert.Equal(40f, sync.OneWayLatencyMs);
            Assert.Equal(7000, sync.ToHostTime(2000));
            Assert.Equal(2000, sync.ToLocalTime(7000));
        }

        [Fact]
        public void TheFastestRecentSampleWins()
        {
            var sync = new ClockSync();
            sync.AddSample(0, 5040, 80); // good: offset 5000
            sync.AddSample(100, 5300, 400); // queued on the way back: 300 ms round trip, offset skewed
            Assert.Equal(5000, sync.OffsetMs);
            Assert.Equal(80, sync.RoundTripMs);
        }

        [Fact]
        public void OldSamplesAgeOut()
        {
            var sync = new ClockSync(window: 2);
            sync.AddSample(0, 5010, 20); // fastest, but will fall out of the window
            sync.AddSample(100, 6150, 200);
            sync.AddSample(300, 6350, 400);
            Assert.Equal(6000, sync.OffsetMs);
        }

        [Fact]
        public void ImpossibleSamplesAreIgnored()
        {
            var sync = new ClockSync();
            Assert.False(sync.AddSample(localSentMs: 500, hostMs: 0, localReceivedMs: 400));
            Assert.False(sync.HasEstimate);
        }
    }

    public class LobbyRosterTests
    {
        [Fact]
        public void PlayersGetTheLowestFreeIdAndColour()
        {
            var roster = new LobbyRoster();
            Assert.Equal(0, roster.Add("Host", isHost: true).PlayerId);
            var a = roster.Add("A");
            var b = roster.Add("B");
            Assert.Equal(1, a.PlayerId);
            Assert.Equal(2, b.PlayerId);
            Assert.Equal(2, b.ColourIndex);

            roster.Remove(1);
            var c = roster.Add("C");
            Assert.Equal(1, c.PlayerId);
            Assert.Equal(1, c.ColourIndex);
            Assert.Equal(new byte[] { 0, 1, 2 }, new[] { roster.Players[0].PlayerId, roster.Players[1].PlayerId, roster.Players[2].PlayerId });
        }

        [Fact]
        public void SixPlayersFillTheSession()
        {
            var roster = new LobbyRoster();
            for (int i = 0; i < LobbyRoster.MaxPlayers; i++)
            {
                Assert.NotNull(roster.Add("P" + i));
            }

            Assert.True(roster.IsFull);
            Assert.Null(roster.Add("Seventh"));
        }

        [Fact]
        public void JoinChecks()
        {
            var roster = new LobbyRoster();
            Assert.Equal(JoinResult.Accepted, roster.CheckJoin(2, 2, eventInProgress: false));
            Assert.Equal(JoinResult.VersionMismatch, roster.CheckJoin(1, 2, eventInProgress: false));
            Assert.Equal(JoinResult.EventInProgress, roster.CheckJoin(2, 2, eventInProgress: true));

            for (int i = 0; i < LobbyRoster.MaxPlayers; i++)
            {
                roster.Add("P" + i);
            }

            Assert.Equal(JoinResult.SessionFull, roster.CheckJoin(2, 2, eventInProgress: false));
        }

        [Fact]
        public void ResettingReadyUnreadiesGuestsButNotTheHost()
        {
            var roster = new LobbyRoster();
            roster.Add("Host", isHost: true);
            var a = roster.Add("A");
            roster.SetReady(a.PlayerId, true);

            roster.ResetReady();
            Assert.False(a.Ready);
            Assert.True(roster.Players[0].Ready); // the host stays ready
        }

        [Fact]
        public void TheHostCannotBeUnreadied()
        {
            var roster = new LobbyRoster();
            var host = roster.Add("Host", isHost: true);
            Assert.False(roster.SetReady(host.PlayerId, false));
            Assert.True(host.Ready);
            Assert.False(roster.SetReady(42, true));
        }

        [Theory]
        [InlineData("  Josh  ", "Josh")]
        [InlineData("", "Player")]
        [InlineData("   ", "Player")]
        [InlineData(null, "Player")]
        [InlineData("A very long phone name that goes on", "A very long phone name t")]
        public void NamesAreCleaned(string input, string expected)
        {
            Assert.Equal(expected, LobbyRoster.CleanName(input));
        }

        [Fact]
        public void NamesAreUniqueIgnoringCase()
        {
            var roster = new LobbyRoster();
            roster.Add("Sam", isHost: true);
            Assert.Equal("sam 2", roster.Add(" sam ").Name);
            Assert.Equal("Sam 3", roster.Add("Sam").Name);
            Assert.Equal("Player", roster.Add("").Name);
            Assert.Equal("Player 2", roster.Add(null).Name);
        }

        [Fact]
        public void NumberedNamesStayWithinTheLimit()
        {
            var roster = new LobbyRoster();
            roster.Add("A very long phone name that goes on");
            Assert.Equal("A very long phone name 2", roster.Add("A very long phone name that goes on").Name);
        }

        [Fact]
        public void CuttingANameDoesNotSplitAnEmoji()
        {
            Assert.Equal(new string('a', 23), LobbyRoster.CleanName(new string('a', 23) + "\U0001F600"));

            var roster = new LobbyRoster();
            string name = new string('a', 21) + "\U0001F600";
            Assert.Equal(name, roster.Add(name).Name);
            Assert.Equal(new string('a', 21) + " 2", roster.Add(name).Name);
        }

        [Fact]
        public void BotsSkipNamesPeopleHave()
        {
            var roster = new LobbyRoster();
            roster.Add(LobbyRoster.BotNames[0], isHost: true);
            roster.Add(LobbyRoster.BotNames[1].ToLowerInvariant());
            Assert.Equal(LobbyRoster.BotNames[2], roster.AddBot().Name);
        }
    }
}
