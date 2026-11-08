using System;
using System.Collections.Generic;
using System.Linq;
using CarRacer.Core.Arena;
using CarRacer.Core.Net;
using CarRacer.Core.Numerics;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class NetSerializationTests
    {
        [Fact]
        public void PrimitivesRoundTrip()
        {
            var w = new NetWriter(1);
            w.WriteByte(0xAB);
            w.WriteBool(true);
            w.WriteUShort(0xBEEF);
            w.WriteUInt(0xDEADBEEF);
            w.WriteInt(-123456);
            w.WriteLong(long.MinValue + 7);
            w.WriteFloat(-3.25f);
            w.WriteVec3(new Vec3(1.5f, -2f, 1e6f));
            w.WriteString("Zoë 🏎");

            var r = new NetReader(w.ToArray());
            Assert.Equal(0xAB, r.ReadByte());
            Assert.True(r.ReadBool());
            Assert.Equal(0xBEEF, r.ReadUShort());
            Assert.Equal(0xDEADBEEF, r.ReadUInt());
            Assert.Equal(-123456, r.ReadInt());
            Assert.Equal(long.MinValue + 7, r.ReadLong());
            Assert.Equal(-3.25f, r.ReadFloat());
            Assert.Equal(new Vec3(1.5f, -2f, 1e6f), r.ReadVec3());
            Assert.Equal("Zoë 🏎", r.ReadString());
            Assert.Equal(0, r.Remaining);
        }

        [Fact]
        public void EncodingIsLittleEndian()
        {
            var w = new NetWriter();
            w.WriteUInt(0x01020304);
            Assert.Equal(new byte[] { 4, 3, 2, 1 }, w.ToArray());
        }

        [Fact]
        public void TruncatedReadThrows()
        {
            var r = new NetReader(new byte[] { 1, 2, 3 });
            Assert.Throws<NetFormatException>(() => r.ReadUInt());
        }

        [Fact]
        public void OverlongStringIsRejected()
        {
            var w = new NetWriter();
            Assert.Throws<ArgumentException>(() => w.WriteString(new string('x', 256)));
        }

        [Fact]
        public void CarStateRoundTripsAndIs43Bytes()
        {
            var original = new CarStateMessage
            {
                PlayerId = 3,
                Sequence = uint.MaxValue,
                SentAtMs = 123456789,
                Position = new Vec3(10f, 0.5f, -20f),
                Velocity = new Vec3(-1f, 0f, 30f),
                HeadingRadians = 1.25f,
                Flags = CarStateFlags.Drifting | CarStateFlags.Shielded,
            };

            byte[] bytes = MessageCodec.Encode(original);
            // type(1) + player(1) + sequence(4) + sentAt(8) + position(12) + velocity(12) + heading(4) + flags(1).
            Assert.Equal(43, bytes.Length);

            var decoded = Assert.IsType<CarStateMessage>(MessageCodec.Decode(bytes));
            Assert.Equal(original.PlayerId, decoded.PlayerId);
            Assert.Equal(original.Sequence, decoded.Sequence);
            Assert.Equal(original.SentAtMs, decoded.SentAtMs);
            Assert.Equal(original.Position, decoded.Position);
            Assert.Equal(original.Velocity, decoded.Velocity);
            Assert.Equal(original.HeadingRadians, decoded.HeadingRadians);
            Assert.Equal(original.Flags, decoded.Flags);
        }

        [Fact]
        public void RaceEventMessagesRoundTrip()
        {
            var hit = Assert.IsType<HitMessage>(MessageCodec.Decode(MessageCodec.Encode(new HitMessage
            {
                AttackerId = 1, TargetId = 2, SentAtMs = 99, Impulse = new Vec3(3f, 1f, -4f), IsItemHit = true,
            })));
            Assert.Equal((byte)1, hit.AttackerId);
            Assert.Equal((byte)2, hit.TargetId);
            Assert.Equal(new Vec3(3f, 1f, -4f), hit.Impulse);
            Assert.True(hit.IsItemHit);

            var cp = Assert.IsType<CheckpointReachedMessage>(MessageCodec.Decode(MessageCodec.Encode(new CheckpointReachedMessage
            {
                PlayerId = 4, CheckpointIndex = 7, RaceTimeMs = 65432,
            })));
            Assert.Equal(7, cp.CheckpointIndex);
            Assert.Equal(65432, cp.RaceTimeMs);

            var countdown = Assert.IsType<RaceCountdownMessage>(MessageCodec.Decode(MessageCodec.Encode(new RaceCountdownMessage
            {
                StartAtHostMs = 5000,
                LapCount = 3,
                Mode = GameMode.Battle,
                TrackIndex = 2,
                GridOrder = new List<byte> { 3, 1, 2 },
                Rules = 6,
            })));
            Assert.Equal(5000, countdown.StartAtHostMs);
            Assert.Equal(3, countdown.LapCount);
            Assert.Equal(GameMode.Battle, countdown.Mode);
            Assert.Equal(2, countdown.TrackIndex);
            Assert.Equal(new byte[] { 3, 1, 2 }, countdown.GridOrder);
            Assert.Equal(6, countdown.Rules);

            var finished = Assert.IsType<PlayerFinishedMessage>(MessageCodec.Decode(MessageCodec.Encode(new PlayerFinishedMessage
            {
                PlayerId = 5, FinishPosition = 2, RaceTimeMs = 90000,
            })));
            Assert.Equal(2, finished.FinishPosition);
        }

        [Fact]
        public void EveryMessageTypeHasOneMessageClassThatTheCodecDecodes()
        {
            var messages = typeof(IGameMessage).Assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && typeof(IGameMessage).IsAssignableFrom(t))
                .Select(t => (IGameMessage)Activator.CreateInstance(t))
                .ToList();

            var expectedTypes = Enum.GetValues(typeof(MessageType)).Cast<MessageType>();
            Assert.Equal(expectedTypes, messages.Select(m => m.Type).OrderBy(t => t));
            foreach (IGameMessage message in messages)
            {
                Assert.IsType(message.GetType(), MessageCodec.Decode(MessageCodec.Encode(message)));
            }
        }

        [Fact]
        public void UnknownTypeAndTrailingBytesAreRejected()
        {
            Assert.Throws<NetFormatException>(() => MessageCodec.Decode(new byte[] { 200, 0, 0 }));

            byte[] valid = MessageCodec.Encode(new RaceCountdownMessage { StartAtHostMs = 1, LapCount = 1 });
            byte[] padded = new byte[valid.Length + 1];
            valid.CopyTo(padded, 0);
            Assert.Throws<NetFormatException>(() => MessageCodec.Decode(padded));
        }
    }
}
