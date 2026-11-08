using System;
using System.Linq;
using CarRacer.Core.Scoring;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class ChampionshipTests
    {
        private static Championship NewSeries(int events = 3)
        {
            var c = new Championship(events);
            c.AddPlayer("ana#1", "Ana");
            c.AddPlayer("ben#2", "Ben");
            c.AddPlayer("cy#3", "Cy");
            return c;
        }

        [Fact]
        public void PointsFollowTheTable()
        {
            var c = NewSeries();
            c.RecordEvent(new[] { "ben#2", "ana#1", "cy#3" });

            var standings = c.Standings();
            Assert.Equal(new[] { "Ben", "Ana", "Cy" }, standings.Select(e => e.DisplayName));
            Assert.Equal(new[] { 10, 8, 6 }, standings.Select(e => e.Points));
            Assert.Equal(1, standings[0].Wins);
            Assert.Equal(0, c.PointsForPosition(7));
        }

        [Fact]
        public void TiesBreakOnWinsThenLatestFinish()
        {
            var c = new Championship(4, new[] { 10, 5, 0 });
            c.AddPlayer("a", "A");
            c.AddPlayer("b", "B");
            c.AddPlayer("c", "C");

            // Everyone on 10 points. A and C have a win each, B has none, so B is last;
            // C beat A in the latest event, so C leads.
            c.RecordEvent(new[] { "a", "b", "c" });
            c.RecordEvent(new[] { "c", "b", "a" });
            Assert.All(c.Standings(), e => Assert.Equal(10, e.Points));
            Assert.Equal(new[] { "c", "a", "b" }, c.Standings().Select(e => e.PlayerKey));
        }

        [Fact]
        public void AbsentPlayersScoreNothing()
        {
            var c = NewSeries();
            c.RecordEvent(new[] { "cy#3", "ana#1" });
            var ben = c.Standings().Single(e => e.PlayerKey == "ben#2");
            Assert.Equal(0, ben.Points);
            Assert.Equal(0, ben.LastEventPosition);
        }

        [Fact]
        public void ReverseGridPutsTheLeaderLast()
        {
            var c = NewSeries();
            c.RecordEvent(new[] { "ana#1", "ben#2", "cy#3" });
            Assert.Equal(new[] { "cy#3", "ben#2", "ana#1" }, c.GridOrder(reverseGrid: true));
            Assert.Equal(new[] { "ana#1", "ben#2", "cy#3" }, c.GridOrder(reverseGrid: false));
        }

        [Fact]
        public void CompletesAfterTheLastEvent()
        {
            var c = NewSeries(events: 2);
            c.RecordEvent(new[] { "ana#1" });
            Assert.False(c.IsComplete);
            c.RecordEvent(new[] { "ana#1" });
            Assert.True(c.IsComplete);
            Assert.Throws<InvalidOperationException>(() => c.RecordEvent(new[] { "ana#1" }));
        }

        [Fact]
        public void InvalidResultsAreRejected()
        {
            var c = NewSeries();
            Assert.Throws<ArgumentException>(() => c.RecordEvent(new[] { "ana#1", "ana#1" }));
            Assert.Throws<ArgumentException>(() => c.RecordEvent(new[] { "stranger" }));
        }

        [Fact]
        public void RejectedResultsLeaveStandingsUntouched()
        {
            var c = NewSeries();
            c.RecordEvent(new[] { "ben#2", "ana#1", "cy#3" });
            var before = c.Standings().Select(e => (e.PlayerKey, e.Points, e.Wins, e.LastEventPosition)).ToList();

            Assert.Throws<ArgumentException>(() => c.RecordEvent(new[] { "ana#1", "stranger" }));

            Assert.Equal(before, c.Standings().Select(e => (e.PlayerKey, e.Points, e.Wins, e.LastEventPosition)));
            Assert.Equal(1, c.EventsCompleted);
        }

        [Fact]
        public void SaveAndResumeRoundTrips()
        {
            var c = NewSeries(events: 5);
            c.RecordEvent(new[] { "ben#2", "ana#1", "cy#3" });
            c.RecordEvent(new[] { "ana#1", "cy#3", "ben#2" });

            var resumed = Championship.FromBytes(c.ToBytes());

            Assert.Equal(5, resumed.TotalEvents);
            Assert.Equal(2, resumed.EventsCompleted);
            var expected = c.Standings();
            var actual = resumed.Standings();
            Assert.Equal(expected.Select(e => (e.PlayerKey, e.DisplayName, e.Points, e.Wins, e.LastEventPosition)),
                actual.Select(e => (e.PlayerKey, e.DisplayName, e.Points, e.Wins, e.LastEventPosition)));

            resumed.RecordEvent(new[] { "cy#3" });
            Assert.Equal(3, resumed.EventsCompleted);
        }
    }
}
