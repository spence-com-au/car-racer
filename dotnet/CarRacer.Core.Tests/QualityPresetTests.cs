using CarRacer.Core.Platform;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class QualityPresetTests
    {
        [Theory]
        [InlineData("Adreno (TM) 540", 4096, QualityLevel.Low)] // Pixel 2 XL, the baseline
        [InlineData("Adreno (TM) 618", 6144, QualityLevel.Medium)]
        [InlineData("Adreno (TM) 640", 8192, QualityLevel.High)]
        [InlineData("Adreno (TM) 740", 12288, QualityLevel.High)]
        [InlineData("Mali-G71 MP20", 4096, QualityLevel.Low)]
        [InlineData("Mali-G76 MP10", 8192, QualityLevel.Medium)]
        [InlineData("Mali-G78 MP20", 8192, QualityLevel.High)]
        [InlineData("Mali-G710 MC10", 12288, QualityLevel.High)]
        [InlineData("Mali-G310", 4096, QualityLevel.Low)] // held down by memory
        [InlineData("Adreno (TM) 530", 8192, QualityLevel.Low)]
        [InlineData("Adreno (TM) 599", 8192, QualityLevel.Low)]
        [InlineData("Adreno (TM) 600", 8192, QualityLevel.Medium)]
        [InlineData("Adreno (TM) 639", 8192, QualityLevel.Medium)]
        [InlineData("Mali-G71 MP20", 8192, QualityLevel.Low)]
        [InlineData("Mali-G72 MP18", 8192, QualityLevel.Medium)]
        [InlineData("Mali-G77 MP11", 8192, QualityLevel.High)]
        [InlineData("Mali-G310", 8192, QualityLevel.Medium)] // the restarted numbering, below G600
        [InlineData("Mali-G610 MC4", 8192, QualityLevel.High)]
        public void TheLevelFollowsTheGpuAndMemory(string gpu, int memoryMb, QualityLevel expected)
        {
            Assert.Equal(expected, QualityPresets.Pick(gpu, memoryMb));
        }

        [Fact]
        public void LittleMemoryHoldsAFastGpuDown()
        {
            Assert.Equal(QualityLevel.Low, QualityPresets.Pick("Adreno (TM) 740", 3072));
            Assert.Equal(QualityLevel.Medium, QualityPresets.Pick("Adreno (TM) 740", 6144));
        }

        [Fact]
        public void AnUnknownGpuGoesByMemory()
        {
            Assert.Equal(QualityLevel.High, QualityPresets.Pick("PowerVR Rogue GE8320", 8192));
            Assert.Equal(QualityLevel.Low, QualityPresets.Pick(null, 2048));
        }

        // An unknown GPU doesn't hold the level down, so these isolate the memory cut-offs.
        [Theory]
        [InlineData(4499, QualityLevel.Low)]
        [InlineData(4500, QualityLevel.Medium)]
        [InlineData(6999, QualityLevel.Medium)]
        [InlineData(7000, QualityLevel.High)]
        public void TheMemoryCutOffsFallWhereDocumented(int memoryMb, QualityLevel expected)
        {
            Assert.Equal(expected, QualityPresets.Pick(null, memoryMb));
        }

        [Fact]
        public void EachLevelHasItsDocumentedRenderSettings()
        {
            var low = QualityPresets.For(QualityLevel.Low);
            var medium = QualityPresets.For(QualityLevel.Medium);
            var high = QualityPresets.For(QualityLevel.High);
            Assert.Equal((0.65f, 0, 0.5f), (low.RenderScale, low.Msaa, low.TreeShare));
            Assert.Equal((0.75f, 2, 1f), (medium.RenderScale, medium.Msaa, medium.TreeShare));
            Assert.Equal((1f, 4, 1f), (high.RenderScale, high.Msaa, high.TreeShare));
        }
    }
}
