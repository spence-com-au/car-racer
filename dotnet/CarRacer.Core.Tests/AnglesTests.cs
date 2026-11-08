using System;
using CarRacer.Core.Numerics;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class AnglesTests
    {
        [Theory]
        [InlineData(0f)]
        [InlineData(0.5f)]
        [InlineData(2f)]
        [InlineData(-1f)]
        [InlineData(-3f)]
        public void HeadingAndForwardAreInverses(float heading)
        {
            var forward = Angles.Forward(heading);
            Assert.Equal(1f, forward.Length, 5);
            Assert.Equal(heading, Angles.Heading(forward * 7f + Vec3.Up * 3f), 5);
        }

        [Fact]
        public void HeadingIsClockwiseFromPlusZ()
        {
            Assert.True(Angles.Forward(0f).ApproximatelyEquals(Vec3.Forward));
            Assert.True(Angles.Forward(MathF.PI / 2f).ApproximatelyEquals(Vec3.Right));
            Assert.True(Vec3.RotateAroundUp(Vec3.Forward, 1f).ApproximatelyEquals(Angles.Forward(1f)));
        }

        [Theory]
        [InlineData(0f, 0f)]
        [InlineData(3f, 3f)]
        [InlineData(4f, 4f - 2f * MathF.PI)]
        [InlineData(-4f, -4f + 2f * MathF.PI)]
        [InlineData(7f, 7f - 2f * MathF.PI)]
        [InlineData(-20f, -20f + 6f * MathF.PI)]
        public void WrapSignedLandsInMinusPiToPi(float radians, float expected)
        {
            Assert.Equal(expected, Angles.WrapSigned(radians), 4);
        }

        [Theory]
        [InlineData(0f, 0f)]
        [InlineData(4f, 4f)]
        [InlineData(-1f, -1f + 2f * MathF.PI)]
        [InlineData(7f, 7f - 2f * MathF.PI)]
        [InlineData(-20f, -20f + 8f * MathF.PI)]
        public void WrapPositiveLandsInZeroToTwoPi(float radians, float expected)
        {
            Assert.Equal(expected, Angles.WrapPositive(radians), 4);
        }
    }
}
