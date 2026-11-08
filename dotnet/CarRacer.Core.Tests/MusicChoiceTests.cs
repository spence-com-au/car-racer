using System;
using CarRacer.Core.Arena;
using CarRacer.Core.Audio;
using CarRacer.Core.Track;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class MusicChoiceTests
    {
        [Fact]
        public void TheLobbyPlaysTheMenuTune()
        {
            Assert.Equal(MusicChoice.Menu, MusicChoice.For(false, GameMode.Race, 1));
            Assert.Equal(MusicChoice.Menu, MusicChoice.For(false, GameMode.Battle, 0));
        }

        [Fact]
        public void EachTrackHasItsOwnTune()
        {
            Assert.Equal("oval", MusicChoice.For(true, GameMode.Race, 0));
            Assert.Equal("canyon", MusicChoice.For(true, GameMode.Race, 1));
            Assert.Equal("figure_eight", MusicChoice.For(true, GameMode.Race, 2));
            Assert.Equal("sweepers", MusicChoice.For(true, GameMode.Race, 3));
            Assert.Equal("switchbacks", MusicChoice.For(true, GameMode.Race, 4));
            Assert.Equal("dunes", MusicChoice.For(true, GameMode.Race, 5));
            Assert.Equal("carousel", MusicChoice.For(true, GameMode.Race, 6));
            Assert.Equal("ridge", MusicChoice.For(true, GameMode.Race, 7));
            Assert.Equal(TrackCatalog.Layouts.Count, MusicChoice.TrackTunes.Length);
        }

        [Fact]
        public void ArenasShareATune()
        {
            Assert.Equal(MusicChoice.Arena, MusicChoice.For(true, GameMode.Sumo, 2));
            Assert.Equal(MusicChoice.Arena, MusicChoice.For(true, GameMode.Battle, 0));
        }

        [Fact]
        public void ExtraTracksReuseTheTunes()
        {
            Assert.Equal("oval", MusicChoice.For(true, GameMode.Race, MusicChoice.TrackTunes.Length));
            Assert.Equal("ridge", MusicChoice.For(true, GameMode.Race, -1));
        }

        [Fact]
        public void EveryChoiceIsAKnownTune()
        {
            foreach (GameMode mode in Enum.GetValues(typeof(GameMode)))
            {
                for (int track = -1; track <= MusicChoice.TrackTunes.Length; track++)
                {
                    Assert.Contains(MusicChoice.For(false, mode, track), MusicChoice.All);
                    Assert.Contains(MusicChoice.For(true, mode, track), MusicChoice.All);
                }
            }
        }
    }
}
