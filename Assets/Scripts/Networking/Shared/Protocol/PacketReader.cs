using System;
using System.Buffers.Binary;
using System.Text;

namespace Networking
{
    public sealed class PacketReader
    {
        private readonly byte[] _buffer;
        private int _position;

        public int Position => _position;
        public int Length => _buffer.Length;
        public int Remaining => _buffer.Length - _position;

        public PacketReader(byte[] buffer)
        {
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        }

        private void EnsureReadable(int bytes)
        {
            if (bytes < 0 || bytes > _buffer.Length - _position)
                throw new InvalidOperationException(
                    "Attempted to read past end of packet.");
        }

        public byte ReadByte()
        {
            EnsureReadable(sizeof(byte));
            return _buffer[_position++];
        }

        public bool ReadBool()
        {
            return ReadByte() != 0;
        }

        public short ReadInt16()
        {
            EnsureReadable(sizeof(short));

            short value = BinaryPrimitives.ReadInt16LittleEndian(_buffer.AsSpan(_position));
            _position += sizeof(short);

            return value;
        }

        public ushort ReadUInt16()
        {
            EnsureReadable(sizeof(ushort));

            ushort value = BinaryPrimitives.ReadUInt16LittleEndian(_buffer.AsSpan(_position));
            _position += sizeof(ushort);

            return value;
        }

        public int ReadInt32()
        {
            EnsureReadable(sizeof(int));

            int value = BinaryPrimitives.ReadInt32LittleEndian(_buffer.AsSpan(_position));
            _position += sizeof(int);

            return value;
        }

        public uint ReadUInt32()
        {
            EnsureReadable(sizeof(uint));

            uint value = BinaryPrimitives.ReadUInt32LittleEndian(_buffer.AsSpan(_position));
            _position += sizeof(uint);

            return value;
        }

        public long ReadInt64()
        {
            EnsureReadable(sizeof(long));

            long value = BinaryPrimitives.ReadInt64LittleEndian(_buffer.AsSpan(_position));
            _position += sizeof(long);

            return value;
        }

        public ulong ReadUInt64()
        {
            EnsureReadable(sizeof(ulong));

            ulong value = BinaryPrimitives.ReadUInt64LittleEndian(_buffer.AsSpan(_position));
            _position += sizeof(ulong);

            return value;
        }

        public float ReadFloat()
        {
            return BitConverter.Int32BitsToSingle(ReadInt32());
        }

        public double ReadDouble()
        {
            return BitConverter.Int64BitsToDouble(ReadInt64());
        }

        public byte[] ReadBytes(int length)
        {
            EnsureReadable(length);

            byte[] data = new byte[length];

            Buffer.BlockCopy(_buffer, _position, data, 0, length);

            _position += length;

            return data;
        }

        public string ReadString()
        {
            ushort length = ReadUInt16();

            EnsureReadable(length);

            string value = Encoding.UTF8.GetString(_buffer, _position, length);

            _position += length;

            return value;
        }

        public ReadOnlySpan<byte> ReadSpan(int length)
        {
            EnsureReadable(length);

            ReadOnlySpan<byte> span = _buffer.AsSpan(_position, length);

            _position += length;

            return span;
        }

        public void Skip(int bytes)
        {
            EnsureReadable(bytes);
            _position += bytes;
        }

        public void Seek(int position)
        {
            if (position < 0 || position > _buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(position));

            _position = position;
        }

        public bool CanRead(int bytes)
        {
            return bytes >= 0 && bytes <= _buffer.Length - _position;
        }
    }
}
