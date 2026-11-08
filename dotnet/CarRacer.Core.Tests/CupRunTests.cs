using System.Collections.Generic;
using System.Linq;
using CarRacer.Core.Arena;
using CarRacer.Core.Net;
using CarRacer.Core.Race;
using CarRacer.Core.Scoring;
using CarRacer.Core.Session;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class CupRunTests
    {
        private const ushort Version = 17;
        private const ulong GuestConnection = 7;

        /// <summary>The format version <see cref="CupRun"/> and <see cref="Championship"/> both write, 1 today.</summary>
        private const ushort SaveFormatVersion = 1;

        /// <summary>Three Battles: quick to end, by spinning the guest out three times.</summary>
        private static readonly Cup ThreeBattles = new Cup("grand-prix", "Grand Prix", new[]
        {
            CupEvent.ArenaEvent(GameMode.Battle),
            CupEvent.ArenaEvent(GameMode.Battle),
            CupEvent.ArenaEvent(GameMode.Battle),
        });

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
        private readonly HostLink _hostLink = new HostLink();
        private readonly HostSession _host;
        private readonly HostRace _race;
        private readonly GuestSession _guest;
        private readonly GuestRace _guestRace;
        private readonly List<byte[]> _saves = new List<byte[]>();

        public CupRunTests()
        {
            _host = new HostSession(_hostLink, "Host", Version, () => _now);
            _race = new HostRace(_host, new[] { 4, 4, 4 }, () => _now, id => 0f);
            _guest = new GuestSession(new GuestLink { Host = _host }, "Guest", Version, () => _now);
            _hostLink.Guest = _guest;
            _guest.OnConnected();
            _guest.OnMessage(new ClockPongMessage { GuestSentMs = _now - 20, HostMs = _now - 10 });
            _guestRace = new GuestRace(_guest, () => _now);
        }

        private byte HostId => _host.HostPlayerId;
        private byte GuestId => _guest.PlayerId;

        private CupRun NewCup(Cup cup = null, bool reverseGrid = false)
        {
            var run = new CupRun(_host, _race, () => _now, cup ?? ThreeBattles, reverseGrid);
            run.Saved += _saves.Add;
            return run;
        }

        private void Tick(CupRun run)
        {
            _race.Tick();
            _guestRace.Tick();
            run?.Tick();
        }

        /// <summary>Runs the current Battle to the end with <paramref name="loser"/> spun out of all their lives.</summary>
        private void PlayBattle(CupRun run, byte loser)
        {
            _now += HostRace.StartLeadMs + HostRace.CountdownMs;
            Tick(run);
            Assert.Equal(RacePhase.Racing, _race.Phase);
            for (int i = 0; i < BattleRules.StartingLives; i++)
            {
                _race.ReportSpinOut(loser);
            }

            _now += 100;
            Tick(run);
            Assert.Equal(RacePhase.Results, _race.Phase);
        }

        /// <summary>A <see cref="CupRun.ToBytes"/> save, built by hand so a field can be made invalid.</summary>
        private static byte[] Save(string cupId, byte[] championship)
        {
            var w = new NetWriter();
            w.WriteUShort(SaveFormatVersion);
            w.WriteString(cupId);
            w.WriteBool(false);
            w.WriteUShort((ushort)championship.Length);
            w.WriteBytes(championship);
            return w.ToArray();
        }

        /// <summary>A <see cref="Championship.ToBytes"/> save with no points table and no players.</summary>
        private static byte[] ChampionshipSave(int totalEvents, int eventsCompleted)
        {
            var w = new NetWriter();
            w.WriteUShort(SaveFormatVersion);
            w.WriteByte((byte)totalEvents);
            w.WriteByte((byte)eventsCompleted);
            w.WriteByte(0);
            w.WriteByte(0);
            return w.ToArray();
        }

        private static int Points(CupRun run, string name) => run.Championship.Standings().Single(e => e.DisplayName == name).Points;

        [Fact]
        public void TheCupStartsItsFirstEvent()
        {
            var run = NewCup(Cups.MixedCup);
            Assert.True(run.StartNextEvent());
            Assert.Equal(RacePhase.Countdown, _guestRace.Phase);
            Assert.Equal(GameMode.Race, _guestRace.Mode);
            Assert.Equal(0, _guestRace.TrackIndex);
            Assert.False(run.StartNextEvent());
        }

        [Fact]
        public void AnEventIsScoredAndEveryoneGetsTheTable()
        {
            var run = NewCup();
            run.StartNextEvent();
            PlayBattle(run, GuestId);

            Assert.Equal(10, Points(run, "Host"));
            Assert.Equal(8, Points(run, "Guest"));
            Assert.Equal(1, run.Championship.EventsCompleted);
            var table = _guestRace.CupStandings;
            Assert.NotNull(table);
            Assert.Equal("Grand Prix", table.CupName);
            Assert.Equal(1, table.EventsCompleted);
            Assert.Equal(3, table.TotalEvents);
            Assert.Equal(new[] { "Host", "Guest" }, table.Standings.Select(e => e.Name));
            Assert.Equal(new[] { 10, 8 }, table.Standings.Select(e => (int)e.Points));
            Assert.Equal(_now + CupRun.AdvanceMs, run.NextEventAtMs);
            Assert.Single(_saves);
        }

        [Fact]
        public void TheNextEventStartsByItselfAfterTheStandings()
        {
            var run = NewCup();
            run.StartNextEvent();
            PlayBattle(run, GuestId);

            _now += CupRun.AdvanceMs - 1;
            Tick(run);
            Assert.Equal(RacePhase.Results, _race.Phase);
            _now += 1;
            Tick(run);
            Assert.Equal(RacePhase.Countdown, _race.Phase);
            Assert.Equal(RacePhase.Countdown, _guestRace.Phase);
            Assert.Null(_guestRace.CupStandings);
        }

        [Fact]
        public void TheHostCanPauseOrSkipTheWait()
        {
            var run = NewCup();
            run.StartNextEvent();
            PlayBattle(run, GuestId);

            run.SetPaused(true);
            Assert.Equal(0, _guestRace.CupStandings.NextEventAtHostMs);
            _now += CupRun.AdvanceMs * 3;
            Tick(run);
            Assert.Equal(RacePhase.Results, _race.Phase);

            run.SetPaused(false);
            Assert.Equal(_now + CupRun.AdvanceMs, run.NextEventAtMs);
            Assert.True(run.AdvanceNow());
            Assert.Equal(RacePhase.Countdown, _race.Phase);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TheGridFollowsTheTableReversedOnRequest(bool reverseGrid)
        {
            var run = NewCup(reverseGrid: reverseGrid);
            run.StartNextEvent();
            PlayBattle(run, GuestId);
            run.AdvanceNow();

            // The host leads; with reverse grid the leader starts last.
            byte pole = reverseGrid ? GuestId : HostId;
            Assert.Equal(0, _race.GridSlotOf(pole));
            Assert.Equal(0, _guestRace.GridSlotOf(pole));
            Assert.Equal(1, _guestRace.GridSlotOf(pole == HostId ? GuestId : HostId));
        }

        [Fact]
        public void TheCupEndsAfterItsLastEvent()
        {
            var run = NewCup();
            run.StartNextEvent();
            PlayBattle(run, GuestId);
            run.AdvanceNow();
            PlayBattle(run, HostId);
            run.AdvanceNow();
            PlayBattle(run, GuestId);

            Assert.True(run.IsComplete);
            Assert.Null(run.NextEvent);
            Assert.Equal(0, run.NextEventAtMs);
            Assert.True(_guestRace.CupStandings.IsComplete);
            Assert.True(_race.ReturnToLobby());
            Assert.Null(_guestRace.CupStandings);
            Assert.Equal(10 + 8 + 10, Points(run, "Host"));
            Assert.Equal(8 + 10 + 8, Points(run, "Guest"));
            _now += CupRun.AdvanceMs * 2;
            Tick(run);
            Assert.Equal(RacePhase.Lobby, _race.Phase);
            Assert.False(run.AdvanceNow());
            Assert.False(run.StartNextEvent());
        }

        [Fact]
        public void ASavedCupResumesAtItsNextEvent()
        {
            var run = NewCup(Cups.MixedCup, reverseGrid: true);
            Assert.True(run.StartNextEvent());
            _now += HostRace.StartLeadMs + HostRace.CountdownMs;
            Tick(run);

            // The host crosses the start line and every gate for 3 laps; the guest runs out of time 30 s later.
            for (int lap = 0; lap < 3; lap++)
            {
                for (int gate = 0; gate < 4; gate++)
                {
                    _race.ReportOwnCheckpoint(gate);
                }
            }

            _race.ReportOwnCheckpoint(0);
            _now += HostRace.PostFinishMs;
            Tick(run);
            Assert.Equal(RacePhase.Results, _race.Phase);

            var resumed = CupRun.Resume(_saves.Single(), _host, _race, () => _now);
            Assert.NotNull(resumed);
            Assert.Same(Cups.MixedCup, resumed.Cup);
            Assert.True(resumed.ReverseGrid);
            Assert.Equal(1, resumed.Championship.EventsCompleted);
            Assert.Equal(1, resumed.NextEvent.Value.TrackIndex);
            Assert.Equal(10, Points(resumed, "Host"));
        }

        [Fact]
        public void BrokenOrForeignSavesDoNotResume()
        {
            Assert.Null(CupRun.Resume(new byte[] { 1, 2, 3 }, _host, _race, () => _now));
            var other = new CupRun(_host, _race, () => _now, new Cup("no-such-cup", "Gone", ThreeBattles.Events), false);
            Assert.Null(CupRun.Resume(other.ToBytes(), _host, _race, () => _now));
        }

        [Fact]
        public void SavesTheFixtureBuildsAreResumable()
        {
            Assert.NotNull(CupRun.Resume(Save(Cups.GrandPrix.Id, ChampionshipSave(Cups.GrandPrix.Events.Count, 0)), _host, _race, () => _now));
        }

        [Fact]
        public void ATruncatedSaveDoesNotResume()
        {
            var save = Save(Cups.GrandPrix.Id, ChampionshipSave(Cups.GrandPrix.Events.Count, 0));
            Assert.Null(CupRun.Resume(save.Take(save.Length - 1).ToArray(), _host, _race, () => _now));
        }

        [Fact]
        public void ASaveWithAnInvalidChampionshipDoesNotResume()
        {
            Assert.Null(CupRun.Resume(Save(Cups.GrandPrix.Id, ChampionshipSave(0, 0)), _host, _race, () => _now));
        }

        [Fact]
        public void ASaveWithTheWrongNumberOfEventsDoesNotResume()
        {
            Assert.Null(CupRun.Resume(Save(Cups.GrandPrix.Id, ChampionshipSave(Cups.GrandPrix.Events.Count + 1, 0)), _host, _race, () => _now));
        }

        [Fact]
        public void ACompletedCupDoesNotResume()
        {
            int events = Cups.GrandPrix.Events.Count;
            Assert.Null(CupRun.Resume(Save(Cups.GrandPrix.Id, ChampionshipSave(events, events)), _host, _race, () => _now));
        }

        [Fact]
        public void APlayerWhoRejoinsKeepsTheirPoints()
        {
            var run = NewCup();
            run.StartNextEvent();
            PlayBattle(run, GuestId);
            run.SetPaused(true);
            Assert.True(_race.ReturnToLobby());

            // The guest drops out in the lobby and joins again as "  GUEST " on a new phone.
            _host.OnDisconnected(GuestConnection);
            var rejoined = new GuestSession(new GuestLink { Host = _host }, "  GUEST ", Version, () => _now);
            _hostLink.Guest = rejoined;
            rejoined.OnConnected();
            rejoined.OnMessage(new ClockPongMessage { GuestSentMs = _now - 20, HostMs = _now - 10 });

            Assert.True(run.StartNextEvent());
            PlayBattle(run, HostId);

            var guests = run.Championship.Standings().Where(e => e.PlayerKey == "guest").ToList();
            Assert.Single(guests);
            Assert.Equal(8 + 10, guests[0].Points);
            Assert.Equal(2, run.Championship.Standings().Count);
        }
    }

    public class CupMessageTests
    {
        [Fact]
        public void TheTableSurvivesTheWire()
        {
            var sent = new CupStandingsMessage { CupName = "Mixed Cup", EventsCompleted = 2, TotalEvents = 5, NextEventAtHostMs = 123456 };
            sent.Standings.Add(new CupStandingsEntry { Name = "Ann", Points = 18, Wins = 1, LastEventPosition = 2 });
            var got = Assert.IsType<CupStandingsMessage>(MessageCodec.Decode(MessageCodec.Encode(sent)));
            Assert.Equal("Mixed Cup", got.CupName);
            Assert.Equal(2, got.EventsCompleted);
            Assert.Equal(5, got.TotalEvents);
            Assert.Equal(123456, got.NextEventAtHostMs);
            Assert.False(got.IsComplete);
            Assert.Equal(18, got.Standings.Single().Points);
            Assert.Equal(2, got.Standings.Single().LastEventPosition);
        }

        [Fact]
        public void TheGridOrderSurvivesTheWire()
        {
            var sent = new RaceCountdownMessage { LapCount = 3 };
            sent.GridOrder.AddRange(new byte[] { 4, 1, 2 });
            var got = Assert.IsType<RaceCountdownMessage>(MessageCodec.Decode(MessageCodec.Encode(sent)));
            Assert.Equal(new byte[] { 4, 1, 2 }, got.GridOrder);
        }

        [Fact]
        public void GridSlotsComeFromTheOrder()
        {
            var order = new List<byte> { 4, 1, 2 };
            Assert.Equal(0, RaceGrid.SlotOf(order, 4));
            Assert.Equal(2, RaceGrid.SlotOf(order, 2));
            Assert.Equal(3 + 5, RaceGrid.SlotOf(order, 5));
            Assert.Equal(5, RaceGrid.SlotOf(new List<byte>(), 5));
        }

        [Fact]
        public void TheBuiltInCupsAreAsDesigned()
        {
            // Every track, in catalog order, so a new track joins the cups or fails here.
            var everyTrack = Enumerable.Range(0, CarRacer.Core.Track.SampleTracks.All().Length).ToArray();
            Assert.Equal(everyTrack, Cups.GrandPrix.Events.Select(e => e.TrackIndex));
            Assert.All(Cups.GrandPrix.Events, e => Assert.Equal(GameMode.Race, e.Mode));
            Assert.Equal(everyTrack, Cups.MixedCup.Events.Where(e => e.Mode == GameMode.Race).Select(e => e.TrackIndex));
            Assert.Equal(new[] { GameMode.Sumo, GameMode.Battle }, Cups.MixedCup.Events.Skip(everyTrack.Length).Select(e => e.Mode));
            Assert.Equal(everyTrack.Length + 2, Cups.MixedCup.Events.Count);
            Assert.Same(Cups.MixedCup, Cups.Find("mixed"));
            Assert.Null(Cups.Find("nope"));
        }
    }
}
