using System;
using System.Collections.Generic;
using CarRacer.Core.Arena;
using CarRacer.Core.Bots;
using CarRacer.Core.Driving;
using CarRacer.Core.Numerics;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class ArenaBotDriverTests
    {
        private const float Dt = 0.02f;
        private static readonly CarTuning Tuning = new CarTuning();

        /// <summary>Drives a bot with the real car model (no walls) and reports how far from the centre it ever got and how close it came to <paramref name="rival"/>.</summary>
        private static (float Farthest, float Closest) Drive(GameMode mode, BotDifficulty difficulty, float radius, Vec3? rival, float seconds, Vec3 start, float heading)
        {
            var driver = new ArenaBotDriver(BotSkill.For(difficulty), Tuning, mode, 1);
            var state = new CarState { Heading = heading };
            var position = start;
            float farthest = 0f;
            float closest = float.MaxValue;
            var rivals = rival.HasValue ? new List<Vec3> { rival.Value } : new List<Vec3>();
            for (float t = 0f; t < seconds; t += Dt)
            {
                var input = driver.Drive(state, position, Vec3.Zero, radius, rivals);
                state = CarModel.Step(state, input, grounded: true, Dt, Tuning);
                position += state.Velocity * Dt;
                farthest = Math.Max(farthest, position.Flattened.Length);
                if (rival.HasValue)
                {
                    closest = Math.Min(closest, (position - rival.Value).Flattened.Length);
                }
            }

            return (farthest, closest);
        }

        [Theory]
        [InlineData(BotDifficulty.Easy)]
        [InlineData(BotDifficulty.Hard)]
        public void ALoneBotStaysOnThePlatform(BotDifficulty difficulty)
        {
            var (farthest, _) = Drive(GameMode.Sumo, difficulty, SumoRules.StartRadius, null, 30f, new Vec3(0f, 0f, 15f), MathF.PI);
            Assert.True(farthest < SumoRules.StartRadius, $"got {farthest:0.0} m out on a {SumoRules.StartRadius} m platform");
        }

        [Fact]
        public void ABotHeadingStraightForTheEdgeTurnsBack()
        {
            // Starting near the edge and pointing straight out at speed is the worst case.
            var driver = new ArenaBotDriver(BotSkill.For(BotDifficulty.Hard), Tuning, GameMode.Sumo, 1);
            var state = new CarState { Heading = 0f, Velocity = new Vec3(0f, 0f, 15f) };
            var position = new Vec3(0f, 0f, 12f);
            float farthest = 0f;
            for (float t = 0f; t < 10f; t += Dt)
            {
                state = CarModel.Step(state, driver.Drive(state, position, Vec3.Zero, 25f, null), grounded: true, Dt, Tuning);
                position += state.Velocity * Dt;
                farthest = Math.Max(farthest, position.Flattened.Length);
            }

            Assert.True(farthest < 25f, $"got {farthest:0.0} m out on a 25 m platform");
        }

        [Fact]
        public void ABotHuntsDownARival()
        {
            var rival = new Vec3(-10f, 0f, -8f);
            var (_, closest) = Drive(GameMode.Battle, BotDifficulty.Normal, BattleRules.BowlRadius, rival, 10f, new Vec3(12f, 0f, 10f), 0f);
            Assert.True(closest < 2f, $"closest was {closest:0.0} m");
        }

        [Fact]
        public void InSumoItAimsPastTheRivalsOuterSide()
        {
            // The rival sits on the line out from the centre and the bot is off to one side, so the aim point's offset changes the bearing.
            var position = new Vec3(-3f, 0f, 0f);
            var rival = new Vec3(0f, 0f, 10f);
            float Steer(GameMode mode)
            {
                var driver = new ArenaBotDriver(BotSkill.For(BotDifficulty.Normal), Tuning, mode, 1);
                return driver.Drive(new CarState { Heading = 0f }, position, Vec3.Zero, 30f, new List<Vec3> { rival }).Steer;
            }

            Assert.Equal(MathF.Atan2(3f, 10f) / BotDriver.FullLockAngle, Steer(GameMode.Battle), 3);
            Assert.Equal(MathF.Atan2(3f, 10f + ArenaBotDriver.PushOffset) / BotDriver.FullLockAngle, Steer(GameMode.Sumo), 3);
        }

        [Fact]
        public void ABotFollowsTheShrinkingPlatformIn()
        {
            var (farthest, _) = Drive(GameMode.Sumo, BotDifficulty.Normal, SumoRules.EndRadius, null, 20f, new Vec3(0f, 0f, 3f), 0f);
            Assert.True(farthest < SumoRules.EndRadius, $"got {farthest:0.0} m out on a {SumoRules.EndRadius} m platform");
        }
    }
}
