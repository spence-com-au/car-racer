using System.Collections.Generic;

namespace CarRacer.Core.Net
{
    public struct CupStandingsEntry
    {
        public string Name;
        public ushort Points;
        public byte Wins;

        /// <summary>Place in the latest event, or 0 if the player wasn't in it.</summary>
        public byte LastEventPosition;
    }

    /// <summary>
    /// Host → all, after each event of a cup (design doc §10): the points table, leader first, and
    /// when the next event starts. The host can pause the countdown to the next event; it resends
    /// this message whenever that changes.
    /// </summary>
    public sealed class CupStandingsMessage : IGameMessage
    {
        public string CupName = string.Empty;
        public byte EventsCompleted;
        public byte TotalEvents;

        /// <summary>When the next event's countdown starts, in host time; 0 while paused or once the cup is over.</summary>
        public long NextEventAtHostMs;

        public List<CupStandingsEntry> Standings = new List<CupStandingsEntry>();

        public MessageType Type => MessageType.CupStandings;

        public bool IsComplete => EventsCompleted >= TotalEvents;

        /// <summary>
        /// Whether the host has paused the countdown (<c>CupRun.Paused</c>): events are left but no time is set for the
        /// next.
        /// </summary>
        public bool IsPaused => !IsComplete && NextEventAtHostMs == 0;

        public void Write(NetWriter w)
        {
            w.WriteString(CupName);
            w.WriteByte(EventsCompleted);
            w.WriteByte(TotalEvents);
            w.WriteLong(NextEventAtHostMs);
            w.WriteByte((byte)Standings.Count);
            foreach (var e in Standings)
            {
                w.WriteString(e.Name);
                w.WriteUShort(e.Points);
                w.WriteByte(e.Wins);
                w.WriteByte(e.LastEventPosition);
            }
        }

        public static CupStandingsMessage Read(NetReader r)
        {
            var message = new CupStandingsMessage
            {
                CupName = r.ReadString(),
                EventsCompleted = r.ReadByte(),
                TotalEvents = r.ReadByte(),
                NextEventAtHostMs = r.ReadLong(),
            };
            int count = r.ReadByte();
            for (int i = 0; i < count; i++)
            {
                message.Standings.Add(new CupStandingsEntry { Name = r.ReadString(), Points = r.ReadUShort(), Wins = r.ReadByte(), LastEventPosition = r.ReadByte() });
            }

            return message;
        }
    }
}
