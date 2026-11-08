using System;
using System.Collections.Generic;
using System.Linq;
using CarRacer.Core.Net;
using CarRacer.Core.Numerics;
using CarRacer.Core.Session;
using Xunit;

namespace CarRacer.Core.Tests
{
    /// <summary>
    /// The frame around every message on the wire, and the one place a packet from anyone on the LAN is read: it must
    /// either give back a whole message or say why not, and never throw.
    /// </summary>
    public class MessageFrameTests
    {
        private static IEnumerable<IGameMessage> Samples()
        {
            var roster = new LobbyRoster();
            roster.Add("Host", isHost: true);
            roster.Add("Guest");
            yield return roster.ToMessage();
            yield return new JoinRequestMessage { ProtocolVersion = 2, PlayerName = "Zoë's Pixel" };
            yield return new JoinReplyMessage { Result = JoinResult.SessionFull, PlayerId = 4 };
            yield return new ClockPongMessage { GuestSentMs = 5, HostMs = -7 };
            yield return new LobbySettingsMessage { Settings = LobbySettings.Default, SavedCup = 1, SavedCupDone = 4 };
            yield return new CarStateMessage
            {
                PlayerId = 3,
                Sequence = 9,
                SentAtMs = 123456,
                Position = new Vec3(1f, 2f, 3f),
                Velocity = new Vec3(4f, 5f, 6f),
                HeadingRadians = 0.5f,
                Flags = CarStateFlags.Boosting | CarStateFlags.Shielded,
            };
        }

        public static IEnumerable<object[]> SampleFrames() =>
            Samples().Select(message => new object[] { MessageCodec.EncodeFrame(message) });

        private static byte[] Frame(int declaredLength, params byte[] message)
        {
            var writer = new NetWriter();
            writer.WriteInt(declaredLength);
            writer.WriteBytes(message);
            return writer.ToArray();
        }

        [Fact]
        public void AFrameIsTheMessagesLengthAsALittleEndianIntThenTheMessage()
        {
            var message = new ClockPingMessage { GuestSentMs = 0x0102030405060708 };
            byte[] bytes = MessageCodec.Encode(message);

            byte[] frame = MessageCodec.EncodeFrame(message);

            Assert.Equal(new byte[] { (byte)bytes.Length, 0, 0, 0 }.Concat(bytes), frame);
            Assert.Equal(MessageCodec.FrameHeaderBytes + bytes.Length, frame.Length);
        }

        [Fact]
        public void AFrameRoundTrips()
        {
            byte[] frame = MessageCodec.EncodeFrame(new ClockPongMessage { GuestSentMs = 5, HostMs = -7 });

            Assert.True(MessageCodec.TryDecodeFrame(frame, out var message, out string error), error);

            var pong = Assert.IsType<ClockPongMessage>(message);
            Assert.Equal(5, pong.GuestSentMs);
            Assert.Equal(-7, pong.HostMs);
            Assert.Null(error);
        }

        [Fact]
        public void EveryKindOfMessageRoundTripsThroughAFrame()
        {
            foreach (var sent in Samples())
            {
                byte[] frame = MessageCodec.EncodeFrame(sent);

                Assert.True(MessageCodec.TryDecodeFrame(frame, out var received, out string error), error);
                Assert.Equal(MessageCodec.Encode(sent), MessageCodec.Encode(received));
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        [InlineData(MessageCodec.MaxMessageBytes + 1)]
        [InlineData(int.MaxValue)]
        public void ALengthOutsideOneToTheCapIsDropped(int declaredLength)
        {
            byte[] frame = Frame(declaredLength, MessageCodec.Encode(new SetReadyMessage { Ready = true }));

            Assert.False(MessageCodec.TryDecodeFrame(frame, out var message, out string error));
            Assert.Null(message);
            Assert.Contains("outside 1 to 2048", error);
        }

        [Fact]
        public void TheCapIsInclusive()
        {
            // The join handshake is the one message that tolerates bytes it doesn't know, so it pads to any size.
            byte[] join = MessageCodec.Encode(new JoinRequestMessage { ProtocolVersion = 2, PlayerName = "Zoë" });
            byte[] atTheCap = join.Concat(new byte[MessageCodec.MaxMessageBytes - join.Length]).ToArray();
            byte[] overTheCap = atTheCap.Concat(new byte[1]).ToArray();

            byte[] full = Frame(atTheCap.Length, atTheCap);
            byte[] over = Frame(overTheCap.Length, overTheCap);

            Assert.True(MessageCodec.TryDecodeFrame(full, out var message, out string error), error);
            Assert.IsType<JoinRequestMessage>(message);
            Assert.False(MessageCodec.TryDecodeFrame(over, out _, out _));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(MessageCodec.FrameHeaderBytes - 1)]
        public void AFrameTooShortForItsLengthIsDropped(int bytes)
        {
            Assert.False(MessageCodec.TryDecodeFrame(new byte[bytes], out var message, out string error));
            Assert.Null(message);
            Assert.Contains("truncated", error);
        }

        [Fact]
        public void AFrameThatEndsBeforeItsDeclaredLengthIsDropped()
        {
            byte[] frame = MessageCodec.EncodeFrame(new ClockPongMessage { GuestSentMs = 5, HostMs = -7 });
            byte[] cut = frame.Take(frame.Length - 1).ToArray();

            Assert.False(MessageCodec.TryDecodeFrame(cut, out var message, out string error));
            Assert.Null(message);
            Assert.Contains("truncated", error);
        }

        [Fact]
        public void AnUnknownMessageTypeIsDropped()
        {
            Assert.False(MessageCodec.TryDecodeFrame(Frame(1, 200), out var message, out string error));
            Assert.Null(message);
            Assert.Contains("Unknown message type 200", error);
        }

        [Fact]
        public void BytesAfterAMessageThatDoesNotTakeThemAreDropped()
        {
            byte[] ready = MessageCodec.Encode(new SetReadyMessage { Ready = true });
            byte[] padded = ready.Concat(new byte[] { 0 }).ToArray();

            Assert.False(MessageCodec.TryDecodeFrame(Frame(padded.Length, padded), out _, out string error));
            Assert.Contains("trailing", error);
        }

        [Theory]
        [MemberData(nameof(SampleFrames))]
        public void NoCutOfAFrameThrows(byte[] frame)
        {
            for (int length = 0; length < frame.Length; length++)
            {
                byte[] cut = frame.Take(length).ToArray();

                Assert.False(MessageCodec.TryDecodeFrame(cut, out var message, out string error), $"cut at {length}");
                Assert.Null(message);
                Assert.NotEmpty(error);
            }
        }

        [Theory]
        [MemberData(nameof(SampleFrames))]
        public void NoCorruptionOfAFrameThrows(byte[] frame)
        {
            var random = new Random(1234);
            for (int attempt = 0; attempt < 2000; attempt++)
            {
                byte[] corrupted = (byte[])frame.Clone();
                int flips = random.Next(1, 4);
                for (int i = 0; i < flips; i++)
                {
                    corrupted[random.Next(corrupted.Length)] = (byte)random.Next(256);
                }

                bool decoded = MessageCodec.TryDecodeFrame(corrupted, out var message, out string error);
                Assert.Equal(decoded, message != null);
                Assert.Equal(decoded, error == null);
            }
        }

        [Fact]
        public void NoRandomPacketThrows()
        {
            var random = new Random(5678);
            for (int attempt = 0; attempt < 5000; attempt++)
            {
                var packet = new byte[random.Next(0, 64)];
                random.NextBytes(packet);

                // Most random packets fail the length check, so make half plausible to reach the message readers.
                if (packet.Length >= MessageCodec.FrameHeaderBytes + 1 && random.Next(2) == 0)
                {
                    packet[0] = (byte)(packet.Length - MessageCodec.FrameHeaderBytes);
                    packet[1] = packet[2] = packet[3] = 0;
                    packet[MessageCodec.FrameHeaderBytes] = (byte)random.Next(1, 30);
                }

                bool decoded = MessageCodec.TryDecodeFrame(packet, out var message, out string error);
                Assert.Equal(decoded, message != null);
                Assert.Equal(decoded, error == null);
            }
        }
    }
}
