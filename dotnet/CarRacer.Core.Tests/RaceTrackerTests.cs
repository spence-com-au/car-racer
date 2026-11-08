using System;
using CarRacer.Core.Race;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class RaceTrackerTests
    {
        private static void DriveLap(RaceTracker tracker, int player, int checkpoints, ref long time)
        {
            for (int i = 1; i < checkpoints; i++)
            {
                Assert.Equal(CheckpointResult.Accepted, tracker.ReportCheckpoint(player, i, time += 1000));
            }
        }

        [Fact]
        public void FullRaceSequence()
        {
            var tracker = new RaceTracker(checkpointCount: 4, lapCount: 2, playerIds: new[] { 1 });
            long t = 0;

            Assert.Equal(CheckpointResult.Started, tracker.ReportCheckpoint(1, 0, t));
            DriveLap(tracker, 1, 4, ref t);
            Assert.Equal(CheckpointResult.LapCompleted, tracker.ReportCheckpoint(1, 0, t += 1000));
            Assert.Equal(1, tracker.Get(1).LapsCompleted);
            DriveLap(tracker, 1, 4, ref t);
            Assert.Equal(CheckpointResult.Finished, tracker.ReportCheckpoint(1, 0, t += 1000));

            var progress = tracker.Get(1);
            Assert.True(progress.IsFinished);
            Assert.Equal(1, progress.FinishPosition);
            Assert.Equal(t, progress.FinishTimeMs);
            Assert.Equal(2, progress.LapsCompleted);
        }

        [Fact]
        public void OutOfOrderAndDuplicateCheckpointsAreIgnored()
        {
            var tracker = new RaceTracker(4, 1, new[] { 1 });
            Assert.Equal(CheckpointResult.Ignored, tracker.ReportCheckpoint(1, 1, 0)); // still on the grid
            Assert.Equal(CheckpointResult.Started, tracker.ReportCheckpoint(1, 0, 0));
            Assert.Equal(CheckpointResult.Ignored, tracker.ReportCheckpoint(1, 0, 1)); // duplicate trigger
            Assert.Equal(CheckpointResult.Ignored, tracker.ReportCheckpoint(1, 2, 2)); // skipped 1 (shortcut)
            Assert.Equal(CheckpointResult.Accepted, tracker.ReportCheckpoint(1, 1, 3));
            Assert.Equal(2, tracker.Get(1).NextCheckpoint);
        }

        [Fact]
        public void ReversingOverTheLineDoesNotCountALap()
        {
            var tracker = new RaceTracker(4, 3, new[] { 1 });
            tracker.ReportCheckpoint(1, 0, 0);
            // Driving backwards hits checkpoint 3, then 0 again: neither is the expected next.
            Assert.Equal(CheckpointResult.Ignored, tracker.ReportCheckpoint(1, 3, 1));
            Assert.Equal(CheckpointResult.Ignored, tracker.ReportCheckpoint(1, 0, 2));
            Assert.Equal(0, tracker.Get(1).LapsCompleted);
        }

        [Fact]
        public void FinishOrderAndLateReportsAfterFinish()
        {
            var tracker = new RaceTracker(3, 1, new[] { 1, 2 });
            foreach (int p in new[] { 2, 1 })
            {
                tracker.ReportCheckpoint(p, 0, 0);
                tracker.ReportCheckpoint(p, 1, 0);
                tracker.ReportCheckpoint(p, 2, 0);
                Assert.Equal(CheckpointResult.Finished, tracker.ReportCheckpoint(p, 0, 0));
            }

            Assert.Equal(new[] { 2, 1 }, tracker.FinishOrder);
            Assert.True(tracker.AllFinished);
            Assert.Equal(CheckpointResult.Ignored, tracker.ReportCheckpoint(2, 1, 0));
            Assert.Equal(CheckpointResult.Ignored, tracker.ReportCheckpoint(99, 0, 0));
        }

        [Fact]
        public void StandingsUseCheckpointsThenTrackFraction()
        {
            var tracker = new RaceTracker(4, 3, new[] { 1, 2, 3 });
            foreach (int p in new[] { 1, 2, 3 })
            {
                tracker.ReportCheckpoint(p, 0, 0);
            }

            tracker.ReportCheckpoint(3, 1, 0);

            // Player 3 is a checkpoint ahead; 2 is further towards checkpoint 1 than 1 is.
            var fractions = new[] { 0f, 0.2f, 0.7f, 0.1f };
            Assert.Equal(new[] { 3, 2, 1 }, tracker.ComputeStandings(p => fractions[p]));
        }

        [Theory]
        [InlineData(1f)]
        [InlineData(1.5f)]
        public void StandingsKeepAFractionAtOrPastOneBehindTheNextCheckpoint(float fraction)
        {
            var tracker = new RaceTracker(4, 3, new[] { 1, 2 });
            tracker.ReportCheckpoint(1, 0, 0);
            tracker.ReportCheckpoint(2, 0, 0);
            tracker.ReportCheckpoint(2, 1, 0);

            // Player 1's geometry has reached checkpoint 1 before its report arrived, so it must not outrank player 2,
            // who has passed it. Player 1 has the lower id, so an unclamped tie at fraction 1 would also favour it.
            Assert.Equal(new[] { 2, 1 }, tracker.ComputeStandings(p => p == 1 ? fraction : 0f));
        }

        [Fact]
        public void StandingsTreatANegativeFractionAsZero()
        {
            var tracker = new RaceTracker(4, 3, new[] { 1, 2 });
            tracker.ReportCheckpoint(1, 0, 0);
            tracker.ReportCheckpoint(1, 1, 0);
            tracker.ReportCheckpoint(2, 0, 0);

            // Player 1 is a checkpoint ahead however far its geometry has slipped back.
            Assert.Equal(new[] { 1, 2 }, tracker.ComputeStandings(p => p == 1 ? -5f : 0.9f));
        }

        [Fact]
        public void StandingsBreakEqualTotalsByPlayerId()
        {
            // Not in id order, so the result cannot come from the order the racers were added in.
            var tracker = new RaceTracker(4, 3, new[] { 3, 1, 2 });
            foreach (int p in new[] { 3, 1, 2 })
            {
                tracker.ReportCheckpoint(p, 0, 0);
            }

            Assert.Equal(new[] { 1, 2, 3 }, tracker.ComputeStandings(_ => 0.5f));
        }

        [Fact]
        public void OvershootingTheNextCheckpointStillRanksBehindPassingIt()
        {
            var tracker = new RaceTracker(4, 3, new[] { 1, 2 });
            tracker.ReportCheckpoint(1, 0, 0);
            tracker.ReportCheckpoint(2, 0, 0);
            tracker.ReportCheckpoint(2, 1, 0);

            // Player 1 is past checkpoint 1 without having passed it; player 2 has just passed it.
            var fractions = new[] { 0f, 1.5f, 0f };
            Assert.Equal(new[] { 2, 1 }, tracker.ComputeStandings(p => fractions[p]));
        }

        [Fact]
        public void FinishRemainingAssignsPositionsAndMarksDnf()
        {
            var tracker = new RaceTracker(3, 1, new[] { 1, 2, 3 });
            foreach (int cp in new[] { 0, 1, 2, 0 })
            {
                tracker.ReportCheckpoint(1, cp, 50_000);
            }

            tracker.ReportCheckpoint(2, 0, 0);
            tracker.ReportCheckpoint(3, 0, 0);
            tracker.ReportCheckpoint(3, 1, 0);

            tracker.FinishRemaining(_ => 0.5f, 80_000);

            Assert.Equal(new[] { 1, 3, 2 }, tracker.FinishOrder);
            Assert.False(tracker.Get(1).DidNotFinish);
            Assert.True(tracker.Get(2).DidNotFinish);
            Assert.Equal(3, tracker.Get(2).FinishPosition);
            Assert.True(tracker.AllFinished);
        }

        [Fact]
        public void InvalidConfigurationThrows()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RaceTracker(0, 1, new[] { 1 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RaceTracker(3, 0, new[] { 1 }));
            Assert.Throws<ArgumentException>(() => new RaceTracker(3, 1, Array.Empty<int>()));
        }

        [Fact]
        public void WrongWayNeedsSustainedReverseAndClearsWithHysteresis()
        {
            var detector = new WrongWayDetector();
            Assert.False(detector.Update(-1f, 10f, 0.5f));
            Assert.False(detector.Update(-1f, 0.5f, 1.0f)); // too slow: timer resets
            Assert.False(detector.Update(-1f, 10f, 0.9f));
            Assert.True(detector.Update(-1f, 10f, 0.2f));

            Assert.True(detector.Update(0.1f, 10f, 1.0f)); // not past exit threshold yet
            Assert.True(detector.Update(0.5f, 10f, 0.2f));
            Assert.False(detector.Update(0.5f, 10f, 0.2f));
        }
    }
}
