using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CarRacer.Core.Arena;
using CarRacer.Core.Art;
using CarRacer.Core.Bots;
using CarRacer.Core.Driving;
using CarRacer.Core.Net;
using CarRacer.Core.Numerics;
using CarRacer.Core.Race;
using CarRacer.Core.Session;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class MonsterJamTests
    {
        private const ushort Version = 25;

        private long _now = 10_000;
        private readonly HostSession _host;
        private readonly HostRace _race;
        private readonly GuestSession _guest;
        private readonly GuestRace _guestRace;
        private readonly Dictionary<byte, Vec3> _cars = new Dictionary<byte, Vec3>();

        public MonsterJamTests()
        {
            var hostLink = new HostLink();
            _host = new HostSession(hostLink, "Host", Version, () => _now);
            _race = new HostRace(_host, new[] { 4 }, () => _now, id => 0f, _cars.TryGetValue);
            _guest = hostLink.Join(_host, "Guest", Version, () => _now);
            _guestRace = new GuestRace(_guest, () => _now);
        }

        private byte HostId => _host.HostPlayerId;
        private byte GuestId => _guest.PlayerId;

        private void Start()
        {
            Assert.True(_race.Start(1, GameMode.Monster));
            _now += HostRace.StartLeadMs + HostRace.CountdownMs;
            Tick();
            Assert.Equal(RacePhase.Racing, _race.Phase);
        }

        private void Tick()
        {
            _race.Tick();
            _guestRace.Tick();
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(399, 0)]
        [InlineData(400, 4)]
        [InlineData(1250, 12)]
        [InlineData(60_000, 60)]
        public void LongerJumpsScoreMoreAndShortHopsNothing(long airMs, int points)
        {
            Assert.Equal(points, MonsterRules.AirPoints(airMs));
        }

        [Fact]
        public void TheTruckDoingTheHittingOrLandingIsTheOneThatCrushes()
        {
            var a = new Vec3(0f, 0f, 0f);
            var b = new Vec3(0f, 0f, 2f);

            // A charges into B, which is barely moving: A's crush, not B's.
            Assert.True(MonsterRules.IsCrush(a, new Vec3(0f, 0f, 10f), b, new Vec3(0f, 0f, 1f)));
            Assert.False(MonsterRules.IsCrush(b, new Vec3(0f, 0f, 1f), a, new Vec3(0f, 0f, 10f)));

            // A gentle nudge is nobody's crush.
            Assert.False(MonsterRules.IsCrush(a, new Vec3(0f, 0f, 5f), b, Vec3.Zero));

            // Coming down on top counts at any speed; the truck underneath didn't crush anyone.
            var above = new Vec3(0f, 1f, 0.5f);
            Assert.True(MonsterRules.IsCrush(above, new Vec3(0f, -3f, 1f), a, Vec3.Zero));
            Assert.False(MonsterRules.IsCrush(a, Vec3.Zero, above, new Vec3(0f, -3f, 1f)));
        }

        [Fact]
        public void TheHostOnlyBelievesAirTimeThatFitsTheClock()
        {
            var stunts = new StuntTracker(new[] { 1, 2 });
            Assert.Equal(10, stunts.AddAir(1, 1000, 5_000));

            // 1.5 s since the last jump landed can't hold a 2 s jump.
            Assert.Equal(0, stunts.AddAir(1, 2000, 6_500));
            Assert.Equal(15, stunts.AddAir(1, 1500, 6_500));
            Assert.Equal(0, stunts.AddAir(9, 1000, 20_000));
            Assert.Equal(25, stunts.Points(1));
        }

        [Fact]
        public void ACrushNeedsTheTrucksCloseAndNotTheSameOneAgainStraightAway()
        {
            var stunts = new StuntTracker(new[] { 1, 2, 3 });
            Assert.Equal(0, stunts.AddCrush(1, 2, 1_000, MonsterRules.CrushReach + 1f));
            Assert.Equal(0, stunts.AddCrush(1, 1, 1_000, 1f));
            Assert.Equal(MonsterRules.CrushPoints, stunts.AddCrush(1, 2, 1_000, 2f));
            Assert.Equal(0, stunts.AddCrush(1, 2, 1_000 + MonsterRules.CrushCooldownMs - 1, 2f));
            Assert.Equal(MonsterRules.CrushPoints, stunts.AddCrush(1, 3, 1_200, 2f));
            Assert.Equal(MonsterRules.CrushPoints, stunts.AddCrush(1, 2, 1_000 + MonsterRules.CrushCooldownMs, 2f));
            Assert.Equal(3 * MonsterRules.CrushPoints, stunts.Points(1));
        }

        [Fact]
        public void PlacingsAreByPointsWithTiesInJoinOrder()
        {
            var stunts = new StuntTracker(new[] { 4, 1, 2 });
            stunts.AddAir(2, 1000, 5_000);
            var placings = stunts.Placings();
            Assert.Equal(new[] { 2, 4, 1 }, placings.Select(p => p.PlayerId));
            Assert.Equal(new[] { 1, 2, 3 }, placings.Select(p => p.Position));
            Assert.Equal(10, placings[0].Points);
        }

        [Fact]
        public void AGuestsStuntsAreScoredByTheHostAndTheMostPointsWin()
        {
            Start();
            Assert.Equal(GameMode.Monster, _guestRace.Mode);
            Assert.NotNull(_race.Stunts);
            Assert.Null(_race.Arena);
            Assert.Null(_race.Tracker);

            _now += 3_000;
            _guestRace.ReportOwnStunt(StuntKind.Air, 1_500, 0);
            Assert.Equal(15, _race.Stunts.Points(GuestId));

            // A crush counts only where the host sees the trucks together.
            _guestRace.ReportOwnStunt(StuntKind.Crush, 0, HostId);
            Assert.Equal(15, _race.Stunts.Points(GuestId));
            _cars[GuestId] = new Vec3(600f, 0f, -380f);
            _cars[HostId] = new Vec3(602f, 0f, -380f);
            _guestRace.ReportOwnStunt(StuntKind.Crush, 0, HostId);
            Assert.Equal(15 + MonsterRules.CrushPoints, _race.Stunts.Points(GuestId));

            _race.ReportOwnStunt(StuntKind.Air, 800, 0);
            Assert.Equal(8, _race.Stunts.Points(HostId));
            Assert.Equal(GuestId, _guestRace.Standings.Order[0].PlayerId);
            Assert.Equal(15 + MonsterRules.CrushPoints, _guestRace.Standings.Order[0].Points);

            _now += MonsterRules.TimeLimitMs;
            Tick();
            Assert.Equal(RacePhase.Results, _guestRace.Phase);
            var results = _guestRace.Results.Results;
            Assert.Equal(new[] { GuestId, HostId }, results.Select(r => r.PlayerId));
            Assert.Equal(15 + MonsterRules.CrushPoints, results[0].Points);
            Assert.Equal(8, results[1].Points);

            // Stunts after the whistle don't count.
            _guestRace.ReportOwnStunt(StuntKind.Air, 1_000, 0);
            Assert.Equal(15 + MonsterRules.CrushPoints, _race.Stunts.Points(GuestId));
            Assert.True(_race.ReturnToLobby());
            Assert.Null(_race.Stunts);
        }

        [Fact]
        public void StuntsOutsideMonsterJamAreIgnored()
        {
            Assert.True(_race.Start(1, GameMode.Battle));
            _now += HostRace.StartLeadMs + HostRace.CountdownMs + 2_000;
            Tick();
            _guestRace.ReportOwnStunt(StuntKind.Air, 1_000, 0);
            _race.ReportOwnStunt(StuntKind.Air, 1_000, 0);
            Assert.Null(_race.Stunts);
        }

        [Fact]
        public void StuntsAndPointsTravelOnTheWire()
        {
            var stunt = Assert.IsType<StuntMessage>(MessageCodec.Decode(MessageCodec.Encode(new StuntMessage { PlayerId = 3, Kind = StuntKind.Crush, AirMs = 1234, VictimId = 5 })));
            Assert.Equal(3, stunt.PlayerId);
            Assert.Equal(StuntKind.Crush, stunt.Kind);
            Assert.Equal(1234, stunt.AirMs);
            Assert.Equal(5, stunt.VictimId);

            byte[] bad = MessageCodec.Encode(new StuntMessage { Kind = StuntKind.Air });
            bad[2] = 9;
            Assert.Throws<NetFormatException>(() => MessageCodec.Decode(bad));

            var standings = new StandingsMessage();
            standings.Order.Add(new StandingsEntry { PlayerId = 2, Points = 777 });
            Assert.Equal(777, Assert.IsType<StandingsMessage>(MessageCodec.Decode(MessageCodec.Encode(standings))).Order[0].Points);

            var results = new RaceResultsMessage();
            results.Results.Add(new RaceResultEntry { PlayerId = 1, Name = "A", Position = 1, Points = 300 });
            Assert.Equal(300, Assert.IsType<RaceResultsMessage>(MessageCodec.Decode(MessageCodec.Encode(results))).Results[0].Points);
        }

        [Fact]
        public void TheLobbyCanPickMonsterJam()
        {
            var settings = LobbySettings.Default;
            settings.Mode = GameMode.Monster;
            Assert.Equal(GameMode.Monster, settings.Clean().Mode);
            settings.Mode = (GameMode)4;
            Assert.Equal(GameMode.Race, settings.Clean().Mode);
        }

        [Fact]
        public void TheRampsSitInsideTheStadiumClearOfTheStartingSpots()
        {
            var centre = ArenaLayout.MonsterCentre;
            var ramps = ArenaLayout.MonsterRamps(centre);
            Assert.Equal(6, ramps.Count);
            foreach (var ramp in ramps)
            {
                foreach (var end in new[] { ramp.Foot, ramp.Lip })
                {
                    Assert.True((end - centre).Flattened.Length + ramp.Width / 2f < MonsterRules.StadiumRadius - 5f);
                }

                Assert.Equal(ramp.Height, ramp.Lip.Y - ramp.Foot.Y, 3);
                Assert.True(ramp.Height / ramp.Length < 0.45f, "steep enough to launch, not a wall");
            }

            for (int slot = 0; slot < LobbyRoster.MaxPlayers; slot++)
            {
                var start = ArenaLayout.StartSlotFor(GameMode.Monster, slot).Position;
                Assert.All(ramps, r => Assert.True(Distance2D(start, r.Foot, r.Lip) > r.Width / 2f + 2f));
            }
        }

        [Fact]
        public void ATruckIsTheCarRaisedOnBigWheels()
        {
            var car = ObjModel.Parse(File.ReadAllText(ModelFiles.Resolve("sedan.obj.bytes")));
            var truck = MonsterTruck.Build(car);
            Assert.Equal(car.Parts.Count, truck.Parts.Count);
            for (int i = 0; i < car.Parts.Count; i++)
            {
                Assert.Equal(car.Parts[i].Uvs, truck.Parts[i].Uvs);
                Assert.Equal(car.Parts[i].Triangles, truck.Parts[i].Triangles);
            }

            var wheel = car.Parts.First(MonsterTruck.IsRoadWheel);
            var bigWheel = truck.Parts.First(MonsterTruck.IsRoadWheel);
            float Height(ObjPart p) => p.Positions.Max(v => v.Y) - p.Positions.Min(v => v.Y);
            Assert.Equal(Height(wheel) * MonsterTruck.WheelScale, Height(bigWheel), 3);

            // The wheels still stand on the ground, and the body clears their tops' old height.
            Assert.Equal(car.Min.Y, truck.Min.Y, 2);
            var body = truck.Parts.Where(p => !MonsterTruck.IsRoadWheel(p)).SelectMany(p => p.Positions).Min(v => v.Y);
            Assert.True(body > wheel.Positions.Max(v => v.Y), "the body rides above where the car's wheels were");
            Assert.True(truck.Max.X - truck.Min.X > car.Max.X - car.Min.X, "wider stance");
        }

        [Fact]
        public void EveryGarageBodyMakesATruck()
        {
            foreach (var body in CarModels.Bodies)
            {
                var truck = MonsterTruck.Build(ObjModel.Parse(File.ReadAllText(ModelFiles.Resolve(body.Name + ".obj.bytes"))));
                Assert.Equal(4, truck.Parts.Count(MonsterTruck.IsRoadWheel));
                Assert.True(truck.Max.Y > truck.Min.Y);

                // The grown wheels may meet between the axles but not cut through each other.
                foreach (string side in new[] { "left", "right" })
                {
                    float frontWheelBack = truck.Parts.Single(p => p.Name == "wheel-front-" + side).Positions.Min(v => v.Z);
                    float backWheelFront = truck.Parts.Single(p => p.Name == "wheel-back-" + side).Positions.Max(v => v.Z);
                    Assert.True(frontWheelBack >= backWheelFront - 0.001f, $"{body.Name}'s {side} wheels overlap");
                }
            }
        }

        [Fact]
        public void BotsWorkRoundTheRampsAndStayOffTheWall()
        {
            var centre = ArenaLayout.MonsterCentre;
            var ramps = ArenaLayout.MonsterRamps(centre);
            var tuning = new CarTuning();
            var driver = new MonsterBotDriver(BotSkill.For(BotDifficulty.Normal), tuning, ramps, centre, MonsterRules.StadiumRadius, 1);
            var start = ArenaLayout.StartSlotFor(GameMode.Monster, 1);
            var state = new CarState { Heading = MathF.Atan2(start.Forward.X, start.Forward.Z) };
            var position = start.Position;
            var visited = new HashSet<int>();
            int launches = 0;
            bool wasLaunching = false;
            for (float t = 0f; t < 40f; t += 0.02f)
            {
                state = CarRacer.Core.Driving.CarModel.Step(state, driver.Drive(state, position, null), grounded: true, 0.02f, tuning);
                position += state.Velocity * 0.02f;
                visited.Add(driver.CurrentRamp);
                if (driver.Launching && !wasLaunching)
                {
                    launches++;
                }

                wasLaunching = driver.Launching;
                Assert.True((position - centre).Flattened.Length < MonsterRules.StadiumRadius, $"hit the wall at {t:0.0} s");
            }

            Assert.True(launches >= 3, $"only {launches} launches");
            Assert.True(visited.Count >= 3);
        }

        [Fact]
        public void ABotRamsARivalRightInFront()
        {
            var centre = ArenaLayout.MonsterCentre;
            var driver = new MonsterBotDriver(BotSkill.For(BotDifficulty.Hard), new CarTuning(), ArenaLayout.MonsterRamps(centre), centre, MonsterRules.StadiumRadius, 0);
            var state = new CarState { Heading = 0f };
            var rival = centre + new Vec3(3f, 0f, 8f);
            var input = driver.Drive(state, centre, new[] { rival });
            Assert.True(input.Steer > 0f, "turns towards the rival on its right");
            Assert.Equal(1f, input.Throttle);
        }

        private static float Distance2D(Vec3 p, Vec3 a, Vec3 b)
        {
            var ab = (b - a).Flattened;
            var ap = (p - a).Flattened;
            float t = Math.Max(0f, Math.Min(1f, Vec3.Dot(ap, ab) / ab.LengthSquared));
            return (ap - ab * t).Length;
        }
    }
}
