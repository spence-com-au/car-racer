using System.Linq;
using CarRacer.Core.Arena;
using CarRacer.Core.Net;
using CarRacer.Core.Numerics;
using CarRacer.Core.Session;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class SumoRulesTests
    {
        [Fact]
        public void ThePlatformHoldsItsSizeThenShrinksAndStops()
        {
            Assert.Equal(SumoRules.StartRadius, SumoRules.PlatformRadius(0f));
            Assert.Equal(SumoRules.StartRadius, SumoRules.PlatformRadius(SumoRules.GraceSeconds));
            float halfway = SumoRules.GraceSeconds + SumoRules.ShrinkSeconds / 2f;
            Assert.Equal((SumoRules.StartRadius + SumoRules.EndRadius) / 2f, SumoRules.PlatformRadius(halfway), 3);
            Assert.Equal(SumoRules.EndRadius, SumoRules.PlatformRadius(SumoRules.GraceSeconds + SumoRules.ShrinkSeconds));
            Assert.Equal(SumoRules.EndRadius, SumoRules.PlatformRadius(1000f));
        }

        [Fact]
        public void ACarIsOutPastTheEdgeOrWhenItHasFallen()
        {
            var centre = new Vec3(0f, 10f, 0f);
            Assert.False(SumoRules.IsOut(new Vec3(19f, 10.6f, 0f), centre, 20f));
            Assert.True(SumoRules.IsOut(new Vec3(21f, 10.6f, 0f), centre, 20f));
            Assert.True(SumoRules.IsOut(new Vec3(0f, 10f - SumoRules.FallDepth - 0.1f, 0f), centre, 20f));
            Assert.False(SumoRules.IsOut(new Vec3(0f, 15f, 0f), centre, 20f)); // airborne over the platform
        }
    }

    public class EliminationTrackerTests
    {
        [Fact]
        public void TheLastCarStandingWinsAndTheRestRankByWhenTheyWentOut()
        {
            var tracker = new EliminationTracker(new[] { 0, 1, 2, 3 });
            Assert.False(tracker.IsOver);
            Assert.True(tracker.Eliminate(2, 5000));
            Assert.True(tracker.Eliminate(0, 9000));
            Assert.False(tracker.IsOver);
            Assert.True(tracker.Eliminate(3, 12000));
            Assert.True(tracker.IsOver);

            var placings = tracker.Placings(12000);
            Assert.Equal(new[] { 1, 3, 0, 2 }, placings.Select(p => p.PlayerId));
            Assert.Equal(new[] { 1, 2, 3, 4 }, placings.Select(p => p.Position));
            Assert.Equal(12000, placings[0].SurvivedMs);
            Assert.Equal(5000, placings[3].SurvivedMs);
        }

        [Fact]
        public void LivesRunDownBeforeAPlayerIsOut()
        {
            var tracker = new EliminationTracker(new[] { 0, 1 }, BattleRules.StartingLives);
            Assert.False(tracker.LoseLife(1, 100));
            Assert.False(tracker.LoseLife(1, 200));
            Assert.Equal(1, tracker.Lives(1));
            Assert.True(tracker.LoseLife(1, 300));
            Assert.True(tracker.IsOut(1));
            Assert.False(tracker.LoseLife(1, 400)); // already out
            Assert.True(tracker.IsOver);
        }

        [Fact]
        public void WhenTimeRunsOutTheMostLivesWins()
        {
            var tracker = new EliminationTracker(new[] { 0, 1, 2 }, BattleRules.StartingLives);
            tracker.LoseLife(0, 100);
            tracker.LoseLife(0, 200);
            tracker.LoseLife(2, 300);
            var placings = tracker.Placings(BattleRules.TimeLimitMs);
            Assert.Equal(new[] { 1, 2, 0 }, placings.Select(p => p.PlayerId));
            Assert.Equal(new[] { 3, 2, 1 }, placings.Select(p => p.Lives));
        }

        [Fact]
        public void EliminatingIgnoresLivesAndUnknownPlayers()
        {
            var tracker = new EliminationTracker(new[] { 0, 1 }, 3);
            Assert.True(tracker.Eliminate(0, 50));
            Assert.False(tracker.Eliminate(0, 60));
            Assert.False(tracker.Eliminate(7, 60));
            Assert.Equal(1, tracker.Remaining);
        }

        [Fact]
        public void AloneTheEventLastsUntilYouAreOut()
        {
            var tracker = new EliminationTracker(new[] { 0 });
            Assert.False(tracker.IsOver);
            tracker.Eliminate(0, 1000);
            Assert.True(tracker.IsOver);
        }
    }

    public class ArenaLayoutTests
    {
        [Fact]
        public void StartSlotsRingTheCentreFacingIn()
        {
            var centre = new Vec3(5f, 2f, -3f);
            var slots = ArenaLayout.StartSlots(centre, 30f, 6);
            Assert.Equal(6, slots.Count);
            foreach (var slot in slots)
            {
                Assert.Equal(30f * ArenaLayout.StartRingFraction, (slot.Position - centre).Flattened.Length, 3);
                var toCentre = (centre - slot.Position).Flattened.Normalized;
                Assert.True(Vec3.Dot(toCentre, slot.Forward) > 0.999f);
            }

            Assert.True((slots[0].Position - slots[1].Position).Length > 10f, "spread out");
        }

        [Fact]
        public void BattleBoxesSitInsideTheBowl()
        {
            var boxes = ArenaLayout.BattleItemBoxes(Vec3.Zero, BattleRules.BowlRadius);
            Assert.Equal(12, boxes.Count);
            Assert.All(boxes, b => Assert.True(b.Flattened.Length < BattleRules.BowlRadius - 5f));
        }

        [Fact]
        public void EachPlayerHasTheirOwnStartSlot()
        {
            foreach (var mode in new[] { GameMode.Sumo, GameMode.Battle })
            {
                var centre = ArenaLayout.CentreFor(mode);
                for (int a = 0; a < LobbyRoster.MaxPlayers; a++)
                {
                    var slot = ArenaLayout.StartSlotFor(mode, a);
                    Assert.Equal(centre.Y, slot.Position.Y, 3);
                    Assert.True((slot.Position - centre).Flattened.Length < ArenaLayout.StartRadiusFor(mode));
                    for (int b = a + 1; b < LobbyRoster.MaxPlayers; b++)
                    {
                        Assert.True((slot.Position - ArenaLayout.StartSlotFor(mode, b).Position).Length > 5f, $"{mode} slots {a} and {b}");
                    }
                }
            }
        }
    }

    public class ArenaStandingsTests
    {
        [Fact]
        public void OnlySumoAndBattleCarsWithAPlaceAreOut()
        {
            var placed = new StandingsEntry { FinishPosition = 2 };
            Assert.True(placed.IsOut(GameMode.Sumo));
            Assert.True(placed.IsOut(GameMode.Battle));
            Assert.False(placed.IsOut(GameMode.Race)); // finished, not out
            Assert.False(placed.IsOut(GameMode.Monster)); // a place there must never lock a truck's controls
            Assert.False(new StandingsEntry().IsOut(GameMode.Battle));
        }

        [Fact]
        public void BattleLivesTravelInLapsCompleted()
        {
            Assert.Equal(2, new StandingsEntry { LapsCompleted = 2 }.Lives);
        }

        [Fact]
        public void TimedEventsCountDownToZero()
        {
            Assert.Equal(BattleRules.TimeLimitMs, EventClock.TimeLeftMs(GameMode.Battle, -5_000));
            Assert.Equal(MonsterRules.TimeLimitMs - 30_000, EventClock.TimeLeftMs(GameMode.Monster, 30_000));
            Assert.Equal(0, EventClock.TimeLeftMs(GameMode.Battle, BattleRules.TimeLimitMs + 1));
            Assert.Equal(0, EventClock.TimeLimitMs(GameMode.Race));
            Assert.Equal(0, EventClock.TimeLimitMs(GameMode.Sumo));
        }
    }
}
