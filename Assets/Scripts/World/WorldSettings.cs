using UnityEngine;

namespace World
{
    [CreateAssetMenu(
        fileName = "WorldSettings",
        menuName = "World/World Settings")]
    public sealed class WorldSettings
        : ScriptableObject
    {
        [Header("World")]
        [SerializeField]
        private int seed = 12345;

        [Header("Chunks")]
        [SerializeField]
        [Min(1)]
        private int chunkSize = 16;

        [SerializeField]
        [Min(1)]
        private int worldHeight = 128;

        [Header("Terrain")]
        [SerializeField]
        private int baseTerrainHeight = 32;

        [SerializeField]
        private int terrainHeightVariation = 16;

        [SerializeField]
        private float terrainFrequency = 0.01f;

        public int Seed => seed;

        public int ChunkSize => chunkSize;

        public int WorldHeight => worldHeight;

        public int BaseTerrainHeight =>
            baseTerrainHeight;

        public int TerrainHeightVariation =>
            terrainHeightVariation;

        public float TerrainFrequency =>
            terrainFrequency;
    }
}