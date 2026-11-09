using System;
using System.Threading;
using World.Blocks;

namespace World.Chunks
{
    /// <summary>
    /// Block storage for one full-height column of the world.
    ///
    /// Stored in horizontal sections 16 blocks tall. A section that is
    /// one block type throughout (the sky above the terrain, solid stone
    /// deep down - about two thirds of every chunk) keeps just that one
    /// value instead of 16 KB of blocks, so a typical chunk needs ~110 KB
    /// instead of 320 KB. <see cref="Compact"/> folds sections after
    /// generation; a uniform section gets its array back as soon as a
    /// different block is written into it.
    ///
    /// Threading: a chunk is filled on a worker thread before it is
    /// published to <see cref="WorldManager"/>. After that, only the
    /// main thread writes to it; mesh workers may read it concurrently.
    /// A read racing a write can at worst produce a stale mesh, which
    /// <see cref="Version"/> detects so the chunk is meshed again.
    /// </summary>
    public sealed class ChunkData
    {
        private const int SectionShift = 4;
        private const int SectionHeight = 1 << SectionShift;   // 16
        private const int SectionMask = SectionHeight - 1;
        private const int MaxPooledSections = 1024;           // 16 MB at 32x32 chunks

        // Freed section arrays, reused by the next chunk (less garbage).
        private static readonly System.Collections.Concurrent.ConcurrentBag<BlockId[]> SectionPool =
            new System.Collections.Concurrent.ConcurrentBag<BlockId[]>();

        private readonly BlockId[][] _sections;   // null = uniform
        private readonly BlockId[] _uniform;      // value of each null section
        private readonly int _sectionSize;
        private readonly byte[] _biomes;
        private int _version;

        public ChunkCoord Coord { get; private set; }

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

            int sectionCount = (sizeY + SectionMask) >> SectionShift;
            _sections = new BlockId[sectionCount][];
            _uniform = new BlockId[sectionCount];
            _sectionSize = sizeX * sizeZ * SectionHeight;
            _biomes = new byte[sizeX * sizeZ];
        }

        public BlockId GetBlock(
            int x,
            int y,
            int z)
        {
            if (!IsInside(x, y, z))
                return BlockId.Air;

            BlockId[] section = _sections[y >> SectionShift];

            return section == null
                ? _uniform[y >> SectionShift]
                : section[x + SizeX * (z + SizeZ * (y & SectionMask))];
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

            int s = y >> SectionShift;
            BlockId[] section = _sections[s];

            if (section == null)
            {
                if (_uniform[s] == block)
                    goto Done;

                // Give the section its own array, filled before it is
                // published so a concurrent reader never sees it half-made.
                section = RentSection(_uniform[s]);
                section[x + SizeX * (z + SizeZ * (y & SectionMask))] = block;
                Volatile.Write(ref _sections[s], section);
                goto Done;
            }

            section[x + SizeX * (z + SizeZ * (y & SectionMask))] = block;

            Done:
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

        /// <summary>
        /// Empties the chunk and gives it a new position, so scratch
        /// chunks (e.g. the half-resolution copies used for distant
        /// meshes) can be reused instead of allocated every time.
        /// Only for chunks that are not published to the world.
        /// </summary>
        public void ResetForReuse(ChunkCoord coord)
        {
            Coord = coord;

            for (int s = 0; s < _sections.Length; s++)
            {
                if (_sections[s] != null)
                    ReturnSection(_sections[s]);

                _sections[s] = null;
                _uniform[s] = BlockId.Air;
            }

            Array.Clear(_biomes, 0, _biomes.Length);
            HighestBlockY = -1;
            IsModified = false;
            Volatile.Write(ref _version, 0);
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

        /// <summary>
        /// Folds sections that contain one block type into a single value.
        /// Call after generation, before the chunk is shared with other
        /// threads (WorldManager does).
        /// </summary>
        public void Compact()
        {
            for (int s = 0; s < _sections.Length; s++)
            {
                BlockId[] section = _sections[s];
                if (section == null)
                    continue;

                BlockId first = section[0];
                bool same = true;

                for (int i = 1; i < section.Length; i++)
                {
                    if (section[i] != first)
                    {
                        same = false;
                        break;
                    }
                }

                if (!same)
                    continue;

                _uniform[s] = first;
                _sections[s] = null;
                ReturnSection(section);
            }
        }

        /// <summary>Bytes used by block storage (for statistics).</summary>
        public int MemoryBytes
        {
            get
            {
                int bytes = _biomes.Length + _sections.Length * 9;
                foreach (BlockId[] section in _sections)
                    if (section != null)
                        bytes += section.Length;
                return bytes;
            }
        }

        private BlockId[] RentSection(BlockId fill)
        {
            if (!SectionPool.TryTake(out BlockId[] section) || section.Length != _sectionSize)
                section = new BlockId[_sectionSize];

            if (fill == BlockId.Air)
                Array.Clear(section, 0, section.Length);
            else
                section.AsSpan().Fill(fill);

            return section;
        }

        private static void ReturnSection(BlockId[] section)
        {
            if (SectionPool.Count < MaxPooledSections)
                SectionPool.Add(section);
        }
    }
}
