using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using CarRacer.Core.Arena;
using CarRacer.Core.Art;
using CarRacer.Core.Net;
using CarRacer.Core.Numerics;
using CarRacer.Core.Race;
using CarRacer.Core.Session;
using CarRacer.Core.Track;
using CarRacer.Core.Viewer;
using Xunit;

namespace CarRacer.Core.Tests
{
    /// <summary>A race as the viewer sees it, set by hand.</summary>
    internal sealed class FakeRaceView : IRaceView
    {
        public RacePhase Phase { get; set; } = RacePhase.Racing;
        public int LapCount { get; set; } = 3;
        public GameMode Mode { get; set; }
        public int TrackIndex { get; set; }
        public EventRules Rules { get; set; } = EventRules.Default;
        public long StartAtLocalMs { get; set; }
        public StandingsMessage Standings { get; set; }
        public RaceResultsMessage Results { get; set; }

        public int GridSlotOf(byte playerId) => 10 - playerId;

        public void Tick()
        {
        }

        public void ReportOwnCheckpoint(int index)
        {
        }

        public void ReportOwnStunt(StuntKind kind, long airMs, byte victimId)
        {
        }
    }

    public class ViewerSceneTests
    {
        internal static readonly RosterEntry[] Players =
        {
            new RosterEntry { PlayerId = 1, Name = "Alice", ColourIndex = 0, Ready = true },
            new RosterEntry { PlayerId = 2, Name = "Bob", ColourIndex = 1 },
            new RosterEntry { PlayerId = 3, Name = "Turbo", ColourIndex = 2, IsBot = true, Ready = true },
        };

        internal static ViewerScene.Inputs Lobby() => new ViewerScene.Inputs
        {
            ServerName = "Lounge",
            Players = Players,
            LeaderId = 2,
            Settings = LobbySettings.Default,
            JoinAddress = "192.168.1.20",
            Port = 47778,
        };

        internal static ViewerScene.Inputs Racing(FakeRaceView race, long nowMs)
        {
            var inputs = Lobby();
            inputs.Race = race;
            inputs.NowMs = nowMs;
            var track = TrackCatalog.Layouts[race.TrackIndex].Spline;
            inputs.Positions = (byte id, out Vec3 position) =>
            {
                position = race.Mode == GameMode.Race
                    ? track.SampleAtDistance(id * 40f).Position
                    : ArenaLayout.CentreFor(race.Mode) + new Vec3(id * 6f - 12f, 0f, id * 3f);
                return true;
            };
            return inputs;
        }

        [Fact]
        public void LobbyListsPlayersAndHowToJoin()
        {
            var scene = ViewerScene.Build(Lobby());
            Assert.Equal(ViewerPhase.Lobby, scene.Phase);
            Assert.Equal("Lounge", scene.Title);
            Assert.Equal(new[] { "Alice", "Bob" }, scene.Rows.Select(r => r.Name));
            Assert.Equal(new[] { "Ready", "runs the room" }, scene.Rows.Select(r => r.Detail));
            Assert.Equal("2 players", scene.Clock);
            Assert.Equal("carracer://join?host=192.168.1.20&port=47778", scene.JoinLink);
            Assert.Equal("Join 192.168.1.20", scene.JoinText);
            Assert.StartsWith("Next: Race, 3 laps, ", scene.Footer);
            Assert.Equal(CarPalette.ForIndex(1), scene.Rows[1].Colour);
        }

        [Fact]
        public void LobbyShowsAPortThatIsNotTheDefault()
        {
            var inputs = Lobby();
            inputs.Port = 50000;
            Assert.Equal("Join 192.168.1.20:50000", ViewerScene.Build(inputs).JoinText);
        }

        [Fact]
        public void CountdownCountsDownThenSaysGo()
        {
            Assert.Null(ViewerScene.CountdownText(-3500));
            Assert.Equal("3", ViewerScene.CountdownText(-2999));
            Assert.Equal("1", ViewerScene.CountdownText(-1));
            Assert.Equal("GO!", ViewerScene.CountdownText(0));
            Assert.Equal("GO!", ViewerScene.CountdownText(999));
            Assert.Null(ViewerScene.CountdownText(1000));
        }

        [Fact]
        public void BeforeStandingsEveryoneIsInGridOrder()
        {
            var race = new FakeRaceView { Phase = RacePhase.Countdown, StartAtLocalMs = 10_000 };
            var scene = ViewerScene.Build(Racing(race, 8_500));
            Assert.Equal(ViewerPhase.Countdown, scene.Phase);
            Assert.Equal("2", scene.Countdown);
            Assert.Equal(new[] { "Turbo", "Bob", "Alice" }, scene.Rows.Select(r => r.Name));
            Assert.Equal(3, scene.Cars.Count);
            Assert.All(scene.Cars, c => Assert.Equal(0, c.Place));
        }

        [Fact]
        public void RaceShowsStandingsLapsAndTheClock()
        {
            var race = new FakeRaceView
            {
                TrackIndex = 1,
                StartAtLocalMs = 1_000,
                Standings = new StandingsMessage
                {
                    Order =
                    {
                        new StandingsEntry { PlayerId = 2, LapsCompleted = 3, FinishPosition = 1 },
                        new StandingsEntry { PlayerId = 1, LapsCompleted = 1 },
                        new StandingsEntry { PlayerId = 3, LapsCompleted = 0 },
                    },
                },
            };
            var scene = ViewerScene.Build(Racing(race, 66_000));
            Assert.Equal(ViewerPhase.Racing, scene.Phase);
            Assert.Equal(TrackCatalog.Layouts[1].Definition.DisplayName, scene.Title);
            Assert.Equal("1:05", scene.Clock);
            Assert.Null(scene.Countdown);
            Assert.Equal(new[] { "Bob", "Alice", "Turbo" }, scene.Rows.Select(r => r.Name));
            Assert.Equal(new[] { "Finished", "Lap 2/3", "Lap 1/3" }, scene.Rows.Select(r => r.Detail));
            Assert.Equal(new[] { 1, 2, 3 }, scene.Rows.Select(r => r.Place));
            Assert.Equal(2, scene.Cars.Single(c => c.PlayerId == 1).Place);
        }

        [Fact]
        public void ArenasShowLivesPointsAndTimeLeft()
        {
            var battle = new FakeRaceView
            {
                Mode = GameMode.Battle,
                Standings = new StandingsMessage { Order = { new StandingsEntry { PlayerId = 1, LapsCompleted = 1 }, new StandingsEntry { PlayerId = 2, FinishPosition = 2 } } },
            };
            var scene = ViewerScene.Build(Racing(battle, 60_000));
            Assert.Equal("Battle", scene.Title);
            Assert.Equal("2:00", scene.Clock);
            Assert.Equal(new[] { "1 life", "Out" }, scene.Rows.Select(r => r.Detail));
            Assert.Equal(new[] { false, true }, scene.Rows.Select(r => r.Dim));

            var monster = new FakeRaceView { Mode = GameMode.Monster, Standings = new StandingsMessage { Order = { new StandingsEntry { PlayerId = 1, Points = 37 } } } };
            Assert.Equal("37 pts", ViewerScene.Build(Racing(monster, 0)).Rows[0].Detail);

            var sumo = new FakeRaceView { Mode = GameMode.Sumo };
            Assert.Equal(SumoRules.PlatformRadius(50f), ViewerScene.Build(Racing(sumo, 50_000)).PlatformRadius);
        }

        [Fact]
        public void ResultsShowTimesAndDnf()
        {
            var race = new FakeRaceView
            {
                Phase = RacePhase.Results,
                Rules = new EventRules(false, true, false),
                Results = new RaceResultsMessage
                {
                    Results =
                    {
                        new RaceResultEntry { PlayerId = 2, Name = "Bob", Position = 1, RaceTimeMs = 83_456 },
                        new RaceResultEntry { PlayerId = 9, Name = "Gone", Position = 2, DidNotFinish = true },
                    },
                },
            };
            var scene = ViewerScene.Build(Racing(race, 0));
            Assert.Equal(ViewerPhase.Results, scene.Phase);
            Assert.StartsWith("Results: ", scene.Title);
            Assert.Equal(new[] { "1:23.4", "DNF" }, scene.Rows.Select(r => r.Detail));
            Assert.True(scene.Rows[1].Dim);
            Assert.Equal("Rules: no damage, equal cars", scene.Footer);
        }
    }

    public class ViewerRendererTests
    {
        [Fact]
        public void FontHasEveryPrintableCharacterAndMeasuresWithSpacing()
        {
            Assert.Equal(95, PixelFont.GlyphCount);
            Assert.Equal(17 * 2, PixelFont.Measure("abc", 2));
            Assert.True(PixelFont.IsSet('|', 2, 3));
            Assert.False(PixelFont.IsSet(' ', 2, 3));

            // Anything outside ASCII draws as '?'.
            for (int column = 0; column < PixelFont.GlyphWidth; column++)
            {
                for (int row = 0; row < PixelFont.GlyphHeight; row++)
                {
                    Assert.Equal(PixelFont.IsSet('?', column, row), PixelFont.IsSet('é', column, row));
                }
            }
        }

        [Fact]
        public void FitShortensTextToTheWidth()
        {
            Assert.Equal("Alice", PixelFont.Fit("Alice", 4, 1000));
            string fitted = PixelFont.Fit("Bartholomew the Great", 4, 200);
            Assert.True(PixelFont.Measure(fitted, 4) <= 200);
            Assert.EndsWith(".", fitted);
        }

        [Fact]
        public void CirclesAreAntiAliasedAndClipped()
        {
            var image = new RgbImage(20, 20);
            image.FillCircle(10f, 10f, 5.3f, new Rgb(255, 0, 0));
            Assert.Equal(new Rgb(255, 0, 0), image.Get(10, 10));
            Assert.Equal(new Rgb(0, 0, 0), image.Get(0, 0));
            var edge = image.Get(15, 10);
            Assert.InRange(edge.R, 1, 254);

            // Off the edge is ignored, not an error.
            image.FillCircle(-3f, -3f, 6f, new Rgb(0, 255, 0));
            image.DrawLine(-10f, 5f, 30f, 5f, 3f, new Rgb(0, 0, 255));
            Assert.Equal(255, image.Get(0, 5).B);
        }

        [Theory]
        [InlineData(GameMode.Race, 0)]
        [InlineData(GameMode.Race, 2)]
        [InlineData(GameMode.Sumo, 0)]
        [InlineData(GameMode.Battle, 0)]
        [InlineData(GameMode.Monster, 0)]
        public void CarsAreDrawnWhereTheyAre(GameMode mode, int track)
        {
            var race = new FakeRaceView { Mode = mode, TrackIndex = track, Standings = new StandingsMessage { Order = { new StandingsEntry { PlayerId = 1 } } } };
            var scene = ViewerScene.Build(ViewerSceneTests.Racing(race, 5_000));
            var frame = new RgbImage(ViewerRenderer.Width, ViewerRenderer.Height);
            var renderer = new ViewerRenderer();
            renderer.Render(scene, frame);
            int yellow = Count(frame, CarPalette.ForIndex(0));
            Assert.InRange(yellow, 300, 1200);

            // Without the cars, that colour is gone from the map.
            scene.Cars.Clear();
            scene.Rows.Clear();
            renderer.Render(scene, frame);
            Assert.True(Count(frame, CarPalette.ForIndex(0)) < 20);
        }

        [Fact]
        public void EveryScreenRenders()
        {
            var renderer = new ViewerRenderer();
            var frame = new RgbImage(ViewerRenderer.Width, ViewerRenderer.Height);
            foreach (var (name, scene) in Screens())
            {
                renderer.Render(scene, frame);
                Assert.True(frame.Width * frame.Height - Count(frame, ViewerRenderer.Background) > 150_000, name);

                // Set VIEWER_SCREENSHOTS to a folder to look at them.
                string folder = Environment.GetEnvironmentVariable("VIEWER_SCREENSHOTS");
                if (!string.IsNullOrEmpty(folder))
                {
                    Directory.CreateDirectory(folder);
                    File.WriteAllBytes(Path.Combine(folder, name + ".png"), Png.Encode(frame));
                }
            }
        }

        private static IEnumerable<(string, ViewerScene)> Screens()
        {
            var standings = new StandingsMessage
            {
                Order =
                {
                    new StandingsEntry { PlayerId = 2, LapsCompleted = 1 },
                    new StandingsEntry { PlayerId = 1, LapsCompleted = 1 },
                    new StandingsEntry { PlayerId = 3, LapsCompleted = 0 },
                },
            };
            yield return ("lobby", ViewerScene.Build(ViewerSceneTests.Lobby()));
            yield return ("countdown", ViewerScene.Build(ViewerSceneTests.Racing(new FakeRaceView { Phase = RacePhase.Countdown, StartAtLocalMs = 3_000 }, 1_200)));
            for (int track = 0; track < TrackCatalog.Count; track++)
            {
                yield return ("race-" + TrackCatalog.Layouts[track].Definition.Id, ViewerScene.Build(ViewerSceneTests.Racing(new FakeRaceView { TrackIndex = track, Standings = standings }, 75_000)));
            }

            foreach (var mode in new[] { GameMode.Sumo, GameMode.Battle, GameMode.Monster })
            {
                yield return (mode.ToString().ToLowerInvariant(), ViewerScene.Build(ViewerSceneTests.Racing(new FakeRaceView { Mode = mode, Standings = standings }, 40_000)));
            }

            yield return ("results", ViewerScene.Build(ViewerSceneTests.Racing(new FakeRaceView
            {
                Phase = RacePhase.Results,
                Results = new RaceResultsMessage
                {
                    Results =
                    {
                        new RaceResultEntry { PlayerId = 2, Name = "Bob", Position = 1, RaceTimeMs = 83_456 },
                        new RaceResultEntry { PlayerId = 1, Name = "Alice", Position = 2, RaceTimeMs = 85_012 },
                        new RaceResultEntry { PlayerId = 3, Name = "Turbo", Position = 3, DidNotFinish = true },
                    },
                },
            }, 0)));
        }

        private static int Count(RgbImage image, Rgb colour)
        {
            int count = 0;
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    if (image.Get(x, y).Equals(colour))
                    {
                        count++;
                    }
                }
            }

            return count;
        }
    }

    /// <summary>Writes an <see cref="RgbImage"/> as a PNG, to look at the viewer's frames.</summary>
    internal static class Png
    {
        public static byte[] Encode(RgbImage image)
        {
            var raw = new MemoryStream();
            for (int y = 0; y < image.Height; y++)
            {
                raw.WriteByte(0);
                raw.Write(image.Pixels, y * image.Width * 3, image.Width * 3);
            }

            var compressed = new MemoryStream();
            using (var z = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            {
                raw.Position = 0;
                raw.CopyTo(z);
            }

            var png = new MemoryStream();
            png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            var header = new byte[13];
            WriteBigEndian(header, 0, (uint)image.Width);
            WriteBigEndian(header, 4, (uint)image.Height);
            header[8] = 8;
            header[9] = 2;
            Chunk(png, "IHDR", header);
            Chunk(png, "IDAT", compressed.ToArray());
            Chunk(png, "IEND", Array.Empty<byte>());
            return png.ToArray();
        }

        private static void Chunk(Stream stream, string type, byte[] data)
        {
            var length = new byte[4];
            WriteBigEndian(length, 0, (uint)data.Length);
            stream.Write(length);
            var body = new byte[4 + data.Length];
            System.Text.Encoding.ASCII.GetBytes(type).CopyTo(body, 0);
            data.CopyTo(body, 4);
            stream.Write(body);
            var crc = new byte[4];
            WriteBigEndian(crc, 0, Crc(body));
            stream.Write(crc);
        }

        private static uint Crc(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in data)
            {
                crc ^= b;
                for (int k = 0; k < 8; k++)
                {
                    crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
                }
            }

            return ~crc;
        }

        private static void WriteBigEndian(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }
    }
}
