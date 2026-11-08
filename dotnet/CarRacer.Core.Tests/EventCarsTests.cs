using CarRacer.Core.Arena;
using CarRacer.Core.Race;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class EventCarsTests
    {
        [Theory]
        [InlineData(RacePhase.Lobby, RacePhase.Countdown, true)]
        [InlineData(RacePhase.Results, RacePhase.Countdown, true)]
        [InlineData(RacePhase.Countdown, RacePhase.Racing, false)]
        [InlineData(RacePhase.Lobby, RacePhase.Racing, true)]
        [InlineData(RacePhase.Racing, RacePhase.Results, false)]
        [InlineData(RacePhase.Results, RacePhase.Lobby, false)]
        [InlineData(RacePhase.Countdown, RacePhase.Lobby, false)]
        public void CarsLineUpAtTheCountdownOrAtAGoThatCameWithoutOne(RacePhase previous, RacePhase phase, bool expected)
        {
            Assert.Equal(expected, EventCars.LinesUp(previous, phase));
        }

        [Theory]
        [InlineData(GameMode.Monster, RacePhase.Countdown, true)]
        [InlineData(GameMode.Monster, RacePhase.Racing, true)]
        [InlineData(GameMode.Monster, RacePhase.Results, true)]
        [InlineData(GameMode.Monster, RacePhase.Lobby, false)]
        [InlineData(GameMode.Race, RacePhase.Racing, false)]
        [InlineData(GameMode.Sumo, RacePhase.Racing, false)]
        [InlineData(GameMode.Battle, RacePhase.Racing, false)]
        public void CarsAreTrucksThroughMonsterJamButNotInItsLobby(GameMode mode, RacePhase phase, bool expected)
        {
            Assert.Equal(expected, EventCars.AreTrucks(mode, phase));
        }

        [Fact]
        public void TrucksBumpHarderThanCars()
        {
            Assert.Equal(MonsterRules.BumpExaggeration, EventCars.BumpExaggeration(true, 1.4f));
            Assert.Equal(1.4f, EventCars.BumpExaggeration(false, 1.4f));
        }

        [Fact]
        public void ASpinOutStartsOnceOnItsRisingEdge()
        {
            bool wasSpunOut = false;
            Assert.False(EventCars.SpinOutStarted(false, ref wasSpunOut));
            Assert.True(EventCars.SpinOutStarted(true, ref wasSpunOut));
            Assert.False(EventCars.SpinOutStarted(true, ref wasSpunOut), "still spinning from the same hit");
            Assert.False(EventCars.SpinOutStarted(false, ref wasSpunOut));
            Assert.True(EventCars.SpinOutStarted(true, ref wasSpunOut), "a second spin-out counts again");
        }
    }
}
