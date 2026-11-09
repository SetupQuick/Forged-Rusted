using System;
using System.Collections.Generic;
using World.Blocks;
using World.Chunks;

namespace World.Rendering
{
    /// <summary>
    /// Cheaper meshes for distant chunks ("level of detail 1"):
    ///
    ///   1. Half resolution: every 2x2x2 voxels become one 1 m block,
    ///      so a face covers 4x the area and there are ~4x fewer faces.
    ///      A cell is solid if any of its 8 voxels is solid (terrain never
    ///      gets holes), and takes the block of its highest solid voxel
    ///      (grass stays on top of dirt).
    ///   2. Hidden caves filled: air that cannot be reached from the sky
    ///      inside the chunk is treated as stone. Cave walls are a big
    ///      part of the triangles and can never be seen from far away.
    ///
    /// Chunk borders: the neighbours are downsampled the other way round
    /// (solid only if all 8 voxels are), so a distant chunk always builds
    /// its border faces where a nearby full-detail neighbour might show
    /// air - no see-through gaps where the two detail levels meet.
    ///
    /// The result is meshed by the normal <see cref="ChunkMeshBuilder"/>
    /// with a doubled voxel size. No grass/flowers and no collider (the
    /// player never stands on a distant chunk; chunks near any player are
    /// always full detail). Safe on worker threads; scratch chunks are
    /// reused per thread, so it creates almost no garbage.
    /// </summary>
    public static class ChunkLod
    {
        /// <summary>Fill enclosed caves (on by default; off only for comparisons).</summary>
        public static bool FillCaves = true;

        [ThreadStatic] private static ChunkData[] _scratch;
        [ThreadStatic] private static int[] _queue;
        [ThreadStatic] private static bool[] _reached;

        public static ChunkMeshResult Build(ChunkNeighborhood full, bool buildLiquid, long seed, float voxelSize)
        {
            ChunkData center = full.Center;
            int sx = center.SizeX / 2, sy = center.SizeY / 2, sz = center.SizeZ / 2;

            if (_scratch == null || _scratch[4].SizeX != sx || _scratch[4].SizeY != sy || _scratch[4].SizeZ != sz)
            {
                _scratch = new ChunkData[9];
                for (int i = 0; i < 9; i++)
                    _scratch[i] = new ChunkData(default, sx, sy, sz);
            }

            var chunks = new ChunkData[9];

            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int index = (dx + 1) + (dz + 1) * 3;
                ChunkData source = full.Get(dx, dz);

                if (source == null)
                    continue; // missing neighbours count as solid, as at full detail

                ChunkData target = _scratch[index];
                target.ResetForReuse(source.Coord);
                Downsample(source, target, optimistic: index == 4);
                chunks[index] = target;
            }

            if (FillCaves)
                FillHiddenCaves(chunks[4]);

            ChunkMeshResult result = ChunkMeshBuilder.Build(
                new ChunkNeighborhood(chunks), buildLiquid, seed, voxelSize * 2f, includeDetail: false);

            result.SourceVersion = full.CombinedVersion;
            result.Lod = 1;
            return result;
        }

        /// <param name="optimistic">
        /// true: a cell is solid if any voxel is (the chunk being meshed);
        /// false: only if all are (neighbours, so border faces are kept).
        /// </param>
        private static void Downsample(ChunkData source, ChunkData target, bool optimistic)
        {
            int top = Math.Min(target.SizeY - 1, source.HighestBlockY / 2 + 1);

            for (int z = 0; z < target.SizeZ; z++)
            for (int x = 0; x < target.SizeX; x++)
            {
                target.SetBiome(x, z, source.GetBiome(x * 2, z * 2));

                for (int y = 0; y <= top; y++)
                {
                    BlockId solid = BlockId.Air;
                    BlockId liquid = BlockId.Air;
                    int solidCount = 0;

                    // Higher voxels first, so the surface block wins.
                    for (int oy = 1; oy >= 0; oy--)
                    for (int oz = 0; oz < 2; oz++)
                    for (int ox = 0; ox < 2; ox++)
                    {
                        BlockId block = source.GetBlock(x * 2 + ox, y * 2 + oy, z * 2 + oz);

                        if (BlockRegistry.IsOpaque(block))
                        {
                            if (solid == BlockId.Air)
                                solid = block;
                            solidCount++;
                        }
                        else if (liquid == BlockId.Air && BlockRegistry.IsLiquid(block))
                        {
                            liquid = block;
                        }
                    }

                    BlockId cell =
                        (optimistic ? solidCount > 0 : solidCount == 8) ? solid :
                        liquid != BlockId.Air ? liquid :
                        BlockId.Air;

                    if (cell != BlockId.Air)
                        target.SetBlockRaw(x, y, z, cell);
                }
            }
        }

        /// <summary>
        /// Flood fill from the open sky through non-solid cells; everything
        /// not reached is enclosed and is filled with stone.
        /// </summary>
        private static void FillHiddenCaves(ChunkData chunk)
        {
            int sx = chunk.SizeX, sy = chunk.SizeY, sz = chunk.SizeZ;
            int top = chunk.HighestBlockY;
            if (top < 0)
                return;

            int count = sx * sz * (top + 1);
            if (_reached == null || _reached.Length < count)
            {
                _reached = new bool[count];
                _queue = new int[count];
            }
            else
            {
                Array.Clear(_reached, 0, count);
            }

            int head = 0, tail = 0;
            int Index(int x, int y, int z) => x + sx * (z + sz * y);

            // Seeds: the open (non-solid) cells of the top layer. Everything
            // above the highest block is open sky anyway.
            for (int z = 0; z < sz; z++)
            for (int x = 0; x < sx; x++)
            {
                if (!BlockRegistry.IsOpaque(chunk.GetBlock(x, top, z)))
                {
                    int i = Index(x, top, z);
                    _reached[i] = true;
                    _queue[tail++] = i;
                }
            }

            while (head < tail)
            {
                int i = _queue[head++];
                int x = i % sx;
                int z = (i / sx) % sz;
                int y = i / (sx * sz);

                Visit(x - 1, y, z); Visit(x + 1, y, z);
                Visit(x, y - 1, z); Visit(x, y + 1, z);
                Visit(x, y, z - 1); Visit(x, y, z + 1);
            }

            void Visit(int x, int y, int z)
            {
                if (x < 0 || y < 0 || z < 0 || x >= sx || y > top || z >= sz)
                    return;

                int i = Index(x, y, z);
                if (_reached[i] || BlockRegistry.IsOpaque(chunk.GetBlock(x, y, z)))
                    return;

                _reached[i] = true;
                _queue[tail++] = i;
            }

            // Fill enclosed air (enclosed water is left alone: lakes in caves are rare).
            for (int y = 0; y <= top; y++)
            for (int z = 0; z < sz; z++)
            for (int x = 0; x < sx; x++)
            {
                if (!_reached[Index(x, y, z)] && chunk.GetBlock(x, y, z) == BlockId.Air)
                    chunk.SetBlockRaw(x, y, z, BlockId.Stone);
            }
        }
    }
}
