using System;

namespace World.Generation
{
    /// <summary>
    /// Immutable snapshot of everything the generator needs.
    ///
    /// Built once from <see cref="WorldSettings"/> plus the seed the
    /// server chose, then shared read-only by all worker threads.
    /// Has no Unity dependency so the generator can run anywhere
    /// (worker threads, the Linux server, tests).
    /// </summary>
    public sealed class TerrainConfig
    {
        /// <summary>
        /// Bump whenever a change to the generator would produce a
        /// different world from the same seed. The server sends it to
        /// clients so mismatched builds can be detected, and world saves
        /// can use it to tell when base terrain must not be regenerated.
        /// </summary>
        public const ushort GeneratorVersion = 3;

        public int Seed { get; }

        /// <summary>Size of one voxel in metres (Unity units).</summary>
        public float VoxelSize { get; }

        /// <summary>
        /// Voxels per metre. Generator constants are tuned in metres and
        /// multiplied by this, so the world keeps its proportions at any
        /// voxel size.
        /// </summary>
        public double FeatureScale => 1.0 / VoxelSize;

        public int ChunkSize { get; }
        public int WorldHeight { get; }
        public int SeaLevel { get; }

        // All in voxels.
        public double ContinentScale { get; }
        public double MountainHeight { get; }
        public double HillHeight { get; }
        public double BiomeScale { get; }
        public int SnowLine { get; }

        public bool CavesEnabled { get; }
        public double CaveDensity { get; }
        public bool OresEnabled { get; }
        public bool RiversEnabled { get; }
        public bool StructuresEnabled { get; }
        public double TreeDensity { get; }

        /// <param name="seed">World seed.</param>
        /// <param name="voxelSize">Metres per voxel.</param>
        /// <param name="chunkSize">Chunk width in voxels.</param>
        /// <param name="worldHeight">World height in voxels.</param>
        /// <param name="seaLevel">Sea level in voxels.</param>
        /// <param name="continentScale">Size of continents in metres.</param>
        /// <param name="mountainHeight">Mountain height in metres.</param>
        /// <param name="hillHeight">Hill height in metres.</param>
        /// <param name="biomeScale">Size of climate zones in metres.</param>
        /// <param name="snowLineAboveSea">Snow line in metres above sea level.</param>
        public TerrainConfig(
            int seed,
            float voxelSize = 0.5f,
            int chunkSize = 32,
            int worldHeight = 320,
            int seaLevel = 80,
            double continentScale = 1400,
            double mountainHeight = 80,
            double hillHeight = 10,
            double biomeScale = 1000,
            double snowLineAboveSea = 62,
            bool cavesEnabled = true,
            double caveDensity = 1.0,
            bool oresEnabled = true,
            bool riversEnabled = true,
            bool structuresEnabled = true,
            double treeDensity = 1.0)
        {
            if (chunkSize < 1)
                throw new ArgumentOutOfRangeException(nameof(chunkSize));

            if (worldHeight < 32)
                throw new ArgumentOutOfRangeException(nameof(worldHeight));

            if (voxelSize < 0.05f || voxelSize > 4f)
                throw new ArgumentOutOfRangeException(nameof(voxelSize));

            Seed = seed;
            VoxelSize = voxelSize;
            ChunkSize = chunkSize;
            WorldHeight = worldHeight;
            SeaLevel = Clamp(seaLevel, 8, worldHeight - 16);

            double perMetre = 1.0 / voxelSize;

            // Everything below is stored in voxels.
            ContinentScale = Math.Max(100, continentScale) * perMetre;
            HillHeight = Math.Max(0, hillHeight) * perMetre;

            // Leave headroom above the highest peaks for trees.
            double maxMountain = worldHeight - SeaLevel - 26 * perMetre - HillHeight - 20 * perMetre;
            MountainHeight = Math.Max(0, Math.Min(mountainHeight * perMetre, maxMountain));

            BiomeScale = Math.Max(100, biomeScale) * perMetre;
            SnowLine = (int)Math.Min(SeaLevel + Math.Max(5, snowLineAboveSea) * perMetre, worldHeight - 2);

            CavesEnabled = cavesEnabled;
            CaveDensity = Math.Max(0, caveDensity);
            OresEnabled = oresEnabled;
            RiversEnabled = riversEnabled;
            StructuresEnabled = structuresEnabled;
            TreeDensity = Math.Max(0, treeDensity);
        }

        /// <summary>
        /// Hash of every setting except the seed. Server and clients
        /// compare it to detect builds with different world settings,
        /// which would otherwise silently generate different terrain.
        /// </summary>
        public uint SettingsFingerprint
        {
            get
            {
                unchecked
                {
                    uint hash = 2166136261;

                    void Add(long value)
                    {
                        for (int i = 0; i < 8; i++)
                        {
                            hash ^= (byte)(value >> (i * 8));
                            hash *= 16777619;
                        }
                    }

                    Add(GeneratorVersion);
                    Add(BitConverter.DoubleToInt64Bits(VoxelSize));
                    Add(ChunkSize);
                    Add(WorldHeight);
                    Add(SeaLevel);
                    Add(BitConverter.DoubleToInt64Bits(ContinentScale));
                    Add(BitConverter.DoubleToInt64Bits(MountainHeight));
                    Add(BitConverter.DoubleToInt64Bits(HillHeight));
                    Add(BitConverter.DoubleToInt64Bits(BiomeScale));
                    Add(SnowLine);
                    Add(CavesEnabled ? 1 : 0);
                    Add(BitConverter.DoubleToInt64Bits(CaveDensity));
                    Add(OresEnabled ? 1 : 0);
                    Add(RiversEnabled ? 1 : 0);
                    Add(StructuresEnabled ? 1 : 0);
                    Add(BitConverter.DoubleToInt64Bits(TreeDensity));

                    return hash;
                }
            }
        }

        private static int Clamp(int value, int min, int max)
        {
            return value < min ? min : value > max ? max : value;
        }
    }
}
