using World.Blocks;
using World.Generation.Noise;

namespace World.Generation
{
    /// <summary>
    /// Which blocks make up the top layers of a column: grass over
    /// dirt, sand over sandstone, snow on peaks, rock on cliffs, and
    /// so on.
    /// </summary>
    public static class SurfaceRules
    {
        private const int CliffSlope = 3;

        public static BlockId GetSurfaceBlock(
            ChunkGenerationContext context,
            int worldX,
            int worldZ,
            ColumnSample column)
        {
            TerrainConfig config = context.Config;
            BiomeDefinition biome = BiomeRegistry.Get(column.Biome);

            if (column.Height < config.SeaLevel)
                return column.Biome == BiomeId.Ocean && column.Height < config.SeaLevel - 14
                    ? BlockId.Gravel
                    : biome.UnderwaterBlock;

            if (column.Height >= config.SnowLine)
                return BlockId.Snow;

            int slope = context.GetSlope(worldX, worldZ);

            if (slope >= CliffSlope && column.Biome != BiomeId.Beach && column.Biome != BiomeId.Desert)
            {
                // Snow still sticks to cold cliffs now and then.
                if (column.Temperature < -0.5f && slope < 5)
                    return BlockId.Snow;

                return Hash(context, worldX, worldZ, 11) < 0.15 ? BlockId.Gravel : BlockId.Stone;
            }

            if (column.Biome == BiomeId.Mountains)
            {
                // Grassy patches on gentler mountain slopes, below the tree line.
                if (slope <= 1 && column.Height < config.SeaLevel + 75 &&
                    Hash(context, worldX, worldZ, 12) < 0.55)
                {
                    return BlockId.Grass;
                }

                return Hash(context, worldX, worldZ, 13) < 0.1 ? BlockId.Gravel : BlockId.Stone;
            }

            // Sand along the water line.
            if (column.Height <= config.SeaLevel + 1 &&
                column.Biome != BiomeId.Swamp &&
                column.Biome != BiomeId.Tundra &&
                column.Temperature > -0.45f)
            {
                return BlockId.Sand;
            }

            return biome.SurfaceBlock;
        }

        /// <summary>
        /// Fills one column with stone, filler layers, the surface block
        /// and water up to sea level. Caves, ores and structures are
        /// applied afterwards.
        /// </summary>
        public static void FillColumn(
            ChunkGenerationContext context,
            int localX,
            int localZ)
        {
            var chunk = context.Chunk;
            TerrainConfig config = context.Config;

            int worldX = context.MinWorldX + localX;
            int worldZ = context.MinWorldZ + localZ;

            ColumnSample column = context.GetColumn(worldX, worldZ);
            BiomeDefinition biome = BiomeRegistry.Get(column.Biome);

            chunk.SetBiome(localX, localZ, (byte)column.Biome);

            int height = column.Height;
            BlockId surface = GetSurfaceBlock(context, worldX, worldZ, column);

            BlockId filler;
            int fillerDepth;

            switch (surface)
            {
                case BlockId.Stone:
                case BlockId.Gravel when column.Height >= config.SeaLevel:
                    filler = BlockId.Stone;
                    fillerDepth = 0;
                    break;

                case BlockId.Sand:
                    filler = BlockId.Sand;
                    fillerDepth = 3;
                    break;

                case BlockId.Snow when column.Biome == BiomeId.SnowyPeaks:
                    filler = BlockId.Stone;
                    fillerDepth = 0;
                    break;

                case BlockId.Gravel:
                case BlockId.Clay:
                    filler = surface;
                    fillerDepth = 2;
                    break;

                default:
                    filler = biome.FillerBlock;
                    fillerDepth = biome.FillerDepth;
                    break;
            }

            // Slight per-column variation in layer depth.
            int jitter = (int)(Hash(context, worldX, worldZ, 21) * 2);
            fillerDepth += fillerDepth > 0 ? jitter : 0;

            int deepDepth = biome.DeepFillerDepth;
            BlockId deep = biome.DeepFillerBlock;

            for (int y = 0; y <= height; y++)
            {
                BlockId block;

                if (y == 0)
                {
                    block = BlockId.Bedrock;
                }
                else if (y < 4 && Hash3(context, worldX, y, worldZ, 22) < 0.6 - y * 0.15)
                {
                    block = BlockId.Bedrock;
                }
                else if (y == height)
                {
                    block = surface;
                }
                else if (y >= height - fillerDepth)
                {
                    block = filler;
                }
                else if (deepDepth > 0 && y >= height - fillerDepth - deepDepth)
                {
                    block = deep;
                }
                else
                {
                    block = BlockId.Stone;
                }

                chunk.SetBlockRaw(localX, y, localZ, block);
            }

            // Oceans, lakes and rivers.
            int sea = config.SeaLevel;

            for (int y = height + 1; y <= sea; y++)
            {
                bool frozen =
                    y == sea &&
                    (biome.FreezesWater || column.Temperature < -0.55f);

                chunk.SetBlockRaw(localX, y, localZ, frozen ? BlockId.Ice : BlockId.Water);
            }
        }

        private static double Hash(ChunkGenerationContext context, int x, int z, int salt)
        {
            return SeedHash.ToUnit(SeedHash.Hash(context.Config.Seed, x, z, salt));
        }

        private static double Hash3(ChunkGenerationContext context, int x, int y, int z, int salt)
        {
            return SeedHash.ToUnit(SeedHash.Hash(context.Config.Seed, x, y, z, salt));
        }
    }
}
