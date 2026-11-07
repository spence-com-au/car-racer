using System;
using System.Collections.Generic;
using System.Linq;
using CarRacer.Core.Net;

namespace CarRacer.Core.Scoring
{
    public sealed class ChampionshipEntry
    {
        /// <summary>Stable identity across reconnects: display name plus device id.</summary>
        public string PlayerKey { get; internal set; }

        public string DisplayName { get; internal set; }
        public int Points { get; internal set; }
        public int Wins { get; internal set; }

        /// <summary>Finishing position in the most recent event, or 0 if the player wasn't in it.</summary>
        public int LastEventPosition { get; internal set; }
    }

    /// <summary>
    /// Points standings across a series of races and arena events. The host owns this and
    /// auto-saves it after each event (<see cref="ToBytes"/>) so a dead session can be resumed.
    /// </summary>
    public sealed class Championship
    {
        public static readonly int[] DefaultPointsTable = { 10, 8, 6, 4, 2, 1 };

        private const ushort SaveFormatVersion = 1;

        private readonly Dictionary<string, ChampionshipEntry> _entries = new Dictionary<string, ChampionshipEntry>();
        private readonly int[] _pointsTable;

        public Championship(int totalEvents, int[] pointsTable = null)
        {
            if (totalEvents < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(totalEvents));
            }

            TotalEvents = totalEvents;
            _pointsTable = (pointsTable ?? DefaultPointsTable).ToArray();
        }

        public int TotalEvents { get; }
        public int EventsCompleted { get; private set; }
        public bool IsComplete => EventsCompleted >= TotalEvents;

        public void AddPlayer(string playerKey, string displayName)
        {
            if (!_entries.ContainsKey(playerKey))
            {
                _entries[playerKey] = new ChampionshipEntry { PlayerKey = playerKey, DisplayName = displayName };
            }
        }

        public int PointsForPosition(int position) =>
            position >= 1 && position <= _pointsTable.Length ? _pointsTable[position - 1] : 0;

        /// <summary>
        /// Records an event result. <paramref name="finishingOrder"/> lists player keys from
        /// first to last. Registered players missing from the order score nothing for this event.
        /// </summary>
        public void RecordEvent(IReadOnlyList<string> finishingOrder)
        {
            if (IsComplete)
            {
                throw new InvalidOperationException("The championship is already complete.");
            }

            if (finishingOrder.Distinct().Count() != finishingOrder.Count)
            {
                throw new ArgumentException("A player appears more than once in the finishing order.", nameof(finishingOrder));
            }

            // Validate everything before changing anything, so a rejected result leaves the standings intact.
            foreach (string key in finishingOrder)
            {
                if (!_entries.ContainsKey(key))
                {
                    throw new ArgumentException($"Unknown player '{key}'.", nameof(finishingOrder));
                }
            }

            foreach (var entry in _entries.Values)
            {
                entry.LastEventPosition = 0;
            }

            for (int i = 0; i < finishingOrder.Count; i++)
            {
                var entry = _entries[finishingOrder[i]];
                int position = i + 1;
                entry.Points += PointsForPosition(position);
                entry.LastEventPosition = position;
                if (position == 1)
                {
                    entry.Wins++;
                }
            }

            EventsCompleted++;
        }

        /// <summary>Leader first. Ties: most wins, then best finish in the latest event, then name.</summary>
        public IReadOnlyList<ChampionshipEntry> Standings() => _entries.Values
            .OrderByDescending(e => e.Points)
            .ThenByDescending(e => e.Wins)
            .ThenBy(e => e.LastEventPosition == 0 ? int.MaxValue : e.LastEventPosition)
            .ThenBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.PlayerKey, StringComparer.Ordinal)
            .ToList();

        /// <summary>Start grid order for the next event, pole first. With reverse grid, the leader starts last.</summary>
        public IReadOnlyList<string> GridOrder(bool reverseGrid)
        {
            var order = Standings().Select(e => e.PlayerKey).ToList();
            if (reverseGrid)
            {
                order.Reverse();
            }

            return order;
        }

        public byte[] ToBytes()
        {
            var w = new NetWriter();
            w.WriteUShort(SaveFormatVersion);
            w.WriteByte((byte)TotalEvents);
            w.WriteByte((byte)EventsCompleted);
            w.WriteByte((byte)_pointsTable.Length);
            foreach (int p in _pointsTable)
            {
                w.WriteUShort((ushort)p);
            }

            w.WriteByte((byte)_entries.Count);
            foreach (var e in _entries.Values.OrderBy(e => e.PlayerKey, StringComparer.Ordinal))
            {
                w.WriteString(e.PlayerKey);
                w.WriteString(e.DisplayName);
                w.WriteUShort((ushort)e.Points);
                w.WriteByte((byte)e.Wins);
                w.WriteByte((byte)e.LastEventPosition);
            }

            return w.ToArray();
        }

        public static Championship FromBytes(byte[] data)
        {
            var r = new NetReader(data);
            ushort version = r.ReadUShort();
            if (version != SaveFormatVersion)
            {
                throw new NetFormatException($"Unsupported championship save version {version}.");
            }

            int totalEvents = r.ReadByte();
            int completed = r.ReadByte();
            var table = new int[r.ReadByte()];
            for (int i = 0; i < table.Length; i++)
            {
                table[i] = r.ReadUShort();
            }

            var championship = new Championship(totalEvents, table) { EventsCompleted = completed };
            int count = r.ReadByte();
            for (int i = 0; i < count; i++)
            {
                var entry = new ChampionshipEntry
                {
                    PlayerKey = r.ReadString(),
                    DisplayName = r.ReadString(),
                    Points = r.ReadUShort(),
                    Wins = r.ReadByte(),
                    LastEventPosition = r.ReadByte(),
                };
                championship._entries[entry.PlayerKey] = entry;
            }

            return championship;
        }
    }
}
