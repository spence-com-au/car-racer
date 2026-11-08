using System;
using CarRacer.Core.Arena;
using CarRacer.Core.Bots;
using CarRacer.Core.Session;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class LobbySettingsTests
    {
        [Fact]
        public void TheModeButtonStepsThroughEveryModeAndWraps()
        {
            var mode = GameMode.Race;
            foreach (GameMode expected in Enum.GetValues(typeof(GameMode)))
            {
                Assert.Equal(expected, mode);
                mode = LobbySettings.NextMode(mode);
            }

            Assert.Equal(GameMode.Race, mode);
        }

        [Fact]
        public void TheBotButtonStepsThroughEveryDifficultyAndWraps()
        {
            var difficulty = BotDifficulty.Easy;
            foreach (BotDifficulty expected in Enum.GetValues(typeof(BotDifficulty)))
            {
                Assert.Equal(expected, difficulty);
                difficulty = LobbySettings.NextDifficulty(difficulty);
            }

            Assert.Equal(BotDifficulty.Easy, difficulty);
        }
    }
}
