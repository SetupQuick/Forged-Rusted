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

        [Header("Voxels")]
        [Tooltip("Size of one voxel in metres. Smaller voxels look more " +
                 "detailed but cost more memory and CPU. A 2 m tall " +
                 "player is 4 voxels tall at 0.5.")]
        [SerializeField]
        [Range(0.25f, 1f)]
        private float voxelSize = 0.5f;

        [Tooltip("Chunk width in voxels.")]
        [SerializeField]
        [Min(8)]
        private int chunkSize = 32;

        [Tooltip("Maximum build height in voxels.")]
        [SerializeField]
        [Min(64)]
        private int worldHeight = 320;

        [Tooltip("Oceans, lakes and rivers fill up to this height (voxels).")]
        [SerializeField]
        [Min(8)]
        private int seaLevel = 80;

        [Header("Terrain shape (metres)")]
        [Tooltip("Size of continents and oceans. Larger means bigger landmasses.")]
        [SerializeField]
        [Min(100)]
        private float continentScale = 1400f;

        [Tooltip("Extra height of the tallest mountains above the land.")]
        [SerializeField]
        [Min(0)]
        private float mountainHeight = 80f;

        [Tooltip("Height of rolling hills.")]
        [SerializeField]
        [Min(0)]
        private float hillHeight = 10f;

        [Tooltip("Height above sea level where snow caps start.")]
        [SerializeField]
        [Min(5)]
        private float snowLineAboveSea = 62f;

        [Header("Biomes")]
        [Tooltip("Size of climate zones in metres.")]
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

        [Tooltip("Trees, grass, flowers, boulders and (later) dungeons.")]
        [SerializeField]
        private bool structures = true;

        [SerializeField]
        [Range(0f, 3f)]
        private float treeDensity = 1f;

        [Header("Day and night")]
        [Tooltip("Real minutes for one full day and night.")]
        [SerializeField]
        [Range(1f, 120f)]
        private float dayLengthMinutes = 20f;

        [Tooltip("Time of day when a new world starts (0 midnight, 0.25 sunrise, 0.5 noon).")]
        [SerializeField]
        [Range(0f, 1f)]
        private float startTimeOfDay = 0.3f;

        public float DayLengthSeconds => dayLengthMinutes * 60f;

        public float StartTimeOfDay => startTimeOfDay;

        public int Seed => seed;

        public bool UseRandomSeed => useRandomSeed;

        public float VoxelSize => voxelSize;

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
                voxelSize,
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
