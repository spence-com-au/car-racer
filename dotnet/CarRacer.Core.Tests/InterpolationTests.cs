using CarRacer.Core.Numerics;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class InterpolationTests
    {
        [Theory]
        [InlineData(2f, 6f, 0f, 2f)]
        [InlineData(2f, 6f, 1f, 6f)]
        [InlineData(2f, 6f, 0.25f, 3f)]
        [InlineData(6f, 2f, 0.5f, 4f)]
        public void LerpMovesFromAToB(float a, float b, float t, float expected)
        {
            Assert.Equal(expected, Interpolation.Lerp(a, b, t), 5);
        }

        [Fact]
        public void BlendClosesNothingInNoTimeAndNearlyAllOfTheGapInALongOne()
        {
            Assert.Equal(0f, Interpolation.Blend(5f, 0f), 5);
            Assert.Equal(1f, Interpolation.Blend(5f, 10f), 5);
        }

        [Fact]
        public void BlendComposesAcrossFrameRates()
        {
            float remainingInOneStep = 1f - Interpolation.Blend(4f, 0.1f);
            float remainingInTwoSteps = (1f - Interpolation.Blend(4f, 0.05f)) * (1f - Interpolation.Blend(4f, 0.05f));
            Assert.Equal(remainingInOneStep, remainingInTwoSteps, 5);
        }
    }
}
