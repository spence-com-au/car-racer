using CarRacer.Core.Numerics;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class Vec3Tests
    {
        [Theory]
        [InlineData(3f, 4f, 5f, true)]
        [InlineData(3f, 4f, 4.9f, false)]
        [InlineData(0f, 0f, 0f, true)]
        public void WithinFlatIncludesTheEdgeOfTheRadius(float x, float z, float radius, bool expected)
        {
            Assert.Equal(expected, Vec3.WithinFlat(new Vec3(1f, 0f, 1f), new Vec3(1f + x, 0f, 1f + z), radius));
        }

        [Fact]
        public void WithinFlatIgnoresHeight()
        {
            Assert.True(Vec3.WithinFlat(Vec3.Zero, new Vec3(0.5f, 100f, 0f), 1f));
        }
    }
}
