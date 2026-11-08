using System;
using System.Collections.Generic;

namespace CarRacer.Core.Net
{
    /// <summary>
    /// Estimates the offset between this guest's clock and the host's (design doc §2, race start
    /// sync) from ping/pong samples, NTP-style: the host stamped its reply roughly halfway through
    /// the round trip. Samples with the shortest round trip are the least distorted by queueing,
    /// so the estimate uses the fastest of the recent ones.
    /// </summary>
    public sealed class ClockSync
    {
        public const int DefaultWindow = 16;

        private readonly int _window;
        private readonly Queue<(long OffsetMs, long RoundTripMs)> _samples = new Queue<(long, long)>();

        public ClockSync(int window = DefaultWindow)
        {
            if (window < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(window));
            }

            _window = window;
        }

        public bool HasEstimate => _samples.Count > 0;

        /// <summary>Host clock minus local clock, in ms. Zero until the first sample.</summary>
        public long OffsetMs { get; private set; }

        /// <summary>Round trip of the sample the offset came from.</summary>
        public long RoundTripMs { get; private set; }

        /// <summary>Best guess at the one-way delay to the host, for <c>RemoteCarPredictor</c>.</summary>
        public float OneWayLatencyMs => RoundTripMs / 2f;

        /// <summary>
        /// One-way delay of a car's state reaching this guest: <see cref="OneWayLatencyMs"/> from a car the host
        /// drives, its own or a bot's, and twice that from another guest's, which the host relays.
        /// <paramref name="noEstimateMs"/> stands in for the estimate before the first sample.
        /// </summary>
        public float CarLatencyMs(bool drivenByHost, float noEstimateMs)
        {
            float toHost = HasEstimate ? OneWayLatencyMs : noEstimateMs;
            return drivenByHost ? toHost : 2f * toHost;
        }

        /// <summary>Records a pong. Returns false (and ignores it) if the times are impossible.</summary>
        public bool AddSample(long localSentMs, long hostMs, long localReceivedMs)
        {
            long roundTrip = localReceivedMs - localSentMs;
            if (roundTrip < 0)
            {
                return false;
            }

            long offset = hostMs - (localSentMs + roundTrip / 2);
            _samples.Enqueue((offset, roundTrip));
            while (_samples.Count > _window)
            {
                _samples.Dequeue();
            }

            var best = (OffsetMs: 0L, RoundTripMs: long.MaxValue);
            foreach (var sample in _samples)
            {
                if (sample.RoundTripMs < best.RoundTripMs)
                {
                    best = sample;
                }
            }

            OffsetMs = best.OffsetMs;
            RoundTripMs = best.RoundTripMs;
            return true;
        }

        public long ToHostTime(long localMs) => localMs + OffsetMs;

        public long ToLocalTime(long hostMs) => hostMs - OffsetMs;
    }
}
