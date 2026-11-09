using World.Blocks;
using World.Chunks;
using World.Generation.Noise;

namespace World.Generation.Structures
{
    /// <summary>
    /// Trees, bushes, cacti and boulders.
    ///
    /// The world is split into a grid of small cells. Each cell holds at
    /// most one feature, at a position and of a kind chosen by hashing
    /// the cell coordinates. Because that choice is a pure function of
    /// the seed and cell, every chunk that overlaps a feature agrees on
    /// it, and features crossing chunk borders line up.
    /// </summary>
    public sealed class SurfaceFeatureGenerator : IStructureGenerator
    {
        private const int CellSize = 5;
        private const int Reach = 3;

        private enum Feature
        {
            None,
            Oak,
            TallOak,
            Pine,
            Acacia,
            Bush,
            Cactus,
            Boulder
        }

        private readonly FractalNoise _forestDensity;

        public int MaxHorizontalReach => Reach;

        public SurfaceFeatureGenerator(TerrainConfig config)
        {
            _forestDensity = new FractalNoise(SeedHash.Derive(config.Seed, 301), 180, 2);
        }

        public void Generate(ChunkGenerationContext context)
        {
            ChunkData chunk = context.Chunk;

            int minX = context.MinWorldX - Reach;
            int minZ = context.MinWorldZ - Reach;
            int maxX = context.MinWorldX + chunk.SizeX - 1 + Reach;
            int maxZ = context.MinWorldZ + chunk.SizeZ - 1 + Reach;

            int minCellX = CaveCarver.FloorDiv(minX, CellSize);
            int minCellZ = CaveCarver.FloorDiv(minZ, CellSize);
            int maxCellX = CaveCarver.FloorDiv(maxX, CellSize);
            int maxCellZ = CaveCarver.FloorDiv(maxZ, CellSize);

            for (int cellZ = minCellZ; cellZ <= maxCellZ; cellZ++)
            for (int cellX = minCellX; cellX <= maxCellX; cellX++)
            {
                ulong hash = SeedHash.Hash(context.Config.Seed, cellX, cellZ, 0x7EE5);
                var random = new WorldRandom(hash);

                int x = cellX * CellSize + random.Range(0, CellSize);
                int z = cellZ * CellSize + random.Range(0, CellSize);

                if (x < minX || x > maxX || z < minZ || z > maxZ)
                    continue;

                Feature feature = ChooseFeature(context, x, z, ref random);

                if (feature == Feature.None)
                    continue;

                int groundY = context.GetColumn(x, z).Height;
                Place(context, feature, x, groundY, z, ref random);
            }
        }

        private Feature ChooseFeature(
            ChunkGenerationContext context,
            int x,
            int z,
            ref WorldRandom random)
        {
            TerrainConfig config = context.Config;
            ColumnSample column = context.GetColumn(x, z);

            if (column.Height < config.SeaLevel + 1 ||
                column.Height >= config.WorldHeight - 20)
            {
                return Feature.None;
            }

            if (context.GetSlope(x, z) > 1 || context.IsSurfaceCarved(x, z))
                return Feature.None;

            BlockId surface = context.GetSurfaceBlock(x, z);
            double roll = random.NextDouble();
            double density = config.TreeDensity;

            // 0..1 patchiness so forests have clearings and groves.
            double grove = TerrainSampler.SmoothStep(-0.35, 0.35, _forestDensity.Sample(x, z));

            if (surface == BlockId.Sand)
            {
                if (column.Biome == BiomeId.Desert && roll < 0.05 * density)
                    return Feature.Cactus;

                return Feature.None;
            }

            if (surface == BlockId.Stone || surface == BlockId.Gravel)
                return roll < 0.03 * density ? Feature.Boulder : Feature.None;

            if (surface != BlockId.Grass && surface != BlockId.Snow)
                return Feature.None;

            switch (column.Biome)
            {
                case BiomeId.Forest:
                    if (roll < (0.25 + 0.6 * grove) * density)
                        return random.Chance(0.2) ? Feature.TallOak : Feature.Oak;
                    if (roll < (0.32 + 0.6 * grove) * density)
                        return Feature.Bush;
                    return Feature.None;

                case BiomeId.Plains:
                    if (roll < 0.035 * grove * density) return Feature.Oak;
                    if (roll < 0.08 * density) return Feature.Bush;
                    if (roll < 0.09 * density) return Feature.Boulder;
                    return Feature.None;

                case BiomeId.Savanna:
                    if (roll < 0.06 * density) return Feature.Acacia;
                    if (roll < 0.10 * density) return Feature.Bush;
                    return Feature.None;

                case BiomeId.Taiga:
                    if (roll < (0.3 + 0.5 * grove) * density) return Feature.Pine;
                    if (roll < (0.34 + 0.5 * grove) * density) return Feature.Boulder;
                    return Feature.None;

                case BiomeId.Tundra:
                    if (roll < 0.02 * density) return Feature.Pine;
                    if (roll < 0.04 * density) return Feature.Boulder;
                    return Feature.None;

                case BiomeId.Swamp:
                    if (roll < 0.18 * density) return Feature.Oak;
                    if (roll < 0.28 * density) return Feature.Bush;
                    return Feature.None;

                case BiomeId.Mountains:
                    if (column.Height < config.SeaLevel + 70 && roll < 0.12 * density)
                        return Feature.Pine;
                    return Feature.None;

                default:
                    return Feature.None;
            }
        }

        private static void Place(
            ChunkGenerationContext context,
            Feature feature,
            int x,
            int groundY,
            int z,
            ref WorldRandom random)
        {
            switch (feature)
            {
                case Feature.Oak:
                    PlaceRoundTree(context, x, groundY, z, random.Range(4, 6), 2, ref random);
                    break;

                case Feature.TallOak:
                    PlaceRoundTree(context, x, groundY, z, random.Range(6, 9), 2, ref random);
                    break;

                case Feature.Pine:
                    PlacePine(context, x, groundY, z, random.Range(6, 11), ref random);
                    break;

                case Feature.Acacia:
                    PlaceAcacia(context, x, groundY, z, ref random);
                    break;

                case Feature.Bush:
                    PlaceBush(context, x, groundY, z, ref random);
                    break;

                case Feature.Cactus:
                    PlaceCactus(context, x, groundY, z, ref random);
                    break;

                case Feature.Boulder:
                    PlaceBoulder(context, x, groundY, z, ref random);
                    break;
            }
        }

        private static void PlaceRoundTree(
            ChunkGenerationContext context,
            int x,
            int groundY,
            int z,
            int trunkHeight,
            int radius,
            ref WorldRandom random)
        {
            SetGroundUnderTrunk(context, x, groundY, z);

            int top = groundY + trunkHeight;

            for (int y = groundY + 1; y <= top; y++)
                SetLog(context, x, y, z, BlockId.Log);

            // Canopy: two wide layers, then two narrow ones.
            for (int dy = -2; dy <= 1; dy++)
            {
                int r = dy <= -1 ? radius : radius - 1;
                int y = top + dy;

                for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    bool corner = (dx == -r || dx == r) && (dz == -r || dz == r);

                    // Randomly trim corners so canopies are not perfect cubes.
                    if (corner && (dy == 1 || (Hash(context, x + dx, y, z + dz) & 1) == 0))
                        continue;

                    SetLeaves(context, x + dx, y, z + dz, BlockId.Leaves);
                }
            }

            SetLeaves(context, x, top + 2, z, BlockId.Leaves);
        }

        private static void PlacePine(
            ChunkGenerationContext context,
            int x,
            int groundY,
            int z,
            int height,
            ref WorldRandom random)
        {
            SetGroundUnderTrunk(context, x, groundY, z);

            int top = groundY + height;

            for (int y = groundY + 1; y < top; y++)
                SetLog(context, x, y, z, BlockId.PineLog);

            // Cone of alternating rings, widest near the bottom.
            int leafStart = groundY + 2 + random.Range(0, 2);
            int radius = 0;

            for (int y = top; y >= leafStart; y--)
            {
                int r = (top - y) % 2 == 0 ? radius : radius - 1;
                if (r < 0) r = 0;

                for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    if (r > 0 && dx * dx + dz * dz > r * r + 1)
                        continue;

                    SetLeaves(context, x + dx, y, z + dz, BlockId.PineLeaves);
                }

                if ((top - y) % 2 == 1 && radius < Reach)
                    radius++;
            }

            SetLeaves(context, x, top + 1, z, BlockId.PineLeaves);
        }

        private static void PlaceAcacia(
            ChunkGenerationContext context,
            int x,
            int groundY,
            int z,
            ref WorldRandom random)
        {
            SetGroundUnderTrunk(context, x, groundY, z);

            int height = random.Range(4, 7);
            int leanX = random.Range(-1, 2);
            int leanZ = random.Range(-1, 2);

            int cx = x;
            int cz = z;

            for (int i = 1; i <= height; i++)
            {
                if (i == height - 1)
                {
                    cx += leanX;
                    cz += leanZ;
                }

                SetLog(context, cx, groundY + i, cz, BlockId.Log);
            }

            int top = groundY + height;

            // Flat, wide canopy. Radius 2 plus the lean of 1 stays
            // within MaxHorizontalReach.
            for (int dx = -2; dx <= 2; dx++)
            for (int dz = -2; dz <= 2; dz++)
            {
                int d = (dx < 0 ? -dx : dx) + (dz < 0 ? -dz : dz);

                if (d <= 3)
                    SetLeaves(context, cx + dx, top + 1, cz + dz, BlockId.Leaves);

                if (d <= 1)
                    SetLeaves(context, cx + dx, top + 2, cz + dz, BlockId.Leaves);
            }
        }

        private static void PlaceBush(
            ChunkGenerationContext context,
            int x,
            int groundY,
            int z,
            ref WorldRandom random)
        {
            SetLog(context, x, groundY + 1, z, BlockId.Log);

            for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (dx != 0 && dz != 0 && random.Chance(0.5))
                    continue;

                SetLeaves(context, x + dx, groundY + 1, z + dz, BlockId.Leaves);
            }

            SetLeaves(context, x, groundY + 2, z, BlockId.Leaves);
        }

        private static void PlaceCactus(
            ChunkGenerationContext context,
            int x,
            int groundY,
            int z,
            ref WorldRandom random)
        {
            int height = random.Range(2, 5);

            for (int y = groundY + 1; y <= groundY + height; y++)
                SetLog(context, x, y, z, BlockId.Cactus);

            // Occasional arm.
            if (height >= 3 && random.Chance(0.5))
            {
                int side = random.Range(0, 4);
                int ax = side == 0 ? 1 : side == 1 ? -1 : 0;
                int az = side == 2 ? 1 : side == 3 ? -1 : 0;
                int armY = groundY + 2;

                SetLog(context, x + ax, armY, z + az, BlockId.Cactus);
                SetLog(context, x + ax, armY + 1, z + az, BlockId.Cactus);
            }
        }

        private static void PlaceBoulder(
            ChunkGenerationContext context,
            int x,
            int groundY,
            int z,
            ref WorldRandom random)
        {
            int radius = random.Range(1, 3);
            BlockId block = random.Chance(0.25) ? BlockId.MossyStone : BlockId.Stone;

            for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -1; dy <= radius; dy++)
            for (int dz = -radius; dz <= radius; dz++)
            {
                int distance = dx * dx + dy * dy * 2 + dz * dz;

                if (distance > radius * radius + 1)
                    continue;

                PlaceIfReplaceable(context, x + dx, groundY + dy, z + dz, block, replaceGround: dy <= 0);
            }
        }

        private static void SetGroundUnderTrunk(ChunkGenerationContext context, int x, int groundY, int z)
        {
            if (!ToLocal(context, x, z, out int lx, out int lz))
                return;

            if (context.Chunk.GetBlock(lx, groundY, lz) == BlockId.Grass)
                context.Chunk.SetBlockRaw(lx, groundY, lz, BlockId.Dirt);
        }

        private static void SetLog(ChunkGenerationContext context, int x, int y, int z, BlockId block)
        {
            PlaceIfReplaceable(context, x, y, z, block, replaceGround: false, replaceLeaves: true);
        }

        private static void SetLeaves(ChunkGenerationContext context, int x, int y, int z, BlockId block)
        {
            PlaceIfReplaceable(context, x, y, z, block, replaceGround: false);
        }

        private static void PlaceIfReplaceable(
            ChunkGenerationContext context,
            int x,
            int y,
            int z,
            BlockId block,
            bool replaceGround,
            bool replaceLeaves = false)
        {
            if (!ToLocal(context, x, z, out int lx, out int lz))
                return;

            ChunkData chunk = context.Chunk;

            if (y <= 0 || y >= chunk.SizeY)
                return;

            BlockId existing = chunk.GetBlock(lx, y, lz);

            bool replaceable =
                existing == BlockId.Air ||
                (replaceLeaves && (existing == BlockId.Leaves || existing == BlockId.PineLeaves)) ||
                (replaceGround && (existing == BlockId.Grass || existing == BlockId.Dirt ||
                                   existing == BlockId.Snow || existing == BlockId.Stone));

            if (replaceable)
                chunk.SetBlockRaw(lx, y, lz, block);
        }

        private static bool ToLocal(ChunkGenerationContext context, int x, int z, out int lx, out int lz)
        {
            lx = x - context.MinWorldX;
            lz = z - context.MinWorldZ;

            return lx >= 0 && lz >= 0 && lx < context.Chunk.SizeX && lz < context.Chunk.SizeZ;
        }

        private static ulong Hash(ChunkGenerationContext context, int x, int y, int z)
        {
            return SeedHash.Hash(context.Config.Seed, x, y, z, 0x1EAF);
        }
    }
}
