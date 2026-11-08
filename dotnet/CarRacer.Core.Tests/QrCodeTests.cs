using System;
using System.Text;
using CarRacer.Core.Platform;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class QrCodeTests
    {
        public static TheoryData<int> CaseIndices()
        {
            var data = new TheoryData<int>();
            for (int i = 0; i < QrReference.Cases.Length; i++)
            {
                data.Add(i);
            }

            return data;
        }

        [Theory]
        [MemberData(nameof(CaseIndices))]
        public void ItMatchesAnIndependentEncoderModuleForModule(int index)
        {
            // Masks are compared with the reference's choice fixed: python-qrcode scores masks a little
            // differently from the standard, and any mask reads the same.
            var reference = QrReference.Cases[index];
            var code = QrCode.Encode(reference.Text, reference.Level, QrReference.ChosenMasks[index]);
            Assert.Equal(reference.Version, code.Version);
            Assert.Equal(reference.Rows.Length, code.Size);

            var mismatches = new StringBuilder();
            for (int y = 0; y < code.Size; y++)
            {
                for (int x = 0; x < code.Size; x++)
                {
                    if (code.IsDark(x, y) != (reference.Rows[y][x] == '#'))
                    {
                        mismatches.Append($"({x},{y}) ");
                    }
                }
            }

            Assert.True(mismatches.Length == 0, $"case {index} differs at {mismatches}");
        }

        // Masks the lowest-penalty rule picks, from a separate Python port of Nayuki's penalty scorer run over
        // all eight masks of each input. They span several masks, so a scorer that always answered 0 (or any
        // other constant) fails.
        [Theory]
        [InlineData("HELLO", QrErrorCorrection.Medium, 4)]
        [InlineData("carracer://join?host=192.168.1.20&port=7777", QrErrorCorrection.Medium, 4)]
        [InlineData("carracer://join?host=10.0.0.5&port=7777", QrErrorCorrection.Low, 7)]
        [InlineData("https://example.com/tv", QrErrorCorrection.Medium, 1)]
        [InlineData("123456789012345", QrErrorCorrection.Low, 2)]
        [InlineData("http://192.168.0.101:8080/", QrErrorCorrection.Medium, 2)]
        public void TheAutomaticMaskIsTheOneWithTheLowestPenalty(string text, QrErrorCorrection level, int mask)
        {
            Assert.Equal(mask, QrCode.Encode(text, level).Mask);
        }

        [Fact]
        public void TheAutomaticMaskIsReproducible()
        {
            var automatic = QrCode.Encode("carracer://join?host=192.168.1.20&port=7777");
            var forced = QrCode.Encode("carracer://join?host=192.168.1.20&port=7777", QrErrorCorrection.Medium, automatic.Mask);
            for (int y = 0; y < automatic.Size; y++)
            {
                for (int x = 0; x < automatic.Size; x++)
                {
                    Assert.Equal(forced.IsDark(x, y), automatic.IsDark(x, y));
                }
            }
        }

        [Fact]
        public void ItPicksTheSmallestVersionThatFits()
        {
            int fits = QrCode.DataCapacityBytes(3, QrErrorCorrection.Medium);
            Assert.Equal(3, QrCode.Encode(new string('a', fits), QrErrorCorrection.Medium).Version);
            Assert.Equal(4, QrCode.Encode(new string('a', fits + 1), QrErrorCorrection.Medium).Version);
        }

        [Fact]
        public void KnownCapacitiesMatchTheStandard()
        {
            // ISO/IEC 18004 table 7, byte mode.
            Assert.Equal(17, QrCode.DataCapacityBytes(1, QrErrorCorrection.Low));
            Assert.Equal(14, QrCode.DataCapacityBytes(1, QrErrorCorrection.Medium));
            Assert.Equal(62, QrCode.DataCapacityBytes(4, QrErrorCorrection.Medium));
            Assert.Equal(271, QrCode.DataCapacityBytes(10, QrErrorCorrection.Low));
            Assert.Equal(119, QrCode.DataCapacityBytes(10, QrErrorCorrection.High));
        }

        [Fact]
        public void TooMuchTextIsRefused()
        {
            Assert.Throws<ArgumentException>(() => QrCode.Encode(new string('a', 272), QrErrorCorrection.Low));
        }

        // An unchecked 8 would be ORed into the format bits' level and read back as a different level and mask.
        [Theory]
        [InlineData(-2)]
        [InlineData(8)]
        public void AMaskOutsideTheEightPatternsIsRefused(int mask)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => QrCode.Encode("HELLO", QrErrorCorrection.Medium, mask));
        }

        [Fact]
        public void OutsideTheGridIsLight()
        {
            var code = QrCode.Encode("HELLO");
            Assert.False(code.IsDark(-1, 0));
            Assert.False(code.IsDark(code.Size, 0));
            Assert.True(code.IsDark(0, 0));
        }
    }
}
