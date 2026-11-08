using CarRacer.Core.Numerics;

namespace CarRacer.Core.Net
{
    /// <summary>
    /// Host → one guest: you're back after your app was away, and a bot drove your car meanwhile
    /// (design doc §12). Carry on from here, with this many checkpoints already crossed. The guest
    /// echoes it back once it has done so, and the host drops its car states until then.
    /// </summary>
    public sealed class ControlReturnedMessage : IGameMessage
    {
        public byte PlayerId;
        public Vec3 Position;
        public Vec3 Velocity;
        public float HeadingRadians;

        /// <summary>Accepted checkpoint crossings so far, for the guest's <c>CheckpointDetector.Resume</c>; 0 outside a race.</summary>
        public ushort CheckpointsPassed;

        public MessageType Type => MessageType.ControlReturned;

        public void Write(NetWriter w)
        {
            w.WriteByte(PlayerId);
            w.WriteVec3(Position);
            w.WriteVec3(Velocity);
            w.WriteFloat(HeadingRadians);
            w.WriteUShort(CheckpointsPassed);
        }

        public static ControlReturnedMessage Read(NetReader r) => new ControlReturnedMessage
        {
            PlayerId = r.ReadByte(),
            Position = r.ReadVec3(),
            Velocity = r.ReadVec3(),
            HeadingRadians = r.ReadFloat(),
            CheckpointsPassed = r.ReadUShort(),
        };
    }
}
