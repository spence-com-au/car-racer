using System;
using System.Collections.Generic;
using System.Linq;

namespace CarRacer.Core.Race
{
    public enum CheckpointResult
    {
        /// <summary>Out of order, duplicate, unknown player, or player already finished.</summary>
        Ignored,

        /// <summary>A mid-lap checkpoint, in the correct order.</summary>
        Accepted,

        /// <summary>First crossing of the start line: the car has left the grid (which sits behind the line).</summary>
        Started,

        LapCompleted,
        Finished,
    }

    public sealed class RacerProgress
    {
        public int PlayerId { get; internal set; }

        /// <summary>Accepted checkpoint crossings, including the initial crossing of the start line.</summary>
        public int CheckpointsPassed { get; internal set; }

        public int LapsCompleted { get; internal set; }
        public int NextCheckpoint { get; internal set; }
        public bool IsFinished => FinishPosition > 0;

        /// <summary>1-based finishing position, or 0 if not finished.</summary>
        public int FinishPosition { get; internal set; }

        public long FinishTimeMs { get; internal set; }
        public bool DidNotFinish { get; internal set; }
    }

    /// <summary>
    /// Host-side referee for a lap race. Checkpoint 0 is the start/finish line and the grid
    /// is behind it, so a car must cross 0, then 1..N-1, then 0 again to complete a lap.
    /// Reports must arrive strictly in order; anything else is ignored, which stops both
    /// shortcuts and duplicate trigger events from counting.
    /// </summary>
    public sealed class RaceTracker
    {
        private readonly Dictionary<int, RacerProgress> _racers = new Dictionary<int, RacerProgress>();
        private readonly List<int> _finishOrder = new List<int>();

        public RaceTracker(int checkpointCount, int lapCount, IEnumerable<int> playerIds)
        {
            if (checkpointCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(checkpointCount));
            }

            if (lapCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(lapCount));
            }

            CheckpointCount = checkpointCount;
            LapCount = lapCount;
            foreach (int id in playerIds)
            {
                _racers[id] = new RacerProgress { PlayerId = id };
            }

            if (_racers.Count == 0)
            {
                throw new ArgumentException("A race needs at least one player.", nameof(playerIds));
            }
        }

        public int CheckpointCount { get; }
        public int LapCount { get; }
        public IReadOnlyList<int> FinishOrder => _finishOrder;
        /// <summary>True when every racer has finished, so the race can end.</summary>
        public bool AllFinished => _racers.Values.All(r => r.IsFinished);
        public IEnumerable<int> PlayerIds => _racers.Keys;

        private int CrossingsToFinish => LapCount * CheckpointCount + 1;

        public RacerProgress Get(int playerId) => _racers[playerId];

        public CheckpointResult ReportCheckpoint(int playerId, int checkpointIndex, long raceTimeMs)
        {
            if (!_racers.TryGetValue(playerId, out var racer) || racer.IsFinished)
            {
                return CheckpointResult.Ignored;
            }

            if (checkpointIndex != racer.NextCheckpoint)
            {
                return CheckpointResult.Ignored;
            }

            racer.CheckpointsPassed++;
            racer.NextCheckpoint = racer.CheckpointsPassed % CheckpointCount;
            int laps = (racer.CheckpointsPassed - 1) / CheckpointCount;
            bool lapCompleted = laps > racer.LapsCompleted;
            racer.LapsCompleted = laps;

            if (racer.CheckpointsPassed >= CrossingsToFinish)
            {
                _finishOrder.Add(playerId);
                racer.FinishPosition = _finishOrder.Count;
                racer.FinishTimeMs = raceTimeMs;
                return CheckpointResult.Finished;
            }

            if (racer.CheckpointsPassed == 1)
            {
                return CheckpointResult.Started;
            }

            return lapCompleted ? CheckpointResult.LapCompleted : CheckpointResult.Accepted;
        }

        /// <summary>
        /// Current race order. Finished racers by finishing position, then everyone still racing by
        /// checkpoints passed plus <paramref name="fractionToNextCheckpoint"/> (0..1, how far
        /// along they are towards their next checkpoint, from track geometry).
        /// </summary>
        public IReadOnlyList<int> ComputeStandings(Func<int, float> fractionToNextCheckpoint)
        {
            var finished = _racers.Values.Where(r => r.IsFinished).OrderBy(r => r.FinishPosition).Select(r => r.PlayerId);
            var running = _racers.Values
                .Where(r => !r.IsFinished)
                .OrderByDescending(r => r.CheckpointsPassed + ClampBelowOne(fractionToNextCheckpoint(r.PlayerId)))
                .ThenBy(r => r.PlayerId)
                .Select(r => r.PlayerId);
            return finished.Concat(running).ToList();
        }

        /// <summary>
        /// Ends the race (e.g. when the post-finish timer expires): everyone still running is
        /// given the next finishing positions in their current race order and marked DNF.
        /// </summary>
        public void FinishRemaining(Func<int, float> fractionToNextCheckpoint, long raceTimeMs)
        {
            foreach (int id in ComputeStandings(fractionToNextCheckpoint))
            {
                var racer = _racers[id];
                if (racer.IsFinished)
                {
                    continue;
                }

                _finishOrder.Add(id);
                racer.FinishPosition = _finishOrder.Count;
                racer.FinishTimeMs = raceTimeMs;
                racer.DidNotFinish = true;
            }
        }

        /// <summary>
        /// Clamps short of 1, not to it, so a car that has reached or overshot its next checkpoint without passing it
        /// still ranks behind one that has passed it rather than tying with it.
        /// </summary>
        private static float ClampBelowOne(float fraction) => Math.Clamp(fraction, 0f, 0.999f);
    }
}
