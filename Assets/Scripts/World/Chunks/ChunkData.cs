using System;
using World.Blocks;

namespace World.Chunks
{
    public sealed class ChunkData
    {
        private readonly BlockId[] _blocks;

        public ChunkCoord Coord { get; }

        public int SizeX { get; }
        public int SizeY { get; }
        public int SizeZ { get; }

        public ChunkData(
            ChunkCoord coord,
            int sizeX,
            int sizeY,
            int sizeZ)
        {
            if (sizeX <= 0)
                throw new ArgumentOutOfRangeException(nameof(sizeX));

            if (sizeY <= 0)
                throw new ArgumentOutOfRangeException(nameof(sizeY));

            if (sizeZ <= 0)
                throw new ArgumentOutOfRangeException(nameof(sizeZ));

            Coord = coord;

            SizeX = sizeX;
            SizeY = sizeY;
            SizeZ = sizeZ;

            _blocks =
                new BlockId[
                    sizeX *
                    sizeY *
                    sizeZ];
        }

        public BlockId GetBlock(
            int x,
            int y,
            int z)
        {
            if (!IsInside(x, y, z))
                return BlockId.Air;

            return _blocks[
                GetIndex(x, y, z)];
        }

        public void SetBlock(
            int x,
            int y,
            int z,
            BlockId block)
        {
            if (!IsInside(x, y, z))
            {
                throw new ArgumentOutOfRangeException(
                    $"Block position ({x}, {y}, {z}) " +
                    $"is outside chunk {Coord}.");
            }

            _blocks[
                GetIndex(x, y, z)] = block;
        }

        public bool IsInside(
            int x,
            int y,
            int z)
        {
            return
                x >= 0 &&
                x < SizeX &&
                y >= 0 &&
                y < SizeY &&
                z >= 0 &&
                z < SizeZ;
        }

        private int GetIndex(
            int x,
            int y,
            int z)
        {
            return
                x +
                SizeX *
                (z + SizeZ * y);
        }
    }
}