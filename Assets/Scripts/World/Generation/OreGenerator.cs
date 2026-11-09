using World.Blocks;
using World.Generation.Noise;

namespace World.Generation
{
    /// <summary>
    /// Scatters ore veins through stone.
    ///
    /// Each chunk places its own veins with a random walk seeded by the
    /// chunk position. Veins are clipped at the chunk edge, which keeps
    /// generation independent per chunk (no cross-chunk dependencies)
    /// at the cost of the occasional vein being cut flat on one side.
    /// </summary>
    public static class OreGenerator
    {
        private readonly struct OreType
        {
            public readonly BlockId Block;
            public readonly int MinY;
            public readonly int MaxY;
            public readonly int VeinsPerChunk;
            public readonly int VeinSize;

            public OreType(BlockId block, int minY, int maxY, int veinsPerChunk, int veinSize)
            {
                Block = block;
                MinY = minY;
                MaxY = maxY;
                VeinsPerChunk = veinsPerChunk;
                VeinSize = veinSize;
            }
        }

        // Rarer ores lower down. Heights are relative to a 256-high
        // world with sea level 64 and are scaled for other sizes.
        private static readonly OreType[] Ores =
        {
            new OreType(BlockId.CoalOre, 8, 150, 16, 12),
            new OreType(BlockId.CopperOre, 20, 100, 9, 9),
            new OreType(BlockId.IronOre, 6, 72, 10, 8),
            new OreType(BlockId.GoldOre, 5, 34, 3, 7),
            new OreType(BlockId.CrystalOre, 5, 18, 1, 5)
        };

        private static readonly int[] StepX = { 1, -1, 0, 0, 0, 0 };
        private static readonly int[] StepY = { 0, 0, 1, -1, 0, 0 };
        private static readonly int[] StepZ = { 0, 0, 0, 0, 1, -1 };

        public static void Generate(ChunkGenerationContext context)
        {
            var chunk = context.Chunk;
            double scale = context.Config.SeaLevel / 64.0;

            for (int oreIndex = 0; oreIndex < Ores.Length; oreIndex++)
            {
                OreType ore = Ores[oreIndex];
                var random = new WorldRandom(SeedHash.Mix(context.ChunkSeed ^ (ulong)(oreIndex + 1) * 0x51ED27UL));

                int minY = (int)(ore.MinY * scale);
                int maxY = (int)(ore.MaxY * scale);

                if (maxY > chunk.SizeY - 1)
                    maxY = chunk.SizeY - 1;

                for (int vein = 0; vein < ore.VeinsPerChunk; vein++)
                {
                    int x = random.Range(0, chunk.SizeX);
                    int y = random.Range(minY, maxY + 1);
                    int z = random.Range(0, chunk.SizeZ);

                    int size = random.Range(ore.VeinSize / 2, ore.VeinSize + 1);

                    for (int step = 0; step < size; step++)
                    {
                        if (chunk.GetBlock(x, y, z) == BlockId.Stone)
                            chunk.SetBlockRaw(x, y, z, ore.Block);

                        int direction = random.Range(0, 6);
                        x += StepX[direction];
                        y += StepY[direction];
                        z += StepZ[direction];
                    }
                }
            }
        }
    }
}
