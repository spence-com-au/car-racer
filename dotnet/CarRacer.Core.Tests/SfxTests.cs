using System;
using System.Linq;
using CarRacer.Core.Audio;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class SfxTests
    {
        [Theory]
        [InlineData(Sfx.Boost, "boost")]
        [InlineData(Sfx.BombThrow, "bomb_throw")]
        [InlineData(Sfx.CrunchLight, "crunch_light")]
        [InlineData(Sfx.ItemBox, "item_box")]
        public void FileNamesAreSnakeCase(Sfx sfx, string expected)
        {
            Assert.Equal(expected, sfx.FileName());
        }

        [Fact]
        public void EveryClipHasItsOwnFileName()
        {
            var names = ((Sfx[])Enum.GetValues(typeof(Sfx))).Select(sfx => sfx.FileName()).ToList();
            Assert.Equal(names.Count, names.Distinct().Count());
        }
    }
}
