using World.Blocks;
using World.Chunks;

namespace World.Generation
{
    /// <summary>
    /// Per-chunk scratch data shared by the generation passes.
    ///
    /// Caches column samples for the chunk plus a border around it, so
    /// passes that look at neighbouring columns (slope checks, trees
    /// that hang over the chunk edge) do not resample noise.
    /// Not thread-safe; one instance per chunk being generated.
    /// </summary>
    public sealed class ChunkGenerationContext
    {
        private readonly ColumnSample[] _columns;
        private readonly bool[] _sampled;
        private readonly int _border;
        private readonly int _width;
        private readonly int _depth;

        public TerrainConfig Config { get; }
        public TerrainSampler Sampler { get; }
        public CaveCarver Carver { get; }
        public ChunkCaveField Caves { get; private set; }
        public ChunkData Chunk { get; }

        public int MinWorldX { get; }
        public int MinWorldZ { get; }

        /// <summary>Seed mixed with the chunk position, for per-chunk randomness.</summary>
        public ulong ChunkSeed { get; }

        public ChunkGenerationContext(
            ChunkData chunk,
            TerrainConfig config,
            TerrainSampler sampler,
            CaveCarver carver,
            int border)
        {
            Chunk = chunk;
            Config = config;
            Sampler = sampler;
            Carver = carver;

            _border = border;
            _width = chunk.SizeX + border * 2;
            _depth = chunk.SizeZ + border * 2;

            _columns = new ColumnSample[_width * _depth];
            _sampled = new bool[_width * _depth];

            MinWorldX = chunk.Coord.X * chunk.SizeX;
            MinWorldZ = chunk.Coord.Z * chunk.SizeZ;

            ChunkSeed = Noise.SeedHash.Hash(config.Seed, chunk.Coord.X, chunk.Coord.Z, 0x0C4A);
        }

        /// <summary>Builds the cave grid once the column heights are known.</summary>
        public void PrepareCaves(int maxHeight)
        {
            if (Carver == null || !Carver.Enabled)
                return;

            Caves = new ChunkCaveField(
                Carver,
                MinWorldX,
                MinWorldZ,
                Chunk.SizeX,
                Chunk.SizeZ,
                maxHeight);
        }

        /// <summary>Column sample at any world position (cached near the chunk).</summary>
        public ColumnSample GetColumn(int worldX, int worldZ)
        {
            int lx = worldX - MinWorldX + _border;
            int lz = worldZ - MinWorldZ + _border;

            if (lx < 0 || lz < 0 || lx >= _width || lz >= _depth)
                return Sampler.Sample(worldX, worldZ);

            int index = lx + lz * _width;

            if (!_sampled[index])
            {
                _columns[index] = Sampler.Sample(worldX, worldZ);
                _sampled[index] = true;
            }

            return _columns[index];
        }

        /// <summary>
        /// Largest height difference to the four direct neighbours.
        /// Steep columns get rocky surfaces instead of grass.
        /// </summary>
        public int GetSlope(int worldX, int worldZ)
        {
            int h = GetColumn(worldX, worldZ).Height;
            int slope = 0;

            slope = Max(slope, Abs(GetColumn(worldX + 1, worldZ).Height - h));
            slope = Max(slope, Abs(GetColumn(worldX - 1, worldZ).Height - h));
            slope = Max(slope, Abs(GetColumn(worldX, worldZ + 1).Height - h));
            slope = Max(slope, Abs(GetColumn(worldX, worldZ - 1).Height - h));

            return slope;
        }

        /// <summary>
        /// The block placed on top of a column, before caves and
        /// structures. A pure function of position, so neighbouring
        /// chunks agree on it.
        /// </summary>
        public BlockId GetSurfaceBlock(int worldX, int worldZ)
        {
            ColumnSample column = GetColumn(worldX, worldZ);
            return SurfaceRules.GetSurfaceBlock(this, worldX, worldZ, column);
        }

        /// <summary>
        /// True when the topmost block of the column was removed by a
        /// cave entrance (nothing should be placed on it).
        /// </summary>
        public bool IsSurfaceCarved(int worldX, int worldZ)
        {
            if (Carver == null || !Carver.Enabled)
                return false;

            int height = GetColumn(worldX, worldZ).Height;

            if (Caves != null &&
                worldX >= MinWorldX && worldX < MinWorldX + Chunk.SizeX &&
                worldZ >= MinWorldZ && worldZ < MinWorldZ + Chunk.SizeZ)
            {
                return Caves.IsCarved(worldX, height, worldZ, height);
            }

            return Carver.IsCarved(worldX, height, worldZ, height);
        }

        private static int Abs(int value) => value < 0 ? -value : value;

        private static int Max(int a, int b) => a > b ? a : b;
    }
}
