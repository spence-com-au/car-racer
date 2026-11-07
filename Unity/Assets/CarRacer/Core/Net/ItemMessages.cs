using System.Collections.Generic;
using CarRacer.Core.Items;
using CarRacer.Core.Numerics;

namespace CarRacer.Core.Net
{
    /// <summary>A car's owner → host: "my car drove through this item box". The host grants the first claim (design doc §8).</summary>
    public sealed class ItemBoxClaimMessage : IGameMessage
    {
        public byte PlayerId;
        public ushort BoxIndex;

        public MessageType Type => MessageType.ItemBoxClaim;

        public void Write(NetWriter w)
        {
            w.WriteByte(PlayerId);
            w.WriteUShort(BoxIndex);
        }

        public static ItemBoxClaimMessage Read(NetReader r) => new ItemBoxClaimMessage
        {
            PlayerId = r.ReadByte(),
            BoxIndex = r.ReadUShort(),
        };
    }

    /// <summary>Host → all: a box was taken by <see cref="PlayerId"/>, who now holds <see cref="Item"/>; it comes back at <see cref="RespawnAtHostMs"/> in the host's clock.</summary>
    public sealed class ItemBoxTakenMessage : IGameMessage
    {
        public ushort BoxIndex;
        public byte PlayerId;
        public ItemKind Item;
        public long RespawnAtHostMs;

        public MessageType Type => MessageType.ItemBoxTaken;

        public void Write(NetWriter w)
        {
            w.WriteUShort(BoxIndex);
            w.WriteByte(PlayerId);
            w.WriteByte((byte)Item);
            w.WriteLong(RespawnAtHostMs);
        }

        public static ItemBoxTakenMessage Read(NetReader r) => new ItemBoxTakenMessage
        {
            BoxIndex = r.ReadUShort(),
            PlayerId = r.ReadByte(),
            Item = (ItemKind)r.ReadByte(),
            RespawnAtHostMs = r.ReadLong(),
        };
    }

    /// <summary>
    /// A car's owner → host: "I used my item here". If the player really holds it, the host stamps
    /// <see cref="UsedAtHostMs"/> and relays it to everyone so they can show it (design doc §8).
    /// </summary>
    public sealed class ItemUsedMessage : IGameMessage
    {
        public byte PlayerId;
        public ItemKind Item;
        public Vec3 Position;
        public Vec3 Velocity;
        public float HeadingRadians;

        /// <summary>Set by the host when it relays the message; 0 from the owner.</summary>
        public long UsedAtHostMs;

        /// <summary>Set by the host when it relays the message: names what the item left behind (an oil slick), so <see cref="HazardGoneMessage"/> can refer to it.</summary>
        public ushort EffectId;

        public MessageType Type => MessageType.ItemUsed;

        public void Write(NetWriter w)
        {
            w.WriteByte(PlayerId);
            w.WriteByte((byte)Item);
            w.WriteVec3(Position);
            w.WriteVec3(Velocity);
            w.WriteFloat(HeadingRadians);
            w.WriteLong(UsedAtHostMs);
            w.WriteUShort(EffectId);
        }

        public static ItemUsedMessage Read(NetReader r) => new ItemUsedMessage
        {
            PlayerId = r.ReadByte(),
            Item = (ItemKind)r.ReadByte(),
            Position = r.ReadVec3(),
            Velocity = r.ReadVec3(),
            HeadingRadians = r.ReadFloat(),
            UsedAtHostMs = r.ReadLong(),
            EffectId = r.ReadUShort(),
        };
    }

    /// <summary>
    /// Host → all: what item use <see cref="EffectId"/> left behind is gone: an oil slick that caught
    /// a car, or a Missile or Bomb that hit, blew up or ran out. Oil slicks that time out are removed
    /// by everyone without a message.
    /// </summary>
    public sealed class HazardGoneMessage : IGameMessage
    {
        public ushort EffectId;

        /// <summary>
        /// What it was and where it ended. A projectile can end before any <see cref="ProjectilesMessage"/>
        /// carried it (a Bomb thrown beside a car goes off at once), so guests show its end from these.
        /// </summary>
        public ItemKind Item;
        public Vec3 Position;

        public MessageType Type => MessageType.HazardGone;

        public void Write(NetWriter w)
        {
            w.WriteUShort(EffectId);
            w.WriteByte((byte)Item);
            w.WriteVec3(Position);
        }

        public static HazardGoneMessage Read(NetReader r) => new HazardGoneMessage
        {
            EffectId = r.ReadUShort(),
            Item = (ItemKind)r.ReadByte(),
            Position = r.ReadVec3(),
        };
    }

    public struct ProjectileSnapshot
    {
        public ushort EffectId;
        public ItemKind Kind;
        public Vec3 Position;

        /// <summary>The player a Missile chases (for their HUD's warning), or <see cref="ProjectileRules.NoTarget"/>.</summary>
        public byte TargetId;
    }

    /// <summary>Host → all, 20 times a second while any are in flight: where every Missile and Bomb is.</summary>
    public sealed class ProjectilesMessage : IGameMessage
    {
        public List<ProjectileSnapshot> Projectiles = new List<ProjectileSnapshot>();

        public MessageType Type => MessageType.Projectiles;

        public void Write(NetWriter w)
        {
            w.WriteByte((byte)Projectiles.Count);
            foreach (var p in Projectiles)
            {
                w.WriteUShort(p.EffectId);
                w.WriteByte((byte)p.Kind);
                w.WriteVec3(p.Position);
                w.WriteByte(p.TargetId);
            }
        }

        public static ProjectilesMessage Read(NetReader r)
        {
            int count = r.ReadByte();
            var message = new ProjectilesMessage();
            for (int i = 0; i < count; i++)
            {
                message.Projectiles.Add(new ProjectileSnapshot { EffectId = r.ReadUShort(), Kind = (ItemKind)r.ReadByte(), Position = r.ReadVec3(), TargetId = r.ReadByte() });
            }

            return message;
        }
    }
}
