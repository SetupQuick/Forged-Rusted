using World.Chunks;

namespace World.Generation
{
    /// <summary>
    /// Fills a freshly created chunk with terrain.
    ///
    /// Implementations must be deterministic (same config + chunk
    /// coordinate = same blocks, on every machine) and thread-safe,
    /// because chunks are generated in parallel on worker threads.
    /// </summary>
    public interface IWorldGenerator
    {
        TerrainConfig Config { get; }

        void GenerateChunk(ChunkData chunk);

        /// <summary>
        /// Terrain height and biome at a column without generating a
        /// chunk. Cheap; used for spawn searches.
        /// </summary>
        ColumnSample SampleColumn(int worldX, int worldZ);
    }
}
