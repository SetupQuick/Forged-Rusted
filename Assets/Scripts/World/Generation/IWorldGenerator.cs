using World.Chunks;

namespace World.Generation
{
    public interface IWorldGenerator
    {
        void GenerateChunk(
            ChunkData chunk,
            WorldSettings settings);
    }
}