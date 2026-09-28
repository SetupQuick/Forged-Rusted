using System;

namespace World.Chunks
{
    public readonly struct ChunkCoord : IEquatable<ChunkCoord>
    {
        public int X { get; }
        public int Z { get; }

        public ChunkCoord(int x, int z)
        {
            X = x;
            Z = z;
        }

        public bool Equals(ChunkCoord other)
        {
            return X == other.X &&
                   Z == other.Z;
        }

        public override bool Equals(object obj)
        {
            return obj is ChunkCoord other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(X, Z);
        }

        public override string ToString()
        {
            return $"({X}, {Z})";
        }

        public static bool operator ==(
            ChunkCoord left,
            ChunkCoord right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            ChunkCoord left,
            ChunkCoord right)
        {
            return !left.Equals(right);
        }
    }
}