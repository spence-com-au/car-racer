using System.Linq;
using CarRacer.Core.Arena;
using CarRacer.Core.Net;
using CarRacer.Core.Race;
using CarRacer.Core.Scoring;
using CarRacer.Core.Session;
using CarRacer.Core.Track;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class HostLobbyRulesTests
    {
        private const ushort Version = 5;
        private const int Gates = 4;

        private sealed class MemoryStore : ICupSaveStore
        {
            public string Text = string.Empty;

            public string Load() => Text;

            public void Save(string save) => Text = save;

            public void Delete() => Text = string.Empty;
        }

        private long _now = 10_000;
        private readonly MemoryStore _store = new MemoryStore();
        private HostSession _host;
        private HostRace _race;
        private HostLobbyRules _rules;

        public HostLobbyRulesTests()
        {
            Create(new HostSession(new RecordingTransport(), "Host", Version, () => _now));
        }

        private void Create(HostSession host)
        {
            _host = host;
            var checkpointCounts = Enumerable.Repeat(Gates, SampleTracks.All().Length).ToArray();
            _race = new HostRace(_host, checkpointCounts, () => _now, id => 0f);
            _rules = new HostLobbyRules(_host, _race, () => _now, _store);
        }

        private void Choose(int cup = 0, GameMode mode = GameMode.Race, int laps = 3, int track = 0)
        {
            var settings = LobbySettings.Default;
            settings.Cup = cup;
            settings.Mode = mode;
            settings.Laps = laps;
            settings.Track = track;
            _host.ChangeSettings(settings);
        }

        private void Tick()
        {
            _race.Tick();
            _rules.Tick();
        }

        /// <summary>The host, alone, drives the event under way to the finish.</summary>
        private void FinishRace()
        {
            _now += HostRace.StartLeadMs + HostRace.CountdownMs;
            Tick();
            Assert.Equal(RacePhase.Racing, _race.Phase);
            for (int lap = 0; lap < _race.LapCount; lap++)
            {
                for (int gate = 0; gate < Gates; gate++)
                {
                    _race.ReportOwnCheckpoint(gate);
                }
            }

            _race.ReportOwnCheckpoint(0);
            _now += 100;
            Tick();
            Assert.Equal(RacePhase.Results, _race.Phase);
        }

        private byte Join(ulong connection, string name)
        {
            _host.OnMessage(connection, new JoinRequestMessage { ProtocolVersion = Version, PlayerName = name });
            return _host.Roster.Players.Single(p => p.Name == name).PlayerId;
        }

        [Fact]
        public void StartRunsASingleEventWithTheLobbySettings()
        {
            Choose(mode: GameMode.Race, laps: 2, track: 1);
            _rules.RunCommand(LobbyCommand.Start);

            Assert.Equal(RacePhase.Countdown, _race.Phase);
            Assert.Equal(2, _race.LapCount);
            Assert.Equal(1, _race.TrackIndex);
            Assert.Null(_rules.Run);

            FinishRace();
            _rules.RunCommand(LobbyCommand.BackToLobby);
            Assert.Equal(RacePhase.Lobby, _race.Phase);
            Assert.Equal(string.Empty, _store.Text);
        }

        [Fact]
        public void StartWaitsForEveryoneToBeReady()
        {
            Join(1, "Guest");
            _rules.RunCommand(LobbyCommand.Start);
            Assert.Equal(RacePhase.Lobby, _race.Phase);

            _host.OnMessage(1, new SetReadyMessage { Ready = true });
            _rules.RunCommand(LobbyCommand.Start);
            Assert.Equal(RacePhase.Countdown, _race.Phase);
        }

        [Fact]
        public void StartWithACupChosenRunsTheCupAndSavesIt()
        {
            Choose(cup: 1);
            _rules.RunCommand(LobbyCommand.Start);

            Assert.Same(Cups.GrandPrix, _rules.Run.Cup);
            Assert.Equal(Cups.GrandPrix.Events[0].TrackIndex, _race.TrackIndex);
            Assert.Equal(0, _host.SavedCup);

            FinishRace();
            Assert.NotEqual(string.Empty, _store.Text);
            Assert.Equal(1, _host.SavedCup);
            Assert.Equal(1, _host.SavedCupDone);
        }

        [Fact]
        public void TheLeaderPausesAndSkipsTheWaitBetweenEvents()
        {
            Choose(cup: 1);
            _rules.RunCommand(LobbyCommand.Start);
            FinishRace();

            _rules.RunCommand(LobbyCommand.PauseCup);
            Assert.True(_rules.Run.Paused);
            _now += CupRun.AdvanceMs * 2;
            Tick();
            Assert.Equal(RacePhase.Results, _race.Phase);

            _rules.RunCommand(LobbyCommand.UnpauseCup);
            Assert.False(_rules.Run.Paused);
            _rules.RunCommand(LobbyCommand.NextEvent);
            Assert.Equal(RacePhase.Countdown, _race.Phase);
            Assert.Equal(Cups.GrandPrix.Events[1].TrackIndex, _race.TrackIndex);
        }

        [Fact]
        public void LeavingACupKeepsItsSaveToResumeLater()
        {
            Choose(cup: 1);
            _rules.RunCommand(LobbyCommand.Start);
            FinishRace();

            _rules.RunCommand(LobbyCommand.LeaveCup);
            Assert.Null(_rules.Run);
            Assert.Equal(RacePhase.Lobby, _race.Phase);
            Assert.Equal(1, _host.SavedCup);

            // A new session on the same phone finds the save and picks the cup back up.
            string save = _store.Text;
            Create(new HostSession(new RecordingTransport(), "Host", Version, () => _now));
            Assert.Equal(save, _store.Text);
            Assert.Equal(1, _host.SavedCup);
            Assert.Equal(1, _host.SavedCupDone);

            _rules.RunCommand(LobbyCommand.ResumeSavedCup);
            Assert.Same(Cups.GrandPrix, _rules.Run.Cup);
            Assert.Equal(1, _rules.Run.Championship.EventsCompleted);
            Assert.Equal(RacePhase.Countdown, _race.Phase);
            Assert.Equal(Cups.GrandPrix.Events[1].TrackIndex, _race.TrackIndex);
        }

        [Fact]
        public void BackToLobbyWaitsForTheCupToEndThenDropsItsSave()
        {
            Choose(cup: 1);
            _rules.RunCommand(LobbyCommand.Start);
            FinishRace();

            _rules.RunCommand(LobbyCommand.BackToLobby);
            Assert.Equal(RacePhase.Results, _race.Phase);
            Assert.NotNull(_rules.Run);

            for (int i = 1; i < Cups.GrandPrix.Events.Count; i++)
            {
                _rules.RunCommand(LobbyCommand.NextEvent);
                FinishRace();
            }

            Assert.True(_rules.Run.IsComplete);
            _rules.RunCommand(LobbyCommand.BackToLobby);
            Assert.Null(_rules.Run);
            Assert.Equal(RacePhase.Lobby, _race.Phase);
            Assert.Equal(string.Empty, _store.Text);
            Assert.Equal(0, _host.SavedCup);
        }

        [Fact]
        public void AnUnreadableSaveIsNotOffered()
        {
            _store.Text = "not a save";
            Create(new HostSession(new RecordingTransport(), "Host", Version, () => _now));
            Assert.Equal(0, _host.SavedCup);

            _rules.RunCommand(LobbyCommand.ResumeSavedCup);
            Assert.Null(_rules.Run);
            Assert.Equal(RacePhase.Lobby, _race.Phase);
        }

        [Fact]
        public void AServerWithNobodyLeftGoesBackToTheLobbyOnceTheResultsAreIn()
        {
            Create(HostSession.Dedicated(new RecordingTransport(), Version, () => _now));
            Join(1, "Alice");
            byte bob = Join(2, "Bob");
            _host.OnMessage(2, new SetReadyMessage { Ready = true });
            Choose(mode: GameMode.Battle);
            _rules.RunCommand(LobbyCommand.Start);
            _now += HostRace.StartLeadMs + HostRace.CountdownMs;
            Tick();
            for (int i = 0; i < BattleRules.StartingLives; i++)
            {
                _race.ReportSpinOut(bob);
            }

            _now += 100;
            Tick();
            Assert.Equal(RacePhase.Results, _race.Phase);

            // While anyone is left to lead, the results wait for them.
            _host.OnDisconnected(2);
            Tick();
            Assert.Equal(RacePhase.Results, _race.Phase);

            _host.OnDisconnected(1);
            Assert.Equal(HostSession.NoPlayer, _host.LeaderId);
            Tick();
            Assert.Equal(RacePhase.Lobby, _race.Phase);
        }
    }
}
