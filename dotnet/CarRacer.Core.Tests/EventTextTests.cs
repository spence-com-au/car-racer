using System.Linq;
using CarRacer.Core.Arena;
using CarRacer.Core.Art;
using CarRacer.Core.Net;
using CarRacer.Core.Race;
using CarRacer.Core.Session;
using CarRacer.Core.Track;
using CarRacer.Core.Viewer;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class EventTextTests
    {
        [Fact]
        public void NamesModesEventsAndRules()
        {
            Assert.Equal(new[] { "Race", "Sumo", "Battle", "Monster Jam" }, new[] { GameMode.Race, GameMode.Sumo, GameMode.Battle, GameMode.Monster }.Select(EventText.ModeName));
            Assert.Equal(TrackCatalog.Layouts[0].Definition.DisplayName, EventText.EventName(GameMode.Race, 99));
            Assert.Equal("Battle", EventText.EventName(GameMode.Battle, 3));
            Assert.Equal(string.Empty, EventText.RulesNote(EventRules.Default));
            Assert.Equal("Rules: no items", EventText.RulesNote(new EventRules(true, false, true)));
        }

        [Fact]
        public void CountdownCountsDownThenSaysGo()
        {
            Assert.Null(EventText.Countdown(RacePhase.Countdown, 3500));
            Assert.Equal("3", EventText.Countdown(RacePhase.Countdown, 2999));
            Assert.Equal("1", EventText.Countdown(RacePhase.Countdown, 1));

            // The phase and the clock can disagree for a frame either way; it says GO! as the clock reaches the start.
            Assert.Equal("GO!", EventText.Countdown(RacePhase.Countdown, 0));
            Assert.Equal("GO!", EventText.Countdown(RacePhase.Racing, -999));
            Assert.Null(EventText.Countdown(RacePhase.Racing, -1000));
            Assert.Null(EventText.Countdown(RacePhase.Racing, 500));
            Assert.Null(EventText.Countdown(RacePhase.Results, 0));
        }

        [Fact]
        public void ResultsReadTheSameOnEveryScreen()
        {
            var result = new RaceResultEntry { RaceTimeMs = 83_456, Points = 37 };
            Assert.Equal("1:23.45", EventText.ResultDetail(GameMode.Race, result));
            Assert.Equal("37 pts", EventText.ResultDetail(GameMode.Monster, result));
            result.DidNotFinish = true;
            Assert.Equal("DNF", EventText.ResultDetail(GameMode.Battle, result));
            Assert.Equal("37 pts", EventText.ResultDetail(GameMode.Monster, result));
        }

        [Fact]
        public void ClocksAreWholeSeconds()
        {
            Assert.Equal("1:05", RaceFormat.Clock(65_999));
            Assert.Equal("0:00", RaceFormat.Clock(-5));
            Assert.Equal("12:00", RaceFormat.Clock(720_000));
        }

        [Fact]
        public void SummarisesTheNextEventFromCleanSettings()
        {
            var settings = LobbySettings.Default;
            settings.Mode = GameMode.Sumo;
            settings.Rules = new EventRules(false, true, true);
            Assert.Equal("Sumo. Rules: no damage", EventText.SettingsSummary(settings));
            settings.Cup = 1;
            Assert.Equal(Scoring.Cups.All[0].Name + ". Rules: no damage", EventText.SettingsSummary(settings));

            // An out-of-range cup and track fall back to the first track in a plain race.
            settings.Cup = 99;
            settings.Mode = GameMode.Race;
            settings.Track = -5;
            settings.Rules = EventRules.Default;
            Assert.Equal("Race, 3 laps, Pebble Oval", EventText.SettingsSummary(settings));
            settings.Laps = 1;
            Assert.Equal("Race, 1 lap, Pebble Oval", EventText.SettingsSummary(settings));
        }

        [Fact]
        public void LobbyStateReadsHostBotLeaderThenReadiness()
        {
            var player = new RosterEntry { PlayerId = 5 };
            Assert.Equal("not ready", EventText.LobbyState(player, leaderId: 9));
            player.Ready = true;
            Assert.Equal("ready", EventText.LobbyState(player, leaderId: 9));
            Assert.Equal("runs the room", EventText.LobbyState(player, leaderId: 5));
            player.IsBot = true;
            Assert.Equal("bot", EventText.LobbyState(player, leaderId: 5));
            player.IsHost = true;
            Assert.Equal("host", EventText.LobbyState(player, leaderId: 5));
        }

        private static StandingsMessage Order(params StandingsEntry[] entries) => new StandingsMessage { Order = entries.ToList() };

        [Fact]
        public void ARaceHudShowsPlaceLapAndRules()
        {
            var race = new FakeRaceView
            {
                Rules = new EventRules(true, false, true),
                Standings = Order(
                    new StandingsEntry { PlayerId = 4, FinishPosition = 1 },
                    new StandingsEntry { PlayerId = 7 },
                    new StandingsEntry { PlayerId = 2 }),
            };

            // Racing: the place in the host's order and the lap from this car's own count.
            Assert.Equal(("2nd / 3", "Lap 2 / 3", "Rules: no items"), EventText.PhoneStatus(race, 7, lapsCompleted: 1, sinceStartMs: 5_000));

            // Home: the finishing place, and the last lap whatever this car had counted.
            Assert.Equal(("Finished 1st", "Lap 3 / 3", "Rules: no items"), EventText.PhoneStatus(race, 4, lapsCompleted: 0, sinceStartMs: 5_000));

            // The lap never runs past the last one, and a car the standings don't list has no place.
            Assert.Equal((string.Empty, "Lap 3 / 3", "Rules: no items"), EventText.PhoneStatus(race, 9, lapsCompleted: 5, sinceStartMs: 5_000));

            // Before the first standings.
            race.Standings = null;
            Assert.Equal((string.Empty, "Lap 1 / 3", "Rules: no items"), EventText.PhoneStatus(race, 7, lapsCompleted: 0, sinceStartMs: -2_000));
        }

        [Fact]
        public void AMonsterJamHudShowsPlacePointsAndTimeLeft()
        {
            var race = new FakeRaceView
            {
                Mode = GameMode.Monster,
                Standings = Order(new StandingsEntry { PlayerId = 2, Points = 37 }, new StandingsEntry { PlayerId = 1, Points = 12 }),
            };
            Assert.Equal(("2nd / 2", "12 pts", "1:30.00"), EventText.PhoneStatus(race, 1, lapsCompleted: 0, sinceStartMs: 30_000));
            Assert.Equal(("1st / 2", "37 pts", "2:00.00"), EventText.PhoneStatus(race, 2, lapsCompleted: 0, sinceStartMs: -500));

            // The TV words the points the same way.
            Assert.Equal(EventText.PhoneStatus(race, 2, 0, 0).Corner2, ViewerScene.Build(ViewerSceneTests.Racing(race, 0)).Rows[0].Detail);

            // The clock only runs once the race has started, and nobody is listed before the first standings.
            race.Phase = RacePhase.Countdown;
            race.Standings = null;
            Assert.Equal((string.Empty, string.Empty, string.Empty), EventText.PhoneStatus(race, 1, lapsCompleted: 0, sinceStartMs: -2_000));
        }

        [Fact]
        public void AnArenaHudShowsLivesOrPlaceOutAndHowManyAreLeft()
        {
            var standings = Order(
                new StandingsEntry { PlayerId = 1, LapsCompleted = 2 },
                new StandingsEntry { PlayerId = 3, LapsCompleted = 1 },
                new StandingsEntry { PlayerId = 2, FinishPosition = 3 });

            // Battle counts lives (which travel in LapsCompleted) and has a clock.
            var battle = new FakeRaceView { Mode = GameMode.Battle, Standings = standings };
            Assert.Equal(("Lives 2", "2 / 3 left", "2:00.00"), EventText.PhoneStatus(battle, 1, lapsCompleted: 0, sinceStartMs: 60_000));
            Assert.Equal(("Out: 3rd", "2 / 3 left", "2:00.00"), EventText.PhoneStatus(battle, 2, lapsCompleted: 0, sinceStartMs: 60_000));

            // Sumo has neither lives nor a clock, and nothing is listed before the first standings.
            var sumo = new FakeRaceView { Mode = GameMode.Sumo, Standings = standings };
            Assert.Equal(("Still on", "2 / 3 left", string.Empty), EventText.PhoneStatus(sumo, 1, lapsCompleted: 0, sinceStartMs: 60_000));
            Assert.Equal(("Out: 3rd", "2 / 3 left", string.Empty), EventText.PhoneStatus(sumo, 2, lapsCompleted: 0, sinceStartMs: 60_000));
            sumo.Standings = null;
            Assert.Equal((string.Empty, string.Empty, string.Empty), EventText.PhoneStatus(sumo, 1, lapsCompleted: 0, sinceStartMs: 60_000));
        }
    }

    public class CarPaletteTests
    {
        [Fact]
        public void EveryRosterColourHasADistinctPaintAndIndicesWrap()
        {
            Assert.Equal(LobbyRoster.ColourCount, CarPalette.Count);
            Assert.Equal(CarPalette.Count, Enumerable.Range(0, CarPalette.Count).Select(CarPalette.ForIndex).Distinct().Count());
            Assert.Equal(CarPalette.ForIndex(1), CarPalette.ForIndex(1 + CarPalette.Count));
            Assert.Equal(CarPalette.ForIndex(CarPalette.Count - 1), CarPalette.ForIndex(-1));
            Assert.Equal(new Rgb(242, 191, 26), CarPalette.ForIndex(0));

            // Light paint gets dark text over it, and dark paint light text.
            Assert.True(CarPalette.ForIndex(6).Luma > 0.5f);
            Assert.True(CarPalette.ForIndex(7).Luma < 0.5f);
            Assert.Equal(Rgb.FromFloats(1f, 0.5f, 0f), new Rgb(255, 128, 0));
        }
    }
}
