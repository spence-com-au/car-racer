using System;
using System.Text;
using CarRacer.Core.Numerics;

namespace CarRacer.Core.Net
{
    /// <summary>
    /// Little-endian binary writer for network packets. Byte order is explicit so
    /// encoding does not depend on the host architecture.
    /// </summary>
    public sealed class NetWriter
    {
        public const int MaxStringBytes = 255;

        private byte[] _buffer;
        private int _length;

        public NetWriter(int initialCapacity = 64)
        {
            _buffer = new byte[Math.Max(initialCapacity, 16)];
        }

        public byte[] ToArray()
        {
            var result = new byte[_length];
            Buffer.BlockCopy(_buffer, 0, result, 0, _length);
            return result;
        }

        public void WriteByte(byte value)
        {
            Ensure(1);
            _buffer[_length++] = value;
        }

        public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

        public void WriteBytes(byte[] bytes)
        {
            Ensure(bytes.Length);
            Buffer.BlockCopy(bytes, 0, _buffer, _length, bytes.Length);
            _length += bytes.Length;
        }

        public void WriteUShort(ushort value)
        {
            Ensure(2);
            _buffer[_length++] = (byte)value;
            _buffer[_length++] = (byte)(value >> 8);
        }

        public void WriteUInt(uint value)
        {
            Ensure(4);
            _buffer[_length++] = (byte)value;
            _buffer[_length++] = (byte)(value >> 8);
            _buffer[_length++] = (byte)(value >> 16);
            _buffer[_length++] = (byte)(value >> 24);
        }

        public void WriteInt(int value) => WriteUInt(unchecked((uint)value));

        public void WriteLong(long value)
        {
            ulong v = unchecked((ulong)value);
            WriteUInt((uint)v);
            WriteUInt((uint)(v >> 32));
        }

        public void WriteFloat(float value) => WriteInt(BitConverter.SingleToInt32Bits(value));

        public void WriteVec3(Vec3 value)
        {
            WriteFloat(value.X);
            WriteFloat(value.Y);
            WriteFloat(value.Z);
        }

        /// <summary>Writes a UTF-8 string prefixed by a one-byte length. Throws if it exceeds 255 bytes.</summary>
        public void WriteString(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            if (bytes.Length > MaxStringBytes)
            {
                throw new ArgumentException($"String is {bytes.Length} bytes; the limit is {MaxStringBytes}.", nameof(value));
            }

            WriteByte((byte)bytes.Length);
            WriteBytes(bytes);
        }

        private void Ensure(int extra)
        {
            if (_length + extra <= _buffer.Length)
            {
                return;
            }

            int capacity = _buffer.Length;
            while (capacity < _length + extra)
            {
                capacity *= 2;
            }

            Array.Resize(ref _buffer, capacity);
        }
    }
}
