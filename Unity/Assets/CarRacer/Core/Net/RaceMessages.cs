using System.Collections.Generic;
using CarRacer.Core.Arena;

namespace CarRacer.Core.Net
{
    /// <summary>
    /// One car's standing. The fields mean different things in different modes (design doc §7.0), so read them through
    /// <see cref="IsOut"/> and <see cref="Lives"/> rather than decoding them at each use.
    /// </summary>
    public struct StandingsEntry
    {
        public byte PlayerId;

        /// <summary>Laps done in a race; lives left in Battle.</summary>
        public byte LapsCompleted;

        /// <summary>
        /// In a race, the 1-based finishing position; in Sumo and Battle, the place a car went out in. 0 while still
        /// racing or still in.
        /// </summary>
        public byte FinishPosition;

        /// <summary>Monster Jam points so far (design doc §7.0); 0 in other modes.</summary>
        public ushort Points;

        /// <summary>Battle lives left, which travel in <see cref="LapsCompleted"/>.</summary>
        public int Lives => LapsCompleted;

        /// <summary>Whether this car is out of a Sumo or Battle event; nobody is ever out of a race or Monster Jam.</summary>
        public bool IsOut(GameMode mode) => (mode == GameMode.Sumo || mode == GameMode.Battle) && FinishPosition > 0;
    }

    /// <summary>Host → all, a couple of times a second during a race: everyone in race order, with laps done.</summary>
    public sealed class StandingsMessage : IGameMessage
    {
        public List<StandingsEntry> Order = new List<StandingsEntry>();

        public MessageType Type => MessageType.Standings;

        /// <summary>
        /// The cars ahead of <paramref name="playerId"/> that are still racing, in race order: what Lightning strikes,
        /// and what a bot waits for before using an item that goes after the cars ahead. Cars that have finished (or
        /// are out) are left be.
        /// </summary>
        public IEnumerable<byte> RacingAhead(byte playerId)
        {
            for (int i = 0; i < Order.Count && Order[i].PlayerId != playerId; i++)
            {
                if (Order[i].FinishPosition == 0)
                {
                    yield return Order[i].PlayerId;
                }
            }
        }

        public void Write(NetWriter w)
        {
            w.WriteByte((byte)Order.Count);
            foreach (var e in Order)
            {
                w.WriteByte(e.PlayerId);
                w.WriteByte(e.LapsCompleted);
                w.WriteByte(e.FinishPosition);
                w.WriteUShort(e.Points);
            }
        }

        public static StandingsMessage Read(NetReader r)
        {
            int count = r.ReadByte();
            var message = new StandingsMessage();
            for (int i = 0; i < count; i++)
            {
                message.Order.Add(new StandingsEntry { PlayerId = r.ReadByte(), LapsCompleted = r.ReadByte(), FinishPosition = r.ReadByte(), Points = r.ReadUShort() });
            }

            return message;
        }
    }

    public struct RaceResultEntry
    {
        public byte PlayerId;

        /// <summary>The name at the start of the race, so a player who has since left still shows by name.</summary>
        public string Name;
        public byte Position;
        public long RaceTimeMs;
        public bool DidNotFinish;

        /// <summary>Monster Jam points (design doc §7.0); 0 in other modes.</summary>
        public ushort Points;
    }

    /// <summary>Host → all: the race is over. Everyone in finishing order; unfinished racers are placed by race order and marked DNF.</summary>
    public sealed class RaceResultsMessage : IGameMessage
    {
        public List<RaceResultEntry> Results = new List<RaceResultEntry>();

        public MessageType Type => MessageType.RaceResults;

        public void Write(NetWriter w)
        {
            w.WriteByte((byte)Results.Count);
            foreach (var e in Results)
            {
                w.WriteByte(e.PlayerId);
                w.WriteString(e.Name);
                w.WriteByte(e.Position);
                w.WriteLong(e.RaceTimeMs);
                w.WriteBool(e.DidNotFinish);
                w.WriteUShort(e.Points);
            }
        }

        public static RaceResultsMessage Read(NetReader r)
        {
            int count = r.ReadByte();
            var message = new RaceResultsMessage();
            for (int i = 0; i < count; i++)
            {
                message.Results.Add(new RaceResultEntry
                {
                    PlayerId = r.ReadByte(),
                    Name = r.ReadString(),
                    Position = r.ReadByte(),
                    RaceTimeMs = r.ReadLong(),
                    DidNotFinish = r.ReadBool(),
                    Points = r.ReadUShort(),
                });
            }

            return message;
        }
    }

    public enum StuntKind : byte
    {
        /// <summary>A jump landed, after <see cref="StuntMessage.AirMs"/> in the air.</summary>
        Air = 0,

        /// <summary>The truck crushed <see cref="StuntMessage.VictimId"/>'s truck.</summary>
        Crush = 1,
    }

    /// <summary>Guest → host, in Monster Jam (design doc §7.0): a stunt by this phone's own truck, for the host to score.</summary>
    public sealed class StuntMessage : IGameMessage
    {
        public byte PlayerId;
        public StuntKind Kind;
        public ushort AirMs;
        public byte VictimId;

        public MessageType Type => MessageType.Stunt;

        public void Write(NetWriter w)
        {
            w.WriteByte(PlayerId);
            w.WriteByte((byte)Kind);
            w.WriteUShort(AirMs);
            w.WriteByte(VictimId);
        }

        public static StuntMessage Read(NetReader r)
        {
            var message = new StuntMessage { PlayerId = r.ReadByte() };
            byte kind = r.ReadByte();
            if (kind > (byte)StuntKind.Crush)
            {
                throw new NetFormatException($"Unknown stunt {kind}.");
            }

            message.Kind = (StuntKind)kind;
            message.AirMs = r.ReadUShort();
            message.VictimId = r.ReadByte();
            return message;
        }
    }

    /// <summary>Host → all: results are done; everyone goes back to the same lobby for a rematch (design doc §11).</summary>
    public sealed class ReturnToLobbyMessage : IGameMessage
    {
        public MessageType Type => MessageType.ReturnToLobby;

        public void Write(NetWriter w)
        {
        }
    }
}
