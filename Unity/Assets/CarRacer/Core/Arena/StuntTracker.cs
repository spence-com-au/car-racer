using System.Collections.Generic;

namespace CarRacer.Core.Arena
{
    /// <summary>One player's place when a Monster Jam event ends.</summary>
    public struct StuntPlacing
    {
        public int PlayerId;

        /// <summary>1 for the most points.</summary>
        public int Position;

        public int Points;
    }

    /// <summary>
    /// Monster Jam's scores (host side, design doc §7.0). Each phone times its own truck's jumps
    /// and spots its own crushes, as it does checkpoints, and reports them; the host keeps the
    /// score and only believes what fits: no more time in the air than has passed since that
    /// player's last jump, and a crush only on a truck the host sees close by, and not the same
    /// one again straight away.
    /// </summary>
    public sealed class StuntTracker
    {
        private readonly Dictionary<int, int> _points = new Dictionary<int, int>();
        private readonly Dictionary<int, long> _lastAirEndMs = new Dictionary<int, long>();
        private readonly Dictionary<(int, int), long> _lastCrushMs = new Dictionary<(int, int), long>();
        private readonly List<int> _order = new List<int>();

        public StuntTracker(IEnumerable<int> playerIds)
        {
            foreach (int id in playerIds)
            {
                _points[id] = 0;
                _lastAirEndMs[id] = 0;
                _order.Add(id);
            }
        }

        public IReadOnlyList<int> PlayerIds => _order;

        public int Points(int playerId) => _points.TryGetValue(playerId, out int points) ? points : 0;

        /// <summary>
        /// A jump of <paramref name="airMs"/> that landed at <paramref name="nowMs"/> (from the
        /// event's start). Returns the points it scored: none for a short hop, an unknown player, or
        /// more air than time has passed since their last jump landed.
        /// </summary>
        public int AddAir(int playerId, long airMs, long nowMs)
        {
            if (!_lastAirEndMs.TryGetValue(playerId, out long lastEnd) || airMs <= 0 || airMs > nowMs - lastEnd)
            {
                return 0;
            }

            _lastAirEndMs[playerId] = nowMs;
            int points = MonsterRules.AirPoints(airMs);
            _points[playerId] += points;
            return points;
        }

        /// <summary>
        /// <paramref name="attackerId"/> crushed <paramref name="victimId"/> at <paramref name="nowMs"/>,
        /// with the host seeing them <paramref name="distance"/> apart. Returns the points it scored.
        /// </summary>
        public int AddCrush(int attackerId, int victimId, long nowMs, float distance)
        {
            if (attackerId == victimId || !_points.ContainsKey(attackerId) || !_points.ContainsKey(victimId) || distance > MonsterRules.CrushReach)
            {
                return 0;
            }

            var key = (attackerId, victimId);
            if (_lastCrushMs.TryGetValue(key, out long last) && nowMs - last < MonsterRules.CrushCooldownMs)
            {
                return 0;
            }

            _lastCrushMs[key] = nowMs;
            _points[attackerId] += MonsterRules.CrushPoints;
            return MonsterRules.CrushPoints;
        }

        /// <summary>Most points first; a tie keeps the order players joined the event in.</summary>
        public List<StuntPlacing> Placings()
        {
            var ids = new List<int>(_order);
            ids.Sort((a, b) =>
            {
                int byPoints = Points(b).CompareTo(Points(a));
                return byPoints != 0 ? byPoints : _order.IndexOf(a).CompareTo(_order.IndexOf(b));
            });

            var placings = new List<StuntPlacing>(ids.Count);
            foreach (int id in ids)
            {
                placings.Add(new StuntPlacing { PlayerId = id, Position = placings.Count + 1, Points = Points(id) });
            }

            return placings;
        }
    }
}
