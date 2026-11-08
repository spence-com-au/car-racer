using System;
using System.Collections.Generic;
using CarRacer.Core.Bots;
using CarRacer.Core.Driving;
using CarRacer.Core.Items;
using CarRacer.Core.Net;
using CarRacer.Core.Numerics;
using CarRacer.Core.Track;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class BotDriverTests
    {
        private const float Dt = 0.02f;
        private static readonly TrackLayout Oval = new TrackLayout(SampleTracks.Oval());
        private static readonly CarTuning Tuning = new CarTuning();

        private sealed class Lap
        {
            public float Seconds = float.PositiveInfinity;
            public float WorstEdgeMargin = float.PositiveInfinity;
        }

        /// <summary>
        /// Drives a bot from the start line with the real car model (no walls to lean on) until it
        /// has gone once round the oval, recording how close it came to the edge of the road.
        /// </summary>
        private static Lap DriveALap(BotDifficulty difficulty, int seed = 1)
        {
            var driver = new BotDriver(Oval.Spline, BotSkill.For(difficulty), Tuning, seed);
            var start = Oval.Spline.SampleAtDistance(0f);
            var state = new CarState { Heading = MathF.Atan2(start.Forward.X, start.Forward.Z) };
            var position = start.Position;
            var lap = new Lap();
            float travelled = 0f;
            float lastDistance = 0f;
            for (float t = 0f; t < 120f; t += Dt)
            {
                var input = driver.Drive(state, position, Dt, null);
                state = CarModel.Step(state, input, grounded: true, Dt, Tuning);
                position += state.Velocity * Dt;

                float distance = driver.TrackDistance;
                float step = distance - lastDistance;
                if (step < -Oval.Spline.Length / 2f)
                {
                    step += Oval.Spline.Length;
                }

                travelled += step;
                lastDistance = distance;

                var sample = Oval.Spline.SampleAtDistance(distance);
                float lateral = Math.Abs(Vec3.Dot((position - sample.Position).Flattened, sample.Right));
                lap.WorstEdgeMargin = Math.Min(lap.WorstEdgeMargin, sample.Width / 2f - lateral);

                if (travelled >= Oval.Spline.Length)
                {
                    lap.Seconds = t;
                    return lap;
                }
            }

            return lap;
        }

        [Theory]
        [InlineData(BotDifficulty.Easy)]
        [InlineData(BotDifficulty.Normal)]
        [InlineData(BotDifficulty.Hard)]
        public void ABotGoesRoundTheOvalWithoutLeavingTheRoad(BotDifficulty difficulty)
        {
            var lap = DriveALap(difficulty);
            Assert.True(lap.Seconds < 40f, $"lap took {lap.Seconds} s");
            Assert.True(lap.WorstEdgeMargin > 0f, $"went {-lap.WorstEdgeMargin:0.0} m off the road");
        }

        [Fact]
        public void HarderBotsAreFaster()
        {
            float easy = DriveALap(BotDifficulty.Easy).Seconds;
            float normal = DriveALap(BotDifficulty.Normal).Seconds;
            float hard = DriveALap(BotDifficulty.Hard).Seconds;
            Assert.True(hard < normal && normal < easy, $"easy {easy:0.0} s, normal {normal:0.0} s, hard {hard:0.0} s");
        }

        [Fact]
        public void DifferentSeedsDriveDifferentLines()
        {
            Assert.NotEqual(DriveALap(BotDifficulty.Easy, seed: 1).WorstEdgeMargin, DriveALap(BotDifficulty.Easy, seed: 7).WorstEdgeMargin);
        }

        [Fact]
        public void ABotRamsARivalCloseInFront()
        {
            var start = Oval.Spline.SampleAtDistance(10f);
            var heading = MathF.Atan2(start.Forward.X, start.Forward.Z);
            var state = new CarState { Heading = heading, Velocity = start.Forward * 15f };
            var rival = start.Position + start.Forward * 6f + start.Right * 2.5f;

            var alone = new BotDriver(Oval.Spline, BotSkill.For(BotDifficulty.Hard), Tuning, 1).Drive(state, start.Position, Dt, null);
            var withRival = new BotDriver(Oval.Spline, BotSkill.For(BotDifficulty.Hard), Tuning, 1).Drive(state, start.Position, Dt, new List<Vec3> { rival });
            Assert.True(withRival.Steer > alone.Steer + 0.3f, $"steer {withRival.Steer} with the rival on the right, {alone.Steer} without");
        }

        [Fact]
        public void ABotIgnoresRivalsBehindOrFarAway()
        {
            var start = Oval.Spline.SampleAtDistance(10f);
            var heading = MathF.Atan2(start.Forward.X, start.Forward.Z);
            var state = new CarState { Heading = heading, Velocity = start.Forward * 15f };
            var rivals = new List<Vec3> { start.Position - start.Forward * 4f, start.Position + start.Forward * 30f + start.Right * 5f };

            var alone = new BotDriver(Oval.Spline, BotSkill.For(BotDifficulty.Hard), Tuning, 1).Drive(state, start.Position, Dt, null);
            var withRivals = new BotDriver(Oval.Spline, BotSkill.For(BotDifficulty.Hard), Tuning, 1).Drive(state, start.Position, Dt, rivals);
            Assert.Equal(alone.Steer, withRivals.Steer, 3);
        }

        [Fact]
        public void ABotSlowsForTheBendAhead()
        {
            // 20 m/s is under Easy's straight-line target (0.82 of top speed, 21.3 m/s), so the bot only
            // lifts for the hairpin if it looks ahead: the straight is the control.
            var canyon = new TrackLayout(SampleTracks.Canyon());
            float hairpin = canyon.Spline.FindNearestDistance(new Vec3(15f, 0f, 130f));

            float onTheStraight = EasyThrottleAt(canyon, 10f, 20f);
            float atTheHairpin = EasyThrottleAt(canyon, hairpin, 20f);

            Assert.Equal(1f, onTheStraight);
            Assert.True(atTheHairpin < onTheStraight, $"throttle {atTheHairpin} at the hairpin, {onTheStraight} on the straight");
        }

        private static float EasyThrottleAt(TrackLayout track, float distance, float speed)
        {
            var sample = track.Spline.SampleAtDistance(distance);
            var state = new CarState { Heading = MathF.Atan2(sample.Forward.X, sample.Forward.Z), Velocity = sample.Forward * speed };
            return new BotDriver(track.Spline, BotSkill.For(BotDifficulty.Easy), Tuning, 1).Drive(state, sample.Position, Dt, null).Throttle;
        }
    }

    public class BotItemsTests
    {
        private static readonly BotSkill Normal = BotSkill.For(BotDifficulty.Normal);

        private static BotItemSituation Nobody => new BotItemSituation
        {
            NearestAhead = float.PositiveInfinity,
            NearestBehind = float.PositiveInfinity,
        };

        [Fact]
        public void NothingIsUsedBeforeTheSkillDelay()
        {
            Assert.False(BotItems.ShouldUse(ItemKind.Shield, Normal.ItemDelaySeconds - 0.1f, Normal, Nobody));
            Assert.True(BotItems.ShouldUse(ItemKind.Shield, Normal.ItemDelaySeconds, Normal, Nobody));
            Assert.False(BotItems.ShouldUse(ItemKind.None, 100f, Normal, Nobody));
        }

        [Fact]
        public void EachItemWaitsForItsMoment()
        {
            float t = Normal.ItemDelaySeconds + 0.1f;
            var straight = Nobody;
            straight.OnStraight = true;
            Assert.False(BotItems.ShouldUse(ItemKind.Boost, t, Normal, Nobody));
            Assert.True(BotItems.ShouldUse(ItemKind.Boost, t, Normal, straight));

            var close = Nobody;
            close.NearestBehind = BotItems.ShockwaveRange - 1f;
            Assert.False(BotItems.ShouldUse(ItemKind.Shockwave, t, Normal, Nobody));
            Assert.True(BotItems.ShouldUse(ItemKind.Shockwave, t, Normal, close));
            Assert.True(BotItems.ShouldUse(ItemKind.OilSlick, t, Normal, close));
            Assert.False(BotItems.ShouldUse(ItemKind.Bomb, t, Normal, close));

            var chasing = Nobody;
            chasing.NearestAhead = 20f;
            chasing.HasCarAhead = true;
            Assert.True(BotItems.ShouldUse(ItemKind.Bomb, t, Normal, chasing));
            Assert.True(BotItems.ShouldUse(ItemKind.Missile, t, Normal, chasing));
            chasing.HasCarAhead = false; // leading: a Missile would fly off straight
            Assert.False(BotItems.ShouldUse(ItemKind.Missile, t, Normal, chasing));
        }

        [Fact]
        public void ObserveSplitsRivalsAheadAndBehindOnTheirOffsetAlongTheHeading()
        {
            var forward = new Vec3(0f, 0f, 1f);
            var rivals = new List<Vec3> { new Vec3(0f, 0f, 30f), new Vec3(0f, 0f, 12f), new Vec3(0f, 0f, -5f), new Vec3(0f, 0f, -9f) };

            var situation = BotItems.Observe(Vec3.Zero, forward, rivals, 0f, null, 1);

            Assert.Equal(12f, situation.NearestAhead);
            Assert.Equal(5f, situation.NearestBehind);
        }

        [Fact]
        public void ObserveCountsARivalBesideTheBotAsAhead()
        {
            var situation = BotItems.Observe(Vec3.Zero, new Vec3(0f, 0f, 1f), new List<Vec3> { new Vec3(4f, 0f, 0f) }, 0f, null, 1);

            Assert.Equal(4f, situation.NearestAhead);
            Assert.True(float.IsPositiveInfinity(situation.NearestBehind));
        }

        [Fact]
        public void ObserveIgnoresHeightWhenMeasuringDistance()
        {
            var situation = BotItems.Observe(Vec3.Zero, new Vec3(0f, 0f, 1f), new List<Vec3> { new Vec3(0f, 50f, 3f) }, 0f, null, 1);

            Assert.Equal(3f, situation.NearestAhead);
        }

        [Fact]
        public void ObserveSeesNobodyWithoutRivals()
        {
            var situation = BotItems.Observe(Vec3.Zero, new Vec3(0f, 0f, 1f), new List<Vec3>(), 0f, null, 1);

            Assert.True(float.IsPositiveInfinity(situation.NearestAhead));
            Assert.True(float.IsPositiveInfinity(situation.NearestBehind));
        }

        [Fact]
        public void ObserveTreatsGentleCurvatureAsAStraight()
        {
            var none = new List<Vec3>();

            Assert.True(BotItems.Observe(Vec3.Zero, Vec3.Zero, none, 0f, null, 1).OnStraight);
            Assert.True(BotItems.Observe(Vec3.Zero, Vec3.Zero, none, BotItems.StraightCurvature - 0.001f, null, 1).OnStraight);
            Assert.False(BotItems.Observe(Vec3.Zero, Vec3.Zero, none, BotItems.StraightCurvature, null, 1).OnStraight);
        }

        [Fact]
        public void ObserveHasACarAheadUnlessTheBotLeadsOrNoStandingsExist()
        {
            var none = new List<Vec3>();
            var standings = new StandingsMessage();
            standings.Order.Add(new StandingsEntry { PlayerId = 7 });
            standings.Order.Add(new StandingsEntry { PlayerId = 1 });

            Assert.True(BotItems.Observe(Vec3.Zero, Vec3.Zero, none, 0f, standings, 1).HasCarAhead);
            Assert.False(BotItems.Observe(Vec3.Zero, Vec3.Zero, none, 0f, standings, 7).HasCarAhead);
            Assert.False(BotItems.Observe(Vec3.Zero, Vec3.Zero, none, 0f, new StandingsMessage(), 1).HasCarAhead);
            Assert.False(BotItems.Observe(Vec3.Zero, Vec3.Zero, none, 0f, null, 1).HasCarAhead);
        }

        [Fact]
        public void TheNewItemsWaitForTheirMomentsToo()
        {
            float t = Normal.ItemDelaySeconds + 0.1f;
            var behind = Nobody;
            behind.NearestBehind = BotItems.OilRange - 1f;
            Assert.False(BotItems.ShouldUse(ItemKind.FakeBox, t, Normal, Nobody));
            Assert.True(BotItems.ShouldUse(ItemKind.FakeBox, t, Normal, behind));

            // Lightning and a Seeker reach the cars ahead wherever they are, so any car ahead will do.
            var trailing = Nobody;
            trailing.HasCarAhead = true;
            foreach (var item in new[] { ItemKind.Lightning, ItemKind.Seeker })
            {
                Assert.False(BotItems.ShouldUse(item, t, Normal, Nobody), item.ToString());
                Assert.True(BotItems.ShouldUse(item, t, Normal, trailing), item.ToString());
            }
        }

        [Fact]
        public void OnlyCarsStillRacingCountAsAhead()
        {
            var standings = new StandingsMessage();
            standings.Order.Add(new StandingsEntry { PlayerId = 1, FinishPosition = 1 });
            standings.Order.Add(new StandingsEntry { PlayerId = 2 });
            standings.Order.Add(new StandingsEntry { PlayerId = 3 });

            // 2nd behind a winner who has finished: nothing left for Lightning or a Seeker to go after.
            Assert.Empty(standings.RacingAhead(2));

            // 3rd behind a 2nd still racing: that car, but not the winner.
            Assert.Equal(new byte[] { 2 }, standings.RacingAhead(3));
        }

        [Fact]
        public void HasRacingAheadSkipsCarsThatHaveFinished()
        {
            var standings = new StandingsMessage();
            standings.Order.Add(new StandingsEntry { PlayerId = 1, FinishPosition = 1 });
            standings.Order.Add(new StandingsEntry { PlayerId = 2 });
            standings.Order.Add(new StandingsEntry { PlayerId = 3 });

            Assert.False(standings.HasRacingAhead(1)); // the winner: nobody ahead at all
            Assert.False(standings.HasRacingAhead(2)); // only the finished winner ahead
            Assert.True(standings.HasRacingAhead(3)); // a 2nd still racing ahead
        }

        [Fact]
        public void AnItemHeldTooLongIsUsedAnyway()
        {
            Assert.True(BotItems.ShouldUse(ItemKind.Missile, BotItems.GiveUpSeconds, Normal, Nobody));
        }

        [Fact]
        public void HarderBotsAreQuickerWithItems()
        {
            Assert.True(BotSkill.For(BotDifficulty.Hard).ItemDelaySeconds < Normal.ItemDelaySeconds);
            Assert.True(Normal.ItemDelaySeconds < BotSkill.For(BotDifficulty.Easy).ItemDelaySeconds);
        }
    }
}
