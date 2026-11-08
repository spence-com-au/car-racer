using System.Collections.Generic;
using System.Linq;
using CarRacer.Core.Arena;
using CarRacer.Core.Net;
using CarRacer.Core.Numerics;
using CarRacer.Core.Race;
using CarRacer.Core.Session;
using CarRacer.Core.Track;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class TrackCatalogPlacementTests
    {
        [Fact]
        public void TheCatalogHoldsEveryBuiltInTrackInOrder()
        {
            var all = SampleTracks.All();
            Assert.Equal(all.Length, TrackCatalog.Count);
            for (int i = 0; i < all.Length; i++)
            {
                Assert.Equal(all[i].Id, TrackCatalog.Layouts[i].Definition.Id);
                Assert.Equal(all[i].CheckpointCount, TrackCatalog.Layouts[i].CheckpointCount);
                Assert.Equal(new TrackLayout(all[i]).Spline.Length, TrackCatalog.Layouts[i].Spline.Length, 1);
            }
        }

        [Fact]
        public void MovingATrackMovesEveryPointAndKeepsTheRest()
        {
            var offset = new Vec3(10f, 2f, -5f);
            foreach (var original in SampleTracks.All())
            {
                var before = original.Points.Select(p => p.Position).ToArray();
                var moved = TrackCatalog.Moved(original, offset);
                Assert.Equal(before.Length, moved.Points.Length);
                for (int i = 0; i < before.Length; i++)
                {
                    Assert.Equal(before[i], original.Points[i].Position);
                    Assert.Equal(before[i] + offset, moved.Points[i].Position);
                    Assert.Equal(original.Points[i].Width, moved.Points[i].Width);
                    Assert.Equal(original.Points[i].BankDegrees, moved.Points[i].BankDegrees);
                }

                // Found by reflection, so a field added to `TrackDefinition` cannot be dropped by `Moved` unnoticed.
                foreach (var field in typeof(TrackDefinition).GetFields().Where(f => f.Name != nameof(TrackDefinition.Points)))
                {
                    Assert.Equal(field.GetValue(original), field.GetValue(moved));
                }
            }
        }

        [Fact]
        public void OutOfRangeTrackIndicesFallBackToTheFirst()
        {
            Assert.Equal(1, TrackCatalog.Clamp(1));
            Assert.Equal(0, TrackCatalog.Clamp(-1));
            Assert.Equal(0, TrackCatalog.Clamp(TrackCatalog.Count));
        }

        /// <summary>Each track's patch of ground reaches this far beyond its road.</summary>
        private const float GroundMargin = 60f;

        [Fact]
        public void TracksAndArenasKeepToTheirOwnGround()
        {
            var areas = new List<(string Name, float MinX, float MaxX, float MinZ, float MaxZ)>();
            foreach (var layout in TrackCatalog.Layouts)
            {
                var samples = layout.Spline.SampleEvenly(2f);
                areas.Add((layout.Definition.Id,
                    samples.Min(s => s.Position.X) - GroundMargin, samples.Max(s => s.Position.X) + GroundMargin,
                    samples.Min(s => s.Position.Z) - GroundMargin, samples.Max(s => s.Position.Z) + GroundMargin));
            }

            AddArena(areas, "sumo", ArenaLayout.SumoCentre, SumoRules.StartRadius + 30f);
            AddArena(areas, "battle", ArenaLayout.BattleCentre, BattleRules.BowlRadius + 10f);
            AddArena(areas, "monster", ArenaLayout.MonsterCentre, MonsterRules.StadiumRadius + 10f);
            for (int a = 0; a < areas.Count; a++)
            {
                for (int b = a + 1; b < areas.Count; b++)
                {
                    bool overlap = areas[a].MinX < areas[b].MaxX && areas[b].MinX < areas[a].MaxX
                        && areas[a].MinZ < areas[b].MaxZ && areas[b].MinZ < areas[a].MaxZ;
                    Assert.False(overlap, $"{areas[a].Name} overlaps {areas[b].Name}");
                }
            }
        }

        private static void AddArena(List<(string, float, float, float, float)> areas, string name, Vec3 centre, float reach) =>
            areas.Add((name, centre.X - reach, centre.X + reach, centre.Z - reach, centre.Z + reach));
    }

    public class TrackChoiceTests
    {
        private const ushort Version = 17;

        private readonly long _now = 10_000;
        private readonly HostSession _host;
        private readonly HostRace _race;
        private readonly GuestRace _guestRace;

        public TrackChoiceTests()
        {
            var hostLink = new HostLink();
            _host = new HostSession(hostLink, "Host", Version, () => _now);
            _race = new HostRace(_host, new[] { 4, 6, 10 }, () => _now, id => 0f);
            _guestRace = new GuestRace(hostLink.Join(_host, "Guest", Version, () => _now), () => _now);
        }

        [Fact]
        public void TheCountdownTellsGuestsTheTrack()
        {
            Assert.True(_race.Start(2, GameMode.Race, 2));
            Assert.Equal(2, _race.TrackIndex);
            Assert.Equal(2, _guestRace.TrackIndex);

            // The race is refereed with that track's checkpoints.
            Assert.Equal(10, _race.Tracker.CheckpointCount);
        }

        [Fact]
        public void ATrackThatDoesNotExistIsRefused()
        {
            Assert.False(_race.Start(2, GameMode.Race, 3));
            Assert.False(_race.Start(2, GameMode.Race, -1));
            Assert.Equal(RacePhase.Lobby, _race.Phase);
        }

        [Fact]
        public void ArenasIgnoreTheTrack()
        {
            Assert.True(_race.Start(1, GameMode.Sumo, 2));
            Assert.Equal(0, _race.TrackIndex);
            Assert.Equal(0, _guestRace.TrackIndex);
        }

        [Fact]
        public void TheTrackIndexSurvivesTheWire()
        {
            var countdown = Assert.IsType<RaceCountdownMessage>(MessageCodec.Decode(MessageCodec.Encode(new RaceCountdownMessage { LapCount = 3, Mode = GameMode.Race, TrackIndex = 2 })));
            Assert.Equal(2, countdown.TrackIndex);
            Assert.Equal(3, countdown.LapCount);
        }
    }
}
