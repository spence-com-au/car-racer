using CarRacer.Core.Platform;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class PerfStatsTests
    {
        [Fact]
        public void SixtyFramesASecondReadsAsSixtyFps()
        {
            var stats = new FrameStats();
            for (int i = 0; i < 300; i++)
            {
                stats.Add(1f / 60f);
            }

            Assert.Equal(60f, stats.Fps, 1);
            Assert.InRange(stats.Frames, 119, 121);
            Assert.Equal(0, stats.SlowFrames);
            Assert.Equal(16.7f, stats.WorstMs, 1);
        }

        [Fact]
        public void ASpikeShowsForTheWholeWindowThenGoes()
        {
            var stats = new FrameStats();
            stats.Add(0.1f);
            for (int i = 0; i < 60; i++)
            {
                stats.Add(1f / 60f);
            }

            Assert.Equal(100f, stats.WorstMs, 1);
            Assert.Equal(1, stats.SlowFrames);

            for (int i = 0; i < 120; i++)
            {
                stats.Add(1f / 60f);
            }

            Assert.Equal(16.7f, stats.WorstMs, 1);
            Assert.Equal(0, stats.SlowFrames);
        }

        [Fact]
        public void TheWindowSurvivesTheBufferWrappingAround()
        {
            // At 240 fps the window holds about 480 of the buffer's 512 frames, so 600 frames
            // then a spike puts the window across the end of the buffer.
            var stats = new FrameStats();
            for (int i = 0; i < 600; i++)
            {
                stats.Add(1f / 240f);
            }

            stats.Add(0.05f);
            for (int i = 0; i < 100; i++)
            {
                stats.Add(1f / 240f);
            }

            // The spike takes the room of 12 ordinary frames.
            Assert.InRange(stats.Frames, 467, 470);
            Assert.Equal(50f, stats.WorstMs, 1);
            Assert.Equal(1, stats.SlowFrames);

            for (int i = 0; i < 500; i++)
            {
                stats.Add(1f / 240f);
            }

            Assert.Equal(4.2f, stats.WorstMs, 1);
            Assert.Equal(0, stats.SlowFrames);
        }

        [Fact]
        public void AFullBufferDropsTheOldestFrameBeforeTheWindowDoes()
        {
            // At 1000 fps the 2 s window would need 2000 frames, so the 512-frame buffer is the limit.
            var stats = new FrameStats();
            stats.Add(0.05f);
            for (int i = 0; i < 2000; i++)
            {
                stats.Add(0.001f);
            }

            Assert.Equal(512, stats.Frames);
            Assert.Equal(1000f, stats.Fps, 0);
            Assert.Equal(1f, stats.WorstMs, 1);
            Assert.Equal(0, stats.SlowFrames);
        }

        [Fact]
        public void AFrameLongerThanTheWindowIsStillCounted()
        {
            var stats = new FrameStats();
            stats.Add(1f / 60f);
            stats.Add(3f);

            Assert.Equal(1, stats.Frames);
            Assert.Equal(3000f, stats.WorstMs, 1);
            Assert.Equal(1f / 3f, stats.Fps, 3);
        }

        [Fact]
        public void NothingYetReadsAsZero()
        {
            var stats = new FrameStats();
            stats.Add(0f);
            Assert.Equal(0f, stats.Fps);
            Assert.Equal(0f, stats.WorstMs);
        }

        [Fact]
        public void TrafficReadsAsTheLastFullSecond()
        {
            var traffic = new TrafficCounter();
            traffic.Sent(100, 10.1);
            traffic.Sent(200, 10.9);
            traffic.Received(50, 10.5);
            Assert.Equal(0, traffic.SentPerSecond);

            traffic.Roll(11.2);
            Assert.Equal(300, traffic.SentPerSecond);
            Assert.Equal(50, traffic.ReceivedPerSecond);

            // A silent second, and a gap of several, both read as nothing.
            traffic.Roll(12.0);
            Assert.Equal(0, traffic.SentPerSecond);
            traffic.Sent(70, 12.5);
            traffic.Roll(15.0);
            Assert.Equal(0, traffic.SentPerSecond);
        }

        [Fact]
        public void TheReadoutFitsOnThreeLines()
        {
            var stats = new FrameStats();
            for (int i = 0; i < 120; i++)
            {
                stats.Add(1f / 60f);
            }

            var traffic = new TrafficCounter();
            traffic.Sent(7900, 0.5);
            traffic.Received(3072, 0.5);
            traffic.Roll(1.5);

            Assert.Equal("60 fps  worst 17 ms  0 slow\n142 draws  310 MB\nup 7.7  down 3.0 KB/s", PerfReadout.Format(stats, 142, 310, traffic));
            Assert.Equal("60 fps  worst 17 ms  0 slow\n- draws", PerfReadout.Format(stats, -1, -1, null));
        }
    }
}
