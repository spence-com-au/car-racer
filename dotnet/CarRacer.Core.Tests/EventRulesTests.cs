using CarRacer.Core.Arena;
using CarRacer.Core.Race;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class EventRulesTests
    {
        [Theory]
        [InlineData(true, true, true)]
        [InlineData(false, true, true)]
        [InlineData(true, false, true)]
        [InlineData(true, true, false)]
        [InlineData(false, false, false)]
        public void RulesSurviveTheWire(bool damage, bool items, bool carStats)
        {
            var rules = EventRules.FromByte(new EventRules(damage, items, carStats).ToByte());
            Assert.Equal(damage, rules.Damage);
            Assert.Equal(items, rules.Items);
            Assert.Equal(carStats, rules.CarStats);
        }

        [Fact]
        public void AZeroByteIsTheDefaultEverythingOn()
        {
            Assert.Equal(0, EventRules.Default.ToByte());
            var rules = EventRules.FromByte(0);
            Assert.True(rules.Damage && rules.Items && rules.CarStats);
        }

        [Fact]
        public void BattleAlwaysHasItems()
        {
            var noItems = EventRules.Default.WithItems(false);
            Assert.True(noItems.For(GameMode.Battle).Items);
            Assert.False(noItems.For(GameMode.Race).Items);
            Assert.False(noItems.For(GameMode.Sumo).Items);
            Assert.False(noItems.WithDamage(false).For(GameMode.Battle).Damage, "only items are forced");
        }

        [Theory]
        [InlineData(GameMode.Race, RacePhase.Racing, true, true)]
        [InlineData(GameMode.Race, RacePhase.Racing, false, false)]
        [InlineData(GameMode.Race, RacePhase.Lobby, true, false)]
        [InlineData(GameMode.Race, RacePhase.Countdown, true, false)]
        [InlineData(GameMode.Race, RacePhase.Results, true, false)]
        [InlineData(GameMode.Sumo, RacePhase.Racing, true, false)]
        [InlineData(GameMode.Battle, RacePhase.Racing, true, false)]
        [InlineData(GameMode.Monster, RacePhase.Racing, true, false)]
        public void DamageCountsOnlyWhileRacingARace(GameMode mode, RacePhase phase, bool damage, bool expected)
        {
            Assert.Equal(expected, EventRules.Default.WithDamage(damage).DamageIn(mode, phase));
        }

        [Theory]
        [InlineData(RacePhase.Lobby, false, true)]
        [InlineData(RacePhase.Lobby, true, true)]
        [InlineData(RacePhase.Countdown, false, false)]
        [InlineData(RacePhase.Racing, false, false)]
        [InlineData(RacePhase.Racing, true, true)]
        [InlineData(RacePhase.Results, false, false)]
        public void TheLobbyAlwaysKeepsCarStats(RacePhase phase, bool carStats, bool expected)
        {
            Assert.Equal(expected, EventRules.Default.WithCarStats(carStats).CarStatsIn(phase));
        }
    }
}
