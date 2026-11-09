using World.Blocks;
using World.Chunks;
using World.Generation.Noise;

namespace World.Generation.Structures
{
    /// <summary>
    /// Grass tufts, flowers and dry grass on top of the ground. Single
    /// voxels in this chunk only, so no cross-chunk work is needed.
    /// Runs after trees so it only fills open ground.
    /// </summary>
    public sealed class GroundCoverGenerator : IStructureGenerator
    {
        private static readonly BlockId[] Flowers =
        {
            BlockId.FlowerRed, BlockId.FlowerYellow, BlockId.FlowerBlue, BlockId.FlowerWhite
        };

        private readonly FractalNoise _meadows;
        private readonly FractalNoise _flowerColour;

        public int MaxHorizontalReach => 0;

        public GroundCoverGenerator(TerrainConfig config)
        {
            double metres = config.FeatureScale;
            _meadows = new FractalNoise(SeedHash.Derive(config.Seed, 401), 40 * metres, 2);
            _flowerColour = new FractalNoise(SeedHash.Derive(config.Seed, 402), 25 * metres, 1);
        }

        public void Generate(ChunkGenerationContext context)
        {
            ChunkData chunk = context.Chunk;
            TerrainConfig config = context.Config;
            double density = config.TreeDensity;

            for (int lz = 0; lz < chunk.SizeZ; lz++)
            for (int lx = 0; lx < chunk.SizeX; lx++)
            {
                int x = context.MinWorldX + lx;
                int z = context.MinWorldZ + lz;

                ColumnSample column = context.GetColumn(x, z);
                int y = column.Height;

                if (y < config.SeaLevel || y + 1 >= chunk.SizeY)
                    continue;

                BlockId ground = chunk.GetBlock(lx, y, lz);

                if (chunk.GetBlock(lx, y + 1, lz) != BlockId.Air)
                    continue;

                double roll = SeedHash.ToUnit(SeedHash.Hash(config.Seed, x, z, 0x6C0F));

                // Patches: dense meadows with flowers, barer ground between.
                double meadow = TerrainSampler.SmoothStep(-0.3, 0.4, _meadows.Sample(x, z));

                BlockId cover = Choose(column.Biome, ground, roll, meadow, density, x, z);

                if (cover != BlockId.Air)
                    chunk.SetBlockRaw(lx, y + 1, lz, cover);
            }
        }

        private BlockId Choose(BiomeId biome, BlockId ground, double roll, double meadow, double density, int x, int z)
        {
            if (ground == BlockId.Sand)
            {
                return biome == BiomeId.Desert && roll < 0.015 * density ? BlockId.DryGrass : BlockId.Air;
            }

            if (ground != BlockId.Grass)
                return BlockId.Air;

            double grass;
            double flowers;

            switch (biome)
            {
                case BiomeId.Plains: grass = 0.08 + 0.3 * meadow; flowers = 0.05 * meadow; break;
                case BiomeId.Forest: grass = 0.06 + 0.18 * meadow; flowers = 0.025 * meadow; break;
                case BiomeId.Swamp: grass = 0.3; flowers = 0.01; break;
                case BiomeId.Taiga: grass = 0.12; flowers = 0.005; break;
                case BiomeId.Mountains: grass = 0.15; flowers = 0.02; break;
                case BiomeId.Savanna:
                    return roll < (0.2 + 0.2 * meadow) * density ? BlockId.DryGrass : BlockId.Air;
                default: grass = 0.08; flowers = 0.0; break;
            }

            grass *= density;
            flowers *= density;

            if (roll < flowers)
            {
                // Flower colour changes gradually over the land.
                double c = (_flowerColour.Sample(x, z) + 0.6) / 1.2 * Flowers.Length;
                int index = (int)c;
                if (index < 0) index = 0;
                if (index >= Flowers.Length) index = Flowers.Length - 1;
                return Flowers[index];
            }

            return roll < flowers + grass ? BlockId.TallGrass : BlockId.Air;
        }
    }
}
