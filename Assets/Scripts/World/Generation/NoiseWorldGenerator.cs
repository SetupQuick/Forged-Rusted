using UnityEngine;
using World.Blocks;
using World.Chunks;

namespace World.Generation
{
    public sealed class NoiseWorldGenerator
        : IWorldGenerator
    {
        public void GenerateChunk(
            ChunkData chunk,
            WorldSettings settings)
        {
            int chunkSize =
                settings.ChunkSize;

            for (int localX = 0;
                 localX < chunk.SizeX;
                 localX++)
            {
                for (int localZ = 0;
                     localZ < chunk.SizeZ;
                     localZ++)
                {
                    int worldX =
                        chunk.Coord.X *
                        chunkSize +
                        localX;

                    int worldZ =
                        chunk.Coord.Z *
                        chunkSize +
                        localZ;

                    int terrainHeight =
                        GetTerrainHeight(
                            worldX,
                            worldZ,
                            settings);

                    for (int y = 0;
                         y < chunk.SizeY;
                         y++)
                    {
                        BlockId block =
                            GetBlockForHeight(
                                y,
                                terrainHeight);

                        chunk.SetBlock(
                            localX,
                            y,
                            localZ,
                            block);
                    }
                }
            }
        }

        private static int GetTerrainHeight(
            int worldX,
            int worldZ,
            WorldSettings settings)
        {
            float seedOffset =
                settings.Seed * 0.001f;

            float sampleX =
                worldX *
                settings.TerrainFrequency +
                seedOffset;

            float sampleZ =
                worldZ *
                settings.TerrainFrequency +
                seedOffset;

            float noise =
                Mathf.PerlinNoise(
                    sampleX,
                    sampleZ);

            int variation =
                Mathf.RoundToInt(
                    noise *
                    settings
                        .TerrainHeightVariation);

            return
                settings.BaseTerrainHeight +
                variation;
        }

        private static BlockId GetBlockForHeight(
            int y,
            int terrainHeight)
        {
            if (y > terrainHeight)
                return BlockId.Air;

            if (y == terrainHeight)
                return BlockId.Grass;

            if (y >= terrainHeight - 3)
                return BlockId.Dirt;

            return BlockId.Stone;
        }
    }
}