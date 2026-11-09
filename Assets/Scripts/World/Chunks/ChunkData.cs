using System;
using System.Threading;
using World.Blocks;

namespace World.Chunks
{
    /// <summary>
    /// Block storage for one full-height column of the world.
    ///
    /// Threading: a chunk is filled on a worker thread before it is
    /// published to <see cref="WorldManager"/>. After that, only the
    /// main thread writes to it; mesh workers may read it concurrently.
    /// A read racing a write can at worst produce a stale mesh, which
    /// <see cref="Version"/> detects so the chunk is meshed again.
    /// </summary>
    public sealed class ChunkData
    {
        private readonly BlockId[] _blocks;
        private readonly byte[] _biomes;
        private int _version;

        public ChunkCoord Coord { get; }

        public int SizeX { get; }
        public int SizeY { get; }
        public int SizeZ { get; }

        /// <summary>
        /// Highest Y that contains a non-air block (-1 when empty).
        /// Lets meshing skip the empty sky above the terrain.
        /// </summary>
        public int HighestBlockY { get; private set; } = -1;

        /// <summary>Incremented on every change after generation.</summary>
        public int Version => Volatile.Read(ref _version);

        /// <summary>
        /// True once a player changed a block. Modified chunks are kept
        /// in memory (and later saved) instead of being regenerated.
        /// </summary>
        public bool IsModified { get; private set; }

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

            _blocks = new BlockId[sizeX * sizeY * sizeZ];
            _biomes = new byte[sizeX * sizeZ];
        }

        public BlockId GetBlock(
            int x,
            int y,
            int z)
        {
            if (!IsInside(x, y, z))
                return BlockId.Air;

            return _blocks[GetIndex(x, y, z)];
        }

        /// <summary>
        /// Gameplay write (block placed or broken). Marks the chunk as
        /// modified and bumps its version.
        /// </summary>
        public void SetBlock(
            int x,
            int y,
            int z,
            BlockId block)
        {
            SetBlockRaw(x, y, z, block);
            IsModified = true;
            Interlocked.Increment(ref _version);
        }

        /// <summary>
        /// Generation write. Does not mark the chunk as modified.
        /// </summary>
        public void SetBlockRaw(
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

            _blocks[GetIndex(x, y, z)] = block;

            if (block != BlockId.Air && y > HighestBlockY)
                HighestBlockY = y;
        }

        /// <summary>Generation helper: sets a block only if the position is inside.</summary>
        public bool TrySetBlockRaw(int x, int y, int z, BlockId block)
        {
            if (!IsInside(x, y, z))
                return false;

            SetBlockRaw(x, y, z, block);
            return true;
        }

        /// <summary>Biome of a column, as the raw byte of World.Generation.BiomeId.</summary>
        public byte GetBiome(int x, int z)
        {
            if (x < 0 || x >= SizeX || z < 0 || z >= SizeZ)
                return 0;

            return _biomes[x + z * SizeX];
        }

        public void SetBiome(int x, int z, byte biome)
        {
            if (x < 0 || x >= SizeX || z < 0 || z >= SizeZ)
                return;

            _biomes[x + z * SizeX] = biome;
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
