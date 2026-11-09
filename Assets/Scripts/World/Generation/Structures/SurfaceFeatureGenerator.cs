using System;
using World.Blocks;
using World.Chunks;
using World.Generation.Noise;

namespace World.Generation.Structures
{
    /// <summary>
    /// Trees, bushes, cacti and boulders, sized in metres so they keep
    /// their proportions at any voxel size. With small voxels they get
    /// lumpy round canopies built from several overlapping blobs.
    ///
    /// The world is split into a grid of cells. Each cell holds at most
    /// one feature, at a position and of a kind chosen by hashing the cell
    /// coordinates. Because that choice is a pure function of the seed and
    /// cell, every chunk that overlaps a feature agrees on it, and features
    /// crossing chunk borders line up.
    /// </summary>
    public sealed class SurfaceFeatureGenerator : IStructureGenerator
    {
        private enum Feature
        {
            None,
            RoundTree,
            BigRoundTree,
            Pine,
            Acacia,
            Bush,
            Cactus,
            Boulder
        }

        // 8 horizontal directions. A table instead of sin/cos, because
        // trig functions may differ in the last bit between platforms and
        // the Windows client must generate exactly what the Linux server does.
        private static readonly double[] DirectionX = { 1, 0.7071, 0, -0.7071, -1, -0.7071, 0, 0.7071 };
        private static readonly double[] DirectionZ = { 0, 0.7071, 1, 0.7071, 0, -0.7071, -1, -0.7071 };

        private readonly FractalNoise _forestDensity;
        private readonly double _metres;
        private readonly int _cellSize;
        private readonly int _reach;
        private readonly double _densityPerCell;

        // Free space (in voxels) kept under tree canopies so a 2 m
        // player can walk underneath without catching on leaves.
        private readonly int _clearance;

        public int MaxHorizontalReach => _reach;

        public SurfaceFeatureGenerator(TerrainConfig config)
        {
            _metres = config.FeatureScale;
            _forestDensity = new FractalNoise(SeedHash.Derive(config.Seed, 301), 180 * _metres, 2);

            // One candidate per 4 m cell; probabilities below are per
            // 5 m x 5 m, so scale them to the cell area.
            _cellSize = Math.Max(3, (int)Math.Round(4 * _metres));
            double cellMetres = _cellSize / _metres;
            _densityPerCell = cellMetres * cellMetres / 25.0;

            // Largest canopy radius plus side blobs (see PlaceRoundTree).
            _reach = (int)Math.Ceiling(4.5 * _metres);
            _clearance = (int)Math.Ceiling(2.4 * _metres);
        }

        public void Generate(ChunkGenerationContext context)
        {
            ChunkData chunk = context.Chunk;

            int minX = context.MinWorldX - _reach;
            int minZ = context.MinWorldZ - _reach;
            int maxX = context.MinWorldX + chunk.SizeX - 1 + _reach;
            int maxZ = context.MinWorldZ + chunk.SizeZ - 1 + _reach;

            int minCellX = CaveCarver.FloorDiv(minX, _cellSize);
            int minCellZ = CaveCarver.FloorDiv(minZ, _cellSize);
            int maxCellX = CaveCarver.FloorDiv(maxX, _cellSize);
            int maxCellZ = CaveCarver.FloorDiv(maxZ, _cellSize);

            for (int cellZ = minCellZ; cellZ <= maxCellZ; cellZ++)
            for (int cellX = minCellX; cellX <= maxCellX; cellX++)
            {
                ulong hash = SeedHash.Hash(context.Config.Seed, cellX, cellZ, 0x7EE5);
                var random = new WorldRandom(hash);

                int x = cellX * _cellSize + random.Range(0, _cellSize);
                int z = cellZ * _cellSize + random.Range(0, _cellSize);

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
                column.Height >= config.WorldHeight - 16 * _metres)
            {
                return Feature.None;
            }

            if (context.GetSlope(x, z) > Math.Max(1, (int)_metres) || context.IsSurfaceCarved(x, z))
                return Feature.None;

            BlockId surface = context.GetSurfaceBlock(x, z);
            double roll = random.NextDouble() / _densityPerCell;
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
                    if (roll < (0.22 + 0.5 * grove) * density)
                        return random.Chance(0.25) ? Feature.BigRoundTree : Feature.RoundTree;
                    if (roll < (0.30 + 0.5 * grove) * density)
                        return Feature.Bush;
                    if (roll < (0.31 + 0.5 * grove) * density)
                        return Feature.Boulder;
                    return Feature.None;

                case BiomeId.Plains:
                    if (roll < 0.04 * grove * density) return Feature.RoundTree;
                    if (roll < 0.09 * density) return Feature.Bush;
                    if (roll < 0.10 * density) return Feature.Boulder;
                    return Feature.None;

                case BiomeId.Savanna:
                    if (roll < 0.06 * density) return Feature.Acacia;
                    if (roll < 0.10 * density) return Feature.Bush;
                    return Feature.None;

                case BiomeId.Taiga:
                    if (roll < (0.28 + 0.45 * grove) * density) return Feature.Pine;
                    if (roll < (0.32 + 0.45 * grove) * density) return Feature.Boulder;
                    return Feature.None;

                case BiomeId.Tundra:
                    if (roll < 0.02 * density) return Feature.Pine;
                    if (roll < 0.04 * density) return Feature.Boulder;
                    return Feature.None;

                case BiomeId.Swamp:
                    if (roll < 0.16 * density) return Feature.RoundTree;
                    if (roll < 0.28 * density) return Feature.Bush;
                    return Feature.None;

                case BiomeId.Mountains:
                    if (column.Height < config.SeaLevel + 70 * _metres && roll < 0.12 * density)
                        return Feature.Pine;
                    return Feature.None;

                default:
                    return Feature.None;
            }
        }

        private void Place(
            ChunkGenerationContext context,
            Feature feature,
            int x,
            int groundY,
            int z,
            ref WorldRandom random)
        {
            switch (feature)
            {
                case Feature.RoundTree:
                    PlaceRoundTree(context, x, groundY, z, 1.0, ref random);
                    break;

                case Feature.BigRoundTree:
                    PlaceRoundTree(context, x, groundY, z, 1.25, ref random);
                    break;

                case Feature.Pine:
                    PlacePine(context, x, groundY, z, ref random);
                    break;

                case Feature.Acacia:
                    PlaceAcacia(context, x, groundY, z, ref random);
                    break;

                case Feature.Bush:
                    PlaceBlob(context, x, groundY + 1, z, M(0.6 + random.NextDouble() * 0.5), 0.8, PickLeaves(context, x, z, ref random), 0x8053, clearance: 0);
                    break;

                case Feature.Cactus:
                    PlaceCactus(context, x, groundY, z, ref random);
                    break;

                case Feature.Boulder:
                    PlaceBoulder(context, x, groundY, z, ref random);
                    break;
            }
        }

        private double M(double metres) => metres * _metres;

        private BlockId PickLeaves(ChunkGenerationContext context, int x, int z, ref WorldRandom random)
        {
            BiomeId biome = context.GetColumn(x, z).Biome;

            if (biome == BiomeId.Forest)
            {
                double roll = random.NextDouble();
                if (roll < 0.18) return BlockId.AutumnLeaves;
                if (roll < 0.28) return BlockId.RedLeaves;
            }
            else
            {
                random.NextDouble(); // keep the random sequence the same length
            }

            return BlockId.Leaves;
        }

        private void PlaceRoundTree(
            ChunkGenerationContext context,
            int x,
            int groundY,
            int z,
            double size,
            ref WorldRandom random)
        {
            BlockId leaves = PickLeaves(context, x, z, ref random);
            SetGroundUnderTrunk(context, x, groundY, z);

            // Short, sturdy trunks under big, low canopies.
            int trunk = (int)M((2.2 + random.NextDouble() * 1.2) * size);
            int top = groundY + trunk;
            bool thick = size > 1.2 || random.Chance(0.35);

            for (int y = groundY + 1; y <= top; y++)
            {
                SetLog(context, x, y, z, BlockId.Log);

                if (thick)
                {
                    SetLog(context, x + 1, y, z, BlockId.Log);
                    SetLog(context, x, y, z + 1, BlockId.Log);
                    SetLog(context, x + 1, y, z + 1, BlockId.Log);
                }
            }

            // Canopy: one main blob and a few side blobs give a lumpy,
            // natural crown.
            double radius = M(1.9 + random.NextDouble() * 0.7) * size;
            PlaceBlob(context, x, top + (int)(radius * 0.25), z, radius, 0.8, leaves, 0x1EAF, _clearance);

            int sideBlobs = random.Range(3, 7);

            for (int i = 0; i < sideBlobs; i++)
            {
                int direction = random.Range(0, 8);
                double dirX = DirectionX[direction];
                double dirZ = DirectionZ[direction];
                double distance = radius * (0.4 + random.NextDouble() * 0.15);
                int bx = x + (int)Math.Round(dirX * distance);
                int bz = z + (int)Math.Round(dirZ * distance);
                int by = top - (int)(radius * 0.4) + random.Range(0, (int)(radius * 0.6) + 1);
                double sideRadius = radius * (0.55 + random.NextDouble() * 0.15);

                PlaceBlob(context, bx, by, bz, sideRadius, 0.9, leaves, 0x51DE + i, _clearance);

                // A branch from the trunk towards the side blob.
                int steps = (int)distance;
                for (int s = 1; s <= steps; s++)
                {
                    int lx = x + (int)Math.Round(dirX * s * 0.8);
                    int lz = z + (int)Math.Round(dirZ * s * 0.8);
                    int branchY = top - 1 + s / 3;

                    // Branches never hang into the walking space.
                    if (branchY >= context.GetColumn(lx, lz).Height + 1 + _clearance)
                        SetLog(context, lx, branchY, lz, BlockId.Log);
                }
            }
        }

        private void PlacePine(
            ChunkGenerationContext context,
            int x,
            int groundY,
            int z,
            ref WorldRandom random)
        {
            SetGroundUnderTrunk(context, x, groundY, z);

            int height = (int)M(5.5 + random.NextDouble() * 4.5);
            int top = groundY + height;

            // Trunk ends below the tip so it never pokes out of the leaves.
            for (int y = groundY + 1; y < top - 2; y++)
                SetLog(context, x, y, z, BlockId.PineLog);

            // Tiered cone: rings get wider towards the bottom, with a
            // short gap between tiers.
            int leafStart = groundY + (int)M(1.5 + random.NextDouble());
            double maxRadius = M(1.8 + random.NextDouble() * 0.5);
            int tier = Math.Max(2, (int)M(1.2));

            for (int y = top; y >= leafStart; y--)
            {
                double t = (top - y) / (double)Math.Max(1, top - leafStart);
                int inTier = (top - y) % tier;
                double r = maxRadius * t * (1.0 - 0.35 * inTier / tier) + 1.0;

                int ri = (int)Math.Ceiling(r);
                for (int dx = -ri; dx <= ri; dx++)
                for (int dz = -ri; dz <= ri; dz++)
                {
                    double d = Math.Sqrt(dx * dx + dz * dz) + Jitter(context, x + dx, y, z + dz, 0.6);

                    if (d <= r)
                        SetLeaves(context, x + dx, y, z + dz, BlockId.PineLeaves, _clearance);
                }
            }

            SetLeaves(context, x, top + 1, z, BlockId.PineLeaves, _clearance);
        }

        private void PlaceAcacia(
            ChunkGenerationContext context,
            int x,
            int groundY,
            int z,
            ref WorldRandom random)
        {
            SetGroundUnderTrunk(context, x, groundY, z);

            int height = (int)M(3 + random.NextDouble() * 1.5);
            int leanX = random.Range(-1, 2);
            int leanZ = random.Range(-1, 2);

            int cx = x;
            int cz = z;

            for (int i = 1; i <= height; i++)
            {
                if (i == height * 2 / 3)
                {
                    cx += leanX;
                    cz += leanZ;
                }

                SetLog(context, cx, groundY + i, cz, BlockId.Log);
            }

            int top = groundY + height;
            double radius = M(2.2 + random.NextDouble() * 0.6);
            int ri = (int)Math.Ceiling(radius);
            int thickness = Math.Max(1, (int)M(0.6));

            for (int dx = -ri; dx <= ri; dx++)
            for (int dz = -ri; dz <= ri; dz++)
            for (int dy = 0; dy <= thickness; dy++)
            {
                double d = Math.Sqrt(dx * dx + dz * dz) + Jitter(context, cx + dx, top + dy, cz + dz, 0.8);
                double layerRadius = dy == thickness ? radius * 0.6 : radius;

                if (d <= layerRadius)
                    SetLeaves(context, cx + dx, top + 1 + dy, cz + dz, BlockId.Leaves, _clearance);
            }
        }

        private void PlaceCactus(
            ChunkGenerationContext context,
            int x,
            int groundY,
            int z,
            ref WorldRandom random)
        {
            int height = (int)M(1.5 + random.NextDouble() * 2);

            for (int y = groundY + 1; y <= groundY + height; y++)
                SetLog(context, x, y, z, BlockId.Cactus);

            if (height >= M(2) && random.Chance(0.6))
            {
                int side = random.Range(0, 4);
                int ax = side == 0 ? 1 : side == 1 ? -1 : 0;
                int az = side == 2 ? 1 : side == 3 ? -1 : 0;
                int armY = groundY + height / 2;
                int armLength = Math.Max(1, (int)M(0.5));

                for (int i = 1; i <= armLength; i++)
                    SetLog(context, x + ax * i, armY, z + az * i, BlockId.Cactus);

                for (int i = 1; i <= (int)M(0.8); i++)
                    SetLog(context, x + ax * armLength, armY + i, z + az * armLength, BlockId.Cactus);
            }
        }

        private void PlaceBoulder(
            ChunkGenerationContext context,
            int x,
            int groundY,
            int z,
            ref WorldRandom random)
        {
            double radius = M(0.7 + random.NextDouble() * 0.7);
            BlockId block = random.Chance(0.3) ? BlockId.MossyStone : BlockId.Stone;
            int ri = (int)Math.Ceiling(radius);

            for (int dx = -ri; dx <= ri; dx++)
            for (int dy = -ri; dy <= ri; dy++)
            for (int dz = -ri; dz <= ri; dz++)
            {
                double d = Math.Sqrt(dx * dx + dy * dy * 1.8 + dz * dz) + Jitter(context, x + dx, groundY + dy, z + dz, 0.7);

                if (d > radius)
                    continue;

                // Moss on top.
                BlockId b = dy >= ri - 1 && block == BlockId.Stone && (Hash(context, x + dx, groundY + dy, z + dz, 3) & 3) == 0
                    ? BlockId.MossyStone
                    : block;

                PlaceIfReplaceable(context, x + dx, groundY + dy, z + dz, b, replaceGround: dy <= 0);
            }
        }

        /// <summary>A roughly spherical blob of leaves with a noisy edge.</summary>
        private void PlaceBlob(
            ChunkGenerationContext context,
            int cx,
            int cy,
            int cz,
            double radius,
            double verticalSquash,
            BlockId leaves,
            int salt,
            int clearance)
        {
            int ri = (int)Math.Ceiling(radius);

            for (int dy = -ri; dy <= ri; dy++)
            for (int dz = -ri; dz <= ri; dz++)
            for (int dx = -ri; dx <= ri; dx++)
            {
                double ny = dy / verticalSquash;
                double d = Math.Sqrt(dx * dx + ny * ny + dz * dz);
                d += Jitter(context, cx + dx, cy + dy, cz + dz, 0.9, salt);

                if (d <= radius)
                    SetLeaves(context, cx + dx, cy + dy, cz + dz, leaves, clearance);
            }
        }

        private static double Jitter(ChunkGenerationContext context, int x, int y, int z, double amount, int salt = 0x1EAF)
        {
            return (SeedHash.ToUnit(Hash(context, x, y, z, salt)) - 0.5) * amount;
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

        /// <param name="clearance">
        /// Voxels kept free above the ground in this column (0 = none, for
        /// bushes). Checked per column, so canopies over slopes stay clear too.
        /// </param>
        private static void SetLeaves(ChunkGenerationContext context, int x, int y, int z, BlockId block, int clearance)
        {
            if (clearance > 0 && y < context.GetColumn(x, z).Height + 1 + clearance)
                return;

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
                (replaceLeaves && IsLeaves(existing)) ||
                (replaceGround && (existing == BlockId.Grass || existing == BlockId.Dirt ||
                                   existing == BlockId.Snow || existing == BlockId.Stone));

            if (replaceable)
                chunk.SetBlockRaw(lx, y, lz, block);
        }

        private static bool IsLeaves(BlockId block)
        {
            return block == BlockId.Leaves || block == BlockId.PineLeaves ||
                   block == BlockId.AutumnLeaves || block == BlockId.RedLeaves;
        }

        private static bool ToLocal(ChunkGenerationContext context, int x, int z, out int lx, out int lz)
        {
            lx = x - context.MinWorldX;
            lz = z - context.MinWorldZ;

            return lx >= 0 && lz >= 0 && lx < context.Chunk.SizeX && lz < context.Chunk.SizeZ;
        }

        private static ulong Hash(ChunkGenerationContext context, int x, int y, int z, int salt)
        {
            return SeedHash.Hash(context.Config.Seed, x, y, z, salt);
        }
    }
}
