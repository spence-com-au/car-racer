using System.Collections.Generic;

namespace CarRacer.Core.Physics
{
    /// <summary>
    /// Ensures each car-to-car contact moves the local car exactly once, whether this phone
    /// detected the contact itself or learned about it from the other phone's fallback
    /// <c>HitMessage</c>. Any contact with the same opponent inside the window is treated as
    /// the same bump.
    /// </summary>
    public sealed class HitDeduplicator
    {
        public const long DefaultWindowMs = 100;

        private readonly Dictionary<int, long> _lastContactMs = new Dictionary<int, long>();
        private readonly long _windowMs;

        public HitDeduplicator(long windowMs = DefaultWindowMs)
        {
            _windowMs = windowMs;
        }

        /// <summary>Local physics saw contact with <paramref name="otherPlayerId"/>. Returns true if the bump should be applied.</summary>
        public bool TryApplyLocalContact(int otherPlayerId, long nowMs) => TryRecord(otherPlayerId, nowMs);

        /// <summary>A fallback hit arrived from <paramref name="attackerId"/>. Returns true if it should be applied.</summary>
        public bool TryApplyRemoteHit(int attackerId, long nowMs) => TryRecord(attackerId, nowMs);

        private bool TryRecord(int otherPlayerId, long nowMs)
        {
            if (_lastContactMs.TryGetValue(otherPlayerId, out long last) && nowMs - last < _windowMs)
            {
                return false;
            }

            _lastContactMs[otherPlayerId] = nowMs;
            return true;
        }
    }
}
