using System;
using System.IO;
using System.Text;

namespace CarRacer.Core.Cast
{
    /// <summary>
    /// One Cast v2 message (design doc §2.4): the protobuf <c>CastMessage</c> a Chromecast speaks
    /// over TLS on port 8009, with a UTF-8 JSON payload. On the wire each is a 4-byte big-endian
    /// length and then the protobuf. Only string payloads are used; binary ones are read and ignored.
    /// </summary>
    public sealed class CastMessage
    {
        public const string ConnectionNamespace = "urn:x-cast:com.google.cast.tp.connection";
        public const string HeartbeatNamespace = "urn:x-cast:com.google.cast.tp.heartbeat";
        public const string ReceiverNamespace = "urn:x-cast:com.google.cast.receiver";
        public const string MediaNamespace = "urn:x-cast:com.google.cast.media";

        /// <summary>Messages bigger than this are refused, so a bad length can't take all the memory.</summary>
        public const int MaxBytes = 64 * 1024;

        public string SourceId = string.Empty;
        public string DestinationId = string.Empty;
        public string Namespace = string.Empty;

        /// <summary>The JSON payload; null if the message carried a binary one.</summary>
        public string Payload;

        public CastMessage()
        {
        }

        public CastMessage(string sourceId, string destinationId, string ns, string payload)
        {
            SourceId = sourceId;
            DestinationId = destinationId;
            Namespace = ns;
            Payload = payload;
        }

        /// <summary>The payload's "type" ("PING", "RECEIVER_STATUS"…), or null.</summary>
        public string PayloadType => Json.String(Json.TryParseObject(Payload ?? string.Empty), "type");

        /// <summary>The protobuf, without the length prefix.</summary>
        public byte[] Encode()
        {
            var stream = new MemoryStream();
            WriteVarintField(stream, 1, 0); // protocol_version: CASTV2_1_0
            WriteStringField(stream, 2, SourceId);
            WriteStringField(stream, 3, DestinationId);
            WriteStringField(stream, 4, Namespace);
            WriteVarintField(stream, 5, 0); // payload_type: STRING
            WriteStringField(stream, 6, Payload ?? string.Empty);
            return stream.ToArray();
        }

        /// <summary>The length prefix and the protobuf, ready to write to the connection.</summary>
        public byte[] Frame()
        {
            byte[] body = Encode();
            var frame = new byte[4 + body.Length];
            frame[0] = (byte)(body.Length >> 24);
            frame[1] = (byte)(body.Length >> 16);
            frame[2] = (byte)(body.Length >> 8);
            frame[3] = (byte)body.Length;
            Buffer.BlockCopy(body, 0, frame, 4, body.Length);
            return frame;
        }

        /// <summary>Reads a protobuf <c>CastMessage</c>; throws <see cref="FormatException"/> if it's malformed.</summary>
        public static CastMessage Decode(byte[] data, int offset, int count)
        {
            var message = new CastMessage();
            int i = offset;
            int end = offset + count;
            while (i < end)
            {
                ulong key = ReadVarint(data, ref i, end);
                int field = (int)(key >> 3);
                int wire = (int)(key & 7);
                switch (wire)
                {
                    case 0:
                        ReadVarint(data, ref i, end);
                        break;
                    case 1:
                        i += 8;
                        break;
                    case 5:
                        i += 4;
                        break;
                    case 2:
                        ulong length = ReadVarint(data, ref i, end);
                        if (length > (ulong)(end - i))
                        {
                            throw new FormatException("Field runs past the message");
                        }

                        string text = field == 2 || field == 3 || field == 4 || field == 6 ? Encoding.UTF8.GetString(data, i, (int)length) : null;
                        switch (field)
                        {
                            case 2:
                                message.SourceId = text;
                                break;
                            case 3:
                                message.DestinationId = text;
                                break;
                            case 4:
                                message.Namespace = text;
                                break;
                            case 6:
                                message.Payload = text;
                                break;
                        }

                        i += (int)length;
                        break;
                    default:
                        throw new FormatException("Unsupported wire type " + wire);
                }
            }

            if (i != end)
            {
                throw new FormatException("Field runs past the message");
            }

            return message;
        }

        /// <summary>
        /// Reads one framed message from <paramref name="stream"/>, blocking; null if the stream
        /// ended cleanly between messages.
        /// </summary>
        public static CastMessage ReadFrom(Stream stream)
        {
            var header = new byte[4];
            if (!ReadExactly(stream, header, 4, allowEnd: true))
            {
                return null;
            }

            int length = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
            if (length < 0 || length > MaxBytes)
            {
                throw new FormatException("Cast message of " + length + " bytes");
            }

            var body = new byte[length];
            ReadExactly(stream, body, length, allowEnd: false);
            return Decode(body, 0, length);
        }

        public override string ToString() => $"{SourceId} → {DestinationId} [{Namespace}] {Payload}";

        private static bool ReadExactly(Stream stream, byte[] buffer, int count, bool allowEnd)
        {
            int read = 0;
            while (read < count)
            {
                int n = stream.Read(buffer, read, count - read);
                if (n <= 0)
                {
                    if (allowEnd && read == 0)
                    {
                        return false;
                    }

                    throw new EndOfStreamException("The Cast connection closed mid-message");
                }

                read += n;
            }

            return true;
        }

        private static void WriteVarintField(Stream stream, int field, ulong value)
        {
            WriteVarint(stream, (ulong)(field << 3));
            WriteVarint(stream, value);
        }

        private static void WriteStringField(Stream stream, int field, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            WriteVarint(stream, (ulong)((field << 3) | 2));
            WriteVarint(stream, (ulong)bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static void WriteVarint(Stream stream, ulong value)
        {
            while (value >= 0x80)
            {
                stream.WriteByte((byte)(value | 0x80));
                value >>= 7;
            }

            stream.WriteByte((byte)value);
        }

        private static ulong ReadVarint(byte[] data, ref int i, int end)
        {
            ulong value = 0;
            for (int shift = 0; shift < 64; shift += 7)
            {
                if (i >= end)
                {
                    throw new FormatException("Varint runs past the message");
                }

                byte b = data[i++];
                value |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                {
                    return value;
                }
            }

            throw new FormatException("Varint too long");
        }
    }
}
