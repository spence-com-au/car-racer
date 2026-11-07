using System;
using System.Collections.Generic;
using CarRacer.Core.Driving;
using CarRacer.Core.Numerics;

namespace CarRacer.Core.Net
{
    public enum MessageType : byte
    {
        CarState = 1,
        Hit = 2,
        CheckpointReached = 3,
        RaceCountdown = 4,
        PlayerFinished = 5,
        JoinRequest = 6,
        JoinReply = 7,
        Roster = 8,
        ClockPing = 9,
        ClockPong = 10,
        SetReady = 11,
        Standings = 12,
        RaceResults = 13,
        ReturnToLobby = 14,
        ItemBoxClaim = 15,
        ItemBoxTaken = 16,
        ItemUsed = 17,
        HazardGone = 18,
        Projectiles = 19,
        CupStandings = 20,
        ControlReturned = 21,
        ChangeCar = 22,
        VoteTrack = 23,
        LobbySettings = 24,
        LobbyCommand = 25,
        Stunt = 26,
        TvStatus = 27,
        CastCommand = 28,
    }

    [Flags]
    public enum CarStateFlags : byte
    {
        None = 0,
        Drifting = 1 << 0,
        Boosting = 1 << 1,
        Shielded = 1 << 2,
        SpunOut = 1 << 3,
        BotControlled = 1 << 4,

        /// <summary>The car is wrecked (design doc §4): stopped until it is mended.</summary>
        Wrecked = 1 << 5,

        /// <summary>How damaged the car looks, for its smoke: light, or heavy (both bits).</summary>
        SmokeLight = 1 << 6,
        SmokeHeavy = 1 << 7,
    }

    /// <summary>Packs a car's damage into its state flags and back, so every phone can draw its smoke.</summary>
    public static class DamageFlags
    {
        public static CarStateFlags For(int smokeLevel, bool wrecked)
        {
            var flags = wrecked ? CarStateFlags.Wrecked : CarStateFlags.None;
            if (smokeLevel >= 2)
            {
                flags |= CarStateFlags.SmokeHeavy;
            }
            else if (smokeLevel == 1)
            {
                flags |= CarStateFlags.SmokeLight;
            }

            return flags;
        }

        /// <summary>0 for no smoke, 1 for light, 2 for heavy.</summary>
        public static int SmokeLevel(CarStateFlags flags) =>
            (flags & CarStateFlags.SmokeHeavy) != 0 ? 2 : (flags & CarStateFlags.SmokeLight) != 0 ? 1 : 0;

        public static bool Wrecked(CarStateFlags flags) => (flags & CarStateFlags.Wrecked) != 0;
    }

    /// <summary>The flags a car's owner sends with its state, the same for a player's car as for a bot's (design doc §9).</summary>
    public static class CarFlags
    {
        public static CarStateFlags For(CarState state, bool shielded, CarDamage damage)
        {
            var flags = DamageFlags.For(damage.SmokeLevel, damage.Wrecked);
            if (state.Drifting)
            {
                flags |= CarStateFlags.Drifting;
            }

            if (state.BoostSecondsLeft > 0f)
            {
                flags |= CarStateFlags.Boosting;
            }

            if (shielded)
            {
                flags |= CarStateFlags.Shielded;
            }

            if (state.SpunOut)
            {
                flags |= CarStateFlags.SpunOut;
            }

            return flags;
        }
    }

    public interface IGameMessage
    {
        MessageType Type { get; }
        void Write(NetWriter writer);
    }

    /// <summary>
    /// Owner-authoritative car state, sent by each phone for its own car at ~30 Hz
    /// (and by the host for bot cars). Times are in the sender's clock.
    /// </summary>
    public sealed class CarStateMessage : IGameMessage
    {
        public byte PlayerId;
        public uint Sequence;
        public long SentAtMs;
        public Vec3 Position;
        public Vec3 Velocity;
        public float HeadingRadians;
        public CarStateFlags Flags;

        public MessageType Type => MessageType.CarState;

        public void Write(NetWriter w)
        {
            w.WriteByte(PlayerId);
            w.WriteUInt(Sequence);
            w.WriteLong(SentAtMs);
            w.WriteVec3(Position);
            w.WriteVec3(Velocity);
            w.WriteFloat(HeadingRadians);
            w.WriteByte((byte)Flags);
        }

        public static CarStateMessage Read(NetReader r) => new CarStateMessage
        {
            PlayerId = r.ReadByte(),
            Sequence = r.ReadUInt(),
            SentAtMs = r.ReadLong(),
            Position = r.ReadVec3(),
            Velocity = r.ReadVec3(),
            HeadingRadians = r.ReadFloat(),
            Flags = (CarStateFlags)r.ReadByte(),
        };
    }

    /// <summary>
    /// Fallback car-to-car contact notification (see BumpResolver): the attacker detected a
    /// contact the target may have missed. The target applies <see cref="Impulse"/> unless
    /// it already resolved a contact with the attacker recently. Item hits reuse this message
    /// with <see cref="IsItemHit"/> set; those are sent by the host and always applied.
    /// </summary>
    public sealed class HitMessage : IGameMessage
    {
        public byte AttackerId;
        public byte TargetId;
        public long SentAtMs;
        public Vec3 Impulse;
        public bool IsItemHit;

        public MessageType Type => MessageType.Hit;

        public void Write(NetWriter w)
        {
            w.WriteByte(AttackerId);
            w.WriteByte(TargetId);
            w.WriteLong(SentAtMs);
            w.WriteVec3(Impulse);
            w.WriteBool(IsItemHit);
        }

        public static HitMessage Read(NetReader r) => new HitMessage
        {
            AttackerId = r.ReadByte(),
            TargetId = r.ReadByte(),
            SentAtMs = r.ReadLong(),
            Impulse = r.ReadVec3(),
            IsItemHit = r.ReadBool(),
        };
    }

    /// <summary>Guest → host: "my car crossed this checkpoint". The host validates order in RaceTracker.</summary>
    public sealed class CheckpointReachedMessage : IGameMessage
    {
        public byte PlayerId;
        public ushort CheckpointIndex;
        public long RaceTimeMs;

        public MessageType Type => MessageType.CheckpointReached;

        public void Write(NetWriter w)
        {
            w.WriteByte(PlayerId);
            w.WriteUShort(CheckpointIndex);
            w.WriteLong(RaceTimeMs);
        }

        public static CheckpointReachedMessage Read(NetReader r) => new CheckpointReachedMessage
        {
            PlayerId = r.ReadByte(),
            CheckpointIndex = r.ReadUShort(),
            RaceTimeMs = r.ReadLong(),
        };
    }

    /// <summary>
    /// Host → all: the race starts at <see cref="StartAtHostMs"/> in the host's clock. Each
    /// phone converts that to its own clock using its estimated offset, so the "GO" lands
    /// at the same moment everywhere.
    /// </summary>
    public sealed class RaceCountdownMessage : IGameMessage
    {
        public long StartAtHostMs;
        public byte LapCount;

        /// <summary>Race, Sumo or Battle (design doc §7).</summary>
        public CarRacer.Core.Arena.GameMode Mode;

        /// <summary>Which of the built-in tracks a race is on (an index into <c>TrackCatalog</c>); 0 in arenas.</summary>
        public byte TrackIndex;

        /// <summary>Player ids from pole back (design doc §10's reverse grid); anyone missing starts on the slot of their id.</summary>
        public List<byte> GridOrder = new List<byte>();

        /// <summary>The host's rules for the event (<see cref="CarRacer.Core.Race.EventRules"/>), as a byte.</summary>
        public byte Rules;

        public MessageType Type => MessageType.RaceCountdown;

        public void Write(NetWriter w)
        {
            w.WriteLong(StartAtHostMs);
            w.WriteByte(LapCount);
            w.WriteByte((byte)Mode);
            w.WriteByte(TrackIndex);
            w.WriteByte((byte)GridOrder.Count);
            foreach (byte id in GridOrder)
            {
                w.WriteByte(id);
            }

            w.WriteByte(Rules);
        }

        public static RaceCountdownMessage Read(NetReader r)
        {
            var message = new RaceCountdownMessage
            {
                StartAtHostMs = r.ReadLong(),
                LapCount = r.ReadByte(),
                Mode = (CarRacer.Core.Arena.GameMode)r.ReadByte(),
                TrackIndex = r.ReadByte(),
            };
            int count = r.ReadByte();
            for (int i = 0; i < count; i++)
            {
                message.GridOrder.Add(r.ReadByte());
            }

            message.Rules = r.ReadByte();
            return message;
        }
    }

    /// <summary>Host → all: a player crossed the finish line in the given position.</summary>
    public sealed class PlayerFinishedMessage : IGameMessage
    {
        public byte PlayerId;
        public byte FinishPosition;
        public long RaceTimeMs;

        public MessageType Type => MessageType.PlayerFinished;

        public void Write(NetWriter w)
        {
            w.WriteByte(PlayerId);
            w.WriteByte(FinishPosition);
            w.WriteLong(RaceTimeMs);
        }

        public static PlayerFinishedMessage Read(NetReader r) => new PlayerFinishedMessage
        {
            PlayerId = r.ReadByte(),
            FinishPosition = r.ReadByte(),
            RaceTimeMs = r.ReadLong(),
        };
    }

    /// <summary>Frames game messages as [type byte][payload].</summary>
    public static class MessageCodec
    {
        public static byte[] Encode(IGameMessage message)
        {
            var writer = new NetWriter();
            writer.WriteByte((byte)message.Type);
            message.Write(writer);
            return writer.ToArray();
        }

        public static IGameMessage Decode(byte[] data) => Decode(data, 0, data.Length);

        public static IGameMessage Decode(byte[] data, int offset, int count)
        {
            var reader = new NetReader(data, offset, count);
            var type = (MessageType)reader.ReadByte();
            IGameMessage message;
            switch (type)
            {
                case MessageType.CarState:
                    message = CarStateMessage.Read(reader);
                    break;
                case MessageType.Hit:
                    message = HitMessage.Read(reader);
                    break;
                case MessageType.CheckpointReached:
                    message = CheckpointReachedMessage.Read(reader);
                    break;
                case MessageType.RaceCountdown:
                    message = RaceCountdownMessage.Read(reader);
                    break;
                case MessageType.PlayerFinished:
                    message = PlayerFinishedMessage.Read(reader);
                    break;
                case MessageType.JoinRequest:
                    message = JoinRequestMessage.Read(reader);
                    break;
                case MessageType.JoinReply:
                    message = JoinReplyMessage.Read(reader);
                    break;
                case MessageType.Roster:
                    message = RosterMessage.Read(reader);
                    break;
                case MessageType.ClockPing:
                    message = ClockPingMessage.Read(reader);
                    break;
                case MessageType.ClockPong:
                    message = ClockPongMessage.Read(reader);
                    break;
                case MessageType.SetReady:
                    message = SetReadyMessage.Read(reader);
                    break;
                case MessageType.Standings:
                    message = StandingsMessage.Read(reader);
                    break;
                case MessageType.RaceResults:
                    message = RaceResultsMessage.Read(reader);
                    break;
                case MessageType.ReturnToLobby:
                    message = new ReturnToLobbyMessage();
                    break;
                case MessageType.ItemBoxClaim:
                    message = ItemBoxClaimMessage.Read(reader);
                    break;
                case MessageType.ItemBoxTaken:
                    message = ItemBoxTakenMessage.Read(reader);
                    break;
                case MessageType.ItemUsed:
                    message = ItemUsedMessage.Read(reader);
                    break;
                case MessageType.HazardGone:
                    message = HazardGoneMessage.Read(reader);
                    break;
                case MessageType.Projectiles:
                    message = ProjectilesMessage.Read(reader);
                    break;
                case MessageType.CupStandings:
                    message = CupStandingsMessage.Read(reader);
                    break;
                case MessageType.ControlReturned:
                    message = ControlReturnedMessage.Read(reader);
                    break;
                case MessageType.ChangeCar:
                    message = ChangeCarMessage.Read(reader);
                    break;
                case MessageType.VoteTrack:
                    message = VoteTrackMessage.Read(reader);
                    break;
                case MessageType.LobbySettings:
                    message = LobbySettingsMessage.Read(reader);
                    break;
                case MessageType.LobbyCommand:
                    message = LobbyCommandMessage.Read(reader);
                    break;
                case MessageType.Stunt:
                    message = StuntMessage.Read(reader);
                    break;
                case MessageType.TvStatus:
                    message = TvStatusMessage.Read(reader);
                    break;
                case MessageType.CastCommand:
                    message = CastCommandMessage.Read(reader);
                    break;
                default:
                    throw new NetFormatException($"Unknown message type {(byte)type}.");
            }

            if (reader.Remaining != 0)
            {
                throw new NetFormatException($"{reader.Remaining} unexpected trailing bytes after {type}.");
            }

            return message;
        }
    }
}
