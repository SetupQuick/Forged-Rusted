using UnityEngine;
using World.Generation;

namespace World
{
    [CreateAssetMenu(
        fileName = "WorldSettings",
        menuName = "World/World Settings")]
    public sealed class WorldSettings
        : ScriptableObject
    {
        [Header("World")]
        [Tooltip("Seed the server uses. Clients always receive the " +
                 "seed from the server; this value is ignored on them.")]
        [SerializeField]
        private int seed = 12345;

        [Tooltip("Pick a new random seed every time a server starts.")]
        [SerializeField]
        private bool useRandomSeed;

        [Header("Size")]
        [SerializeField]
        [Min(1)]
        private int chunkSize = 16;

        [Tooltip("Maximum build height in blocks.")]
        [SerializeField]
        [Min(64)]
        private int worldHeight = 256;

        [Tooltip("Oceans, lakes and rivers fill up to this height.")]
        [SerializeField]
        [Min(8)]
        private int seaLevel = 64;

        [Header("Terrain shape")]
        [Tooltip("Size of continents and oceans in blocks. " +
                 "Larger means bigger landmasses.")]
        [SerializeField]
        [Min(100)]
        private float continentScale = 1400f;

        [Tooltip("Extra height of the tallest mountains above the land.")]
        [SerializeField]
        [Min(0)]
        private float mountainHeight = 120f;

        [Tooltip("Height of rolling hills.")]
        [SerializeField]
        [Min(0)]
        private float hillHeight = 20f;

        [Tooltip("Blocks above sea level where snow caps start.")]
        [SerializeField]
        [Min(10)]
        private int snowLineAboveSea = 95;

        [Header("Biomes")]
        [Tooltip("Size of climate zones in blocks.")]
        [SerializeField]
        [Min(100)]
        private float biomeScale = 1000f;

        [Header("Features")]
        [SerializeField]
        private bool rivers = true;

        [SerializeField]
        private bool caves = true;

        [SerializeField]
        [Range(0f, 2f)]
        private float caveDensity = 1f;

        [SerializeField]
        private bool ores = true;

        [Tooltip("Trees, cacti, boulders and (later) dungeons.")]
        [SerializeField]
        private bool structures = true;

        [SerializeField]
        [Range(0f, 3f)]
        private float treeDensity = 1f;

        public int Seed => seed;

        public bool UseRandomSeed => useRandomSeed;

        public int ChunkSize => chunkSize;

        public int WorldHeight => worldHeight;

        public int SeaLevel => seaLevel;

        /// <summary>
        /// Creates the immutable generator configuration for a seed.
        /// </summary>
        public TerrainConfig CreateConfig(int worldSeed)
        {
            return new TerrainConfig(
                worldSeed,
                chunkSize,
                worldHeight,
                seaLevel,
                continentScale,
                mountainHeight,
                hillHeight,
                biomeScale,
                snowLineAboveSea,
                caves,
                caveDensity,
                ores,
                rivers,
                structures,
                treeDensity);
        }
    }
}
