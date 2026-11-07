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
        public void SummarisesTheNextEventAndSurvivesABadCup()
        {
            var settings = LobbySettings.Default;
            settings.Mode = GameMode.Sumo;
            settings.Rules = new EventRules(false, true, true);
            Assert.Equal("Sumo. Rules: no damage", EventText.SettingsSummary(settings));
            settings.Cup = 1;
            Assert.StartsWith(Scoring.Cups.All[0].Name, EventText.SettingsSummary(settings));
            settings.Cup = 99;
            settings.Mode = GameMode.Race;
            settings.Track = -5;
            Assert.StartsWith("Race, ", EventText.SettingsSummary(settings));
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
        }
    }
}
