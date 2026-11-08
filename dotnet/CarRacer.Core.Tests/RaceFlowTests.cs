using System.Collections.Generic;
using System.Linq;
using CarRacer.Core.Net;
using CarRacer.Core.Numerics;
using CarRacer.Core.Race;
using CarRacer.Core.Session;
using CarRacer.Core.Track;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class RaceMessageTests
    {
        [Fact]
        public void RaceMessagesRoundTrip()
        {
            var standings = Assert.IsType<StandingsMessage>(MessageCodec.Decode(MessageCodec.Encode(new StandingsMessage
            {
                Order = { new StandingsEntry { PlayerId = 2, LapsCompleted = 1, FinishPosition = 0 }, new StandingsEntry { PlayerId = 0, LapsCompleted = 3, FinishPosition = 1 } },
            })));
            Assert.Equal(new byte[] { 2, 0 }, standings.Order.Select(e => e.PlayerId));
            Assert.Equal(3, standings.Order[1].LapsCompleted);
            Assert.Equal(1, standings.Order[1].FinishPosition);

            var results = Assert.IsType<RaceResultsMessage>(MessageCodec.Decode(MessageCodec.Encode(new RaceResultsMessage
            {
                Results = { new RaceResultEntry { PlayerId = 1, Name = "Zoë", Position = 2, RaceTimeMs = 93456, DidNotFinish = true } },
            })));
            Assert.Equal("Zoë", results.Results[0].Name);
            Assert.Equal(93456, results.Results[0].RaceTimeMs);
            Assert.True(results.Results[0].DidNotFinish);

            Assert.IsType<ReturnToLobbyMessage>(MessageCodec.Decode(MessageCodec.Encode(new ReturnToLobbyMessage())));
        }
    }

    public class CheckpointDetectorTests
    {
        private static readonly TrackLayout Oval = new TrackLayout(SampleTracks.Oval());

        private static Vec3 Along(float distance, float lateral = 0f, TrackLayout track = null)
        {
            var s = (track ?? Oval).Spline.SampleAtDistance(distance);
            return s.Position + s.Right * lateral;
        }

        /// <summary>Drives along the centre line in 1 m steps.</summary>
        private static List<int> Drive(CheckpointDetector detector, float from, float to, float lateral = 0f, TrackLayout track = null)
        {
            var crossed = new List<int>();
            for (float d = from; d < to; d += 1f)
            {
                int c = detector.Update(Along(d, lateral, track), Along(d + 1f, lateral, track));
                if (c >= 0)
                {
                    crossed.Add(c);
                }
            }

            return crossed;
        }

        [Fact]
        public void ADetectorCanPickUpPartwayThroughARace()
        {
            var detector = new CheckpointDetector(Oval.CheckpointGates());
            detector.Resume(3);
            Assert.Equal(3, detector.NextCheckpoint);
            Assert.Equal(3, detector.CrossingsCount);
            detector.Resume(Oval.CheckpointCount + 2);
            Assert.Equal(2, detector.NextCheckpoint);
            Assert.Equal(1, detector.LapsCompleted);
        }

        [Fact]
        public void ALapCrossesEveryGateInOrder()
        {
            var detector = new CheckpointDetector(Oval.CheckpointGates());
            var crossed = Drive(detector, -6f, Oval.Spline.Length + 1f);
            Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 0 }, crossed);
            Assert.Equal(1, detector.LapsCompleted);
            Assert.Equal(1, detector.NextCheckpoint);
        }

        public static IEnumerable<object[]> Tracks => SampleTracks.All().Select(t => new object[] { t.Id });

        [Theory]
        [MemberData(nameof(Tracks))]
        public void ALapOfAnyTrackCrossesEveryGateInOrder(string id)
        {
            // Banking, climbs and the figure-eight's bridge all change a gate's height and lateral reach.
            var track = new TrackLayout(SampleTracks.All().Single(t => t.Id == id));
            var detector = new CheckpointDetector(track.CheckpointGates());
            var crossed = Drive(detector, -6f, track.Spline.Length + 1f, track: track);
            Assert.Equal(Enumerable.Range(0, track.CheckpointCount).Append(0), crossed);
            Assert.Equal(1, detector.LapsCompleted);
        }

        [Fact]
        public void ReversingOverAGateDoesNotCount()
        {
            var detector = new CheckpointDetector(Oval.CheckpointGates());
            for (float d = 5f; d > -5f; d -= 1f)
            {
                Assert.Equal(-1, detector.Update(Along(d), Along(d - 1f)));
            }

            Assert.Equal(0, detector.CrossingsCount);
        }

        [Fact]
        public void OnlyTheNextGateCounts()
        {
            // Start past gate 0 (as if the car skipped it): gate 1 isn't expected yet.
            var detector = new CheckpointDetector(Oval.CheckpointGates());
            float gate1 = Oval.CheckpointDistance(1);
            Assert.Empty(Drive(detector, 5f, gate1 + 5f));
        }

        [Fact]
        public void CrossingNearTheWallStillCounts()
        {
            var detector = new CheckpointDetector(Oval.CheckpointGates());
            float edge = Oval.Spline.SampleAtDistance(0f).Width / 2f + 1f;
            Assert.Equal(new[] { 0 }, Drive(detector, -3f, 3f, lateral: edge));
        }

        [Fact]
        public void PassingFarOutsideTheRoadDoesNotCount()
        {
            var detector = new CheckpointDetector(Oval.CheckpointGates());
            Assert.Empty(Drive(detector, -3f, 3f, lateral: 30f));
        }

        [Fact]
        public void ARespawnJumpIsNotACrossing()
        {
            var detector = new CheckpointDetector(Oval.CheckpointGates());
            Assert.Equal(-1, detector.Update(Along(-30f), Along(10f)));
            Assert.Equal(0, detector.CrossingsCount);
        }

        [Fact]
        public void ResetStartsOver()
        {
            var detector = new CheckpointDetector(Oval.CheckpointGates());
            Drive(detector, -6f, 60f);
            detector.Reset();
            Assert.Equal(0, detector.NextCheckpoint);
            Assert.Equal(0, detector.CrossingsCount);
        }
    }

    public class RaceFlowTests
    {
        private const ushort Version = 3;
        private const int Checkpoints = 4;

        private sealed class Transport : ISessionTransport
        {
            public readonly List<(ulong To, IGameMessage Message, bool Reliable)> Sent = new List<(ulong, IGameMessage, bool)>();
            public GuestSession Guest;

            public void Send(ulong connection, IGameMessage message, bool reliable)
            {
                Sent.Add((connection, message, reliable));
                if (Guest != null && connection == GuestLink.Connection)
                {
                    Guest.OnMessage(message);
                }
            }

            public void Disconnect(ulong connection)
            {
            }

            public List<T> All<T>() => Sent.Select(s => s.Message).OfType<T>().ToList();
        }

        private long _now = 10_000;
        private readonly Transport _transport = new Transport();
        private readonly HostSession _session;
        private readonly HostRace _race;
        private readonly Dictionary<int, float> _fractions = new Dictionary<int, float>();
        private readonly GuestSession _guest;
        private readonly GuestRace _guestRace;

        public RaceFlowTests()
        {
            _session = new HostSession(_transport, "Host", Version, () => _now);
            _race = new HostRace(_session, new[] { Checkpoints }, () => _now, id => _fractions.TryGetValue(id, out float f) ? f : 0f);

            // One guest, whose clock runs 1,000,000 ms behind the host's.
            var link = new GuestLink { Host = _session };
            _guest = new GuestSession(link, "Guest", Version, () => _now - 1_000_000);
            _transport.Guest = _guest;
            _guest.OnConnected();
            _guest.OnMessage(new ClockPongMessage { GuestSentMs = _now - 1_000_020, HostMs = _now - 10 }); // 20 ms round trip
            _guestRace = new GuestRace(_guest, () => _now - 1_000_000);
        }

        private void Advance(long ms)
        {
            for (long t = 0; t < ms; t += 50)
            {
                _now += 50;
                _race.Tick();
                _guestRace.Tick();
            }
        }

        private void Lap(int playerId, int laps = 1)
        {
            for (int lap = 0; lap < laps; lap++)
            {
                for (int c = 0; c < Checkpoints; c++)
                {
                    Report(playerId, c);
                }
            }

            Report(playerId, 0); // across the line
        }

        private void Report(int playerId, int checkpoint)
        {
            if (playerId == _session.HostPlayerId)
            {
                _race.ReportOwnCheckpoint(checkpoint);
            }
            else
            {
                _guestRace.ReportOwnCheckpoint(checkpoint);
            }
        }

        [Fact]
        public void StartingSendsASyncedCountdown()
        {
            Assert.True(_race.Start(laps: 2));
            Assert.Equal(RacePhase.Countdown, _race.Phase);
            Assert.True(_session.EventInProgress);

            var countdown = _transport.All<RaceCountdownMessage>().Single();
            Assert.Equal(_now + HostRace.StartLeadMs + HostRace.CountdownMs, countdown.StartAtHostMs);
            Assert.Equal(2, countdown.LapCount);

            // The guest's clock is 1,000,000 ms behind, so its start time is too.
            Assert.Equal(RacePhase.Countdown, _guestRace.Phase);
            Assert.Equal(countdown.StartAtHostMs - 1_000_000, _guestRace.StartAtLocalMs);
            Assert.Equal(2, _guestRace.LapCount);
        }

        [Fact]
        public void BothSidesGoGreenTogether()
        {
            _race.Start(laps: 1);
            Advance(HostRace.StartLeadMs + HostRace.CountdownMs - 100);
            Assert.Equal(RacePhase.Countdown, _race.Phase);
            Assert.Equal(RacePhase.Countdown, _guestRace.Phase);

            Advance(100);
            Assert.Equal(RacePhase.Racing, _race.Phase);
            Assert.Equal(RacePhase.Racing, _guestRace.Phase);
        }

        [Fact]
        public void CannotStartTwiceOrWithNoLaps()
        {
            Assert.False(_race.Start(laps: 0));
            Assert.True(_race.Start(laps: 1));
            Assert.False(_race.Start(laps: 1));
        }

        [Fact]
        public void CheckpointsBeforeTheStartAreIgnored()
        {
            _race.Start(laps: 1);
            _race.ReportOwnCheckpoint(0);
            _session.OnMessage(7, new CheckpointReachedMessage { PlayerId = 1, CheckpointIndex = 0 });
            Assert.Equal(0, _race.Tracker.Get(0).CheckpointsPassed);
            Assert.Equal(0, _race.Tracker.Get(1).CheckpointsPassed);
        }

        [Fact]
        public void FinishersAreAnnouncedWithTheirTime()
        {
            _race.Start(laps: 1);
            Advance(HostRace.StartLeadMs + HostRace.CountdownMs);
            Advance(40_000);
            Lap(1);

            var finished = _transport.All<PlayerFinishedMessage>().Single();
            Assert.Equal(1, finished.PlayerId);
            Assert.Equal(1, finished.FinishPosition);
            Assert.InRange(finished.RaceTimeMs, 39_900, 40_100);
            Assert.Equal(RacePhase.Racing, _race.Phase); // the host is still out there
        }

        [Fact]
        public void TheRaceEndsWhenEveryoneFinishes()
        {
            _race.Start(laps: 2);
            Advance(HostRace.StartLeadMs + HostRace.CountdownMs);
            Lap(0, laps: 2);
            Lap(1, laps: 2);
            Advance(50);

            Assert.Equal(RacePhase.Results, _race.Phase);
            Assert.Equal(RacePhase.Results, _guestRace.Phase);
            Assert.Equal(new byte[] { 0, 1 }, _guestRace.Results.Results.Select(r => r.PlayerId));
            Assert.All(_race.Results.Results, r => Assert.False(r.DidNotFinish));
        }

        [Fact]
        public void TheRaceEndsThirtySecondsAfterTheWinner()
        {
            _race.Start(laps: 1);
            Advance(HostRace.StartLeadMs + HostRace.CountdownMs);
            Lap(0);
            Advance(HostRace.PostFinishMs - 100);
            Assert.Equal(RacePhase.Racing, _race.Phase);

            Advance(100);
            Assert.Equal(RacePhase.Results, _race.Phase);
            var guest = _race.Results.Results.Single(r => r.PlayerId == 1);
            Assert.True(guest.DidNotFinish);
            Assert.Equal(2, guest.Position);
        }

        [Fact]
        public void StandingsFollowTheRace()
        {
            _race.Start(laps: 2);
            Advance(HostRace.StartLeadMs + HostRace.CountdownMs);
            Report(1, 0);
            Report(1, 1);
            Report(0, 0);
            Advance(HostRace.StandingsIntervalMs);

            Assert.Equal(new byte[] { 1, 0 }, _race.Standings.Order.Select(e => e.PlayerId));
            Assert.Equal(new byte[] { 1, 0 }, _guestRace.Standings.Order.Select(e => e.PlayerId));
            Assert.False(_transport.Sent.First(s => s.Message is StandingsMessage).Reliable);

            // Same checkpoints: track position decides.
            Report(0, 1);
            _fractions[0] = 0.8f;
            _fractions[1] = 0.2f;
            Advance(HostRace.StandingsIntervalMs);
            Assert.Equal(new byte[] { 0, 1 }, _race.Standings.Order.Select(e => e.PlayerId));
        }

        [Fact]
        public void AGuestsTimeIsCappedAtTheHostsElapsedTime()
        {
            _race.Start(laps: 1);
            Advance(HostRace.StartLeadMs + HostRace.CountdownMs + 10_000);
            for (int c = 0; c < Checkpoints; c++)
            {
                _session.OnMessage(7, new CheckpointReachedMessage { PlayerId = 1, CheckpointIndex = (ushort)c, RaceTimeMs = 1 });
            }

            _session.OnMessage(7, new CheckpointReachedMessage { PlayerId = 1, CheckpointIndex = 0, RaceTimeMs = 999_999 });
            Assert.InRange(_race.Tracker.Get(1).FinishTimeMs, 9_900, 10_100);
        }

        [Fact]
        public void AGuestCannotReportForSomeoneElse()
        {
            _race.Start(laps: 1);
            Advance(HostRace.StartLeadMs + HostRace.CountdownMs);
            _session.OnMessage(7, new CheckpointReachedMessage { PlayerId = 0, CheckpointIndex = 0 });
            Assert.Equal(0, _race.Tracker.Get(0).CheckpointsPassed);
        }

        [Fact]
        public void ResultsCarryEachPlayersName()
        {
            _race.Start(laps: 1);
            Advance(HostRace.StartLeadMs + HostRace.CountdownMs);
            Lap(0);
            Lap(1);
            Advance(50);
            Assert.Equal(new[] { "Host", "Guest" }, _guestRace.Results.Results.Select(r => r.Name));
        }

        [Fact]
        public void ABotTakesOverAGuestWhoLeavesMidRace()
        {
            _race.Start(laps: 1);
            Advance(HostRace.StartLeadMs + HostRace.CountdownMs);
            Report(1, 0);
            _session.OnDisconnected(7);

            // Same player, now a bot, still racing from where they were.
            var player = _session.Roster.Find(1);
            Assert.True(player.IsBot);
            Assert.Equal("Guest", player.Name);
            Assert.Equal(1, _race.Tracker.Get(1).NextCheckpoint);

            // The host reports the bot's checkpoints for the rest of the lap.
            for (int c = 1; c < Checkpoints; c++)
            {
                _race.ReportBotCheckpoint(1, c);
            }

            _race.ReportBotCheckpoint(1, 0);
            Assert.Equal(1, _race.Tracker.Get(1).FinishPosition);
            Lap(0);
            Advance(50);
            Assert.Equal(RacePhase.Results, _race.Phase);
            Assert.Equal("Guest", _race.Results.Results[0].Name);

            // Back in the lobby the bot leaves with the others.
            Assert.True(_race.ReturnToLobby());
            Assert.Null(_session.Roster.Find(1));
        }

        [Fact]
        public void LeavingInTheLobbyKeepsThemOutOfTheRace()
        {
            _session.OnDisconnected(7);
            Assert.True(_race.Start(laps: 1));
            Assert.Equal(new[] { 0 }, _race.Tracker.PlayerIds);
        }

        [Fact]
        public void BackToTheLobbyForARematch()
        {
            _guest.SetReady(true);
            Assert.False(_race.ReturnToLobby()); // not in results yet

            _race.Start(laps: 1);
            Advance(HostRace.StartLeadMs + HostRace.CountdownMs);
            Lap(0);
            Lap(1);
            Advance(50);

            Assert.True(_race.ReturnToLobby());
            Assert.Equal(RacePhase.Lobby, _race.Phase);
            Assert.Equal(RacePhase.Lobby, _guestRace.Phase);
            Assert.False(_session.EventInProgress);
            Assert.False(_session.Roster.Find(1).Ready);
            Assert.False(_guest.Roster.Players.Single(p => p.PlayerId == 1).Ready);
            Assert.False(_guest.Ready);

            Assert.True(_race.Start(laps: 1)); // rematch
            Assert.Null(_guestRace.Results);
        }
    }
}
