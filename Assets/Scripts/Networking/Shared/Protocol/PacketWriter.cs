using System;
using System.Buffers.Binary;
using System.Text;

namespace Networking
{
    public sealed class PacketWriter
    {
        private byte[] _buffer;
        private int _position;

        public int Position => _position;
        public int Length => _position;
        public int Capacity => _buffer.Length;

        public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _position);

        public PacketWriter(int initialCapacity = 256)
        {
            _buffer = new byte[initialCapacity];
        }

        public void Reset()
        {
            _position = 0;
        }

        private void EnsureCapacity(int bytes)
        {
            if (_position + bytes <= _buffer.Length)
                return;

            int newCapacity = Math.Max(_buffer.Length * 2, _position + bytes);
            Array.Resize(ref _buffer, newCapacity);
        }

        public byte[] ToArray()
        {
            byte[] result = new byte[_position];
            Buffer.BlockCopy(_buffer, 0, result, 0, _position);
            return result;
        }

        public void Write(byte value)
        {
            EnsureCapacity(sizeof(byte));
            _buffer[_position++] = value;
        }

        public void Write(bool value)
        {
            Write((byte)(value ? 1 : 0));
        }

        public void Write(short value)
        {
            EnsureCapacity(sizeof(short));
            BinaryPrimitives.WriteInt16LittleEndian(_buffer.AsSpan(_position), value);
            _position += sizeof(short);
        }

        public void Write(ushort value)
        {
            EnsureCapacity(sizeof(ushort));
            BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(_position), value);
            _position += sizeof(ushort);
        }

        public void Write(int value)
        {
            EnsureCapacity(sizeof(int));
            BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(_position), value);
            _position += sizeof(int);
        }

        public void Write(uint value)
        {
            EnsureCapacity(sizeof(uint));
            BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(_position), value);
            _position += sizeof(uint);
        }

        public void Write(long value)
        {
            EnsureCapacity(sizeof(long));
            BinaryPrimitives.WriteInt64LittleEndian(_buffer.AsSpan(_position), value);
            _position += sizeof(long);
        }

        public void Write(ulong value)
        {
            EnsureCapacity(sizeof(ulong));
            BinaryPrimitives.WriteUInt64LittleEndian(_buffer.AsSpan(_position), value);
            _position += sizeof(ulong);
        }

        public void Write(float value)
        {
            Write(BitConverter.SingleToInt32Bits(value));
        }

        public void Write(double value)
        {
            Write(BitConverter.DoubleToInt64Bits(value));
        }

        public void Write(ReadOnlySpan<byte> data)
        {
            EnsureCapacity(data.Length);
            data.CopyTo(_buffer.AsSpan(_position));
            _position += data.Length;
        }

        public void Write(string value)
        {
            value ??= string.Empty;

            int byteCount = Encoding.UTF8.GetByteCount(value);

            if (byteCount > ushort.MaxValue)
                throw new InvalidOperationException("String is too large.");

            EnsureCapacity(sizeof(ushort) + byteCount);

            Write((ushort)byteCount);

            Encoding.UTF8.GetBytes(
                value.AsSpan(),
                _buffer.AsSpan(_position));

            _position += byteCount;
        }
    }
}