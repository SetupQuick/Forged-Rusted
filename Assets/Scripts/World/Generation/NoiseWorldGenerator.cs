using System.Collections.Generic;
using World.Blocks;
using World.Chunks;
using World.Generation.Structures;

namespace World.Generation
{
    /// <summary>
    /// The world generator. Runs these passes for each chunk:
    ///   1. Terrain   - heights from layered noise, biome surface layers,
    ///                  water up to sea level (<see cref="TerrainSampler"/>,
    ///                  <see cref="SurfaceRules"/>).
    ///   2. Caves     - caverns and tunnels (<see cref="CaveCarver"/>).
    ///   3. Ores      - veins in stone (<see cref="OreGenerator"/>).
    ///   4. Structures- trees, boulders, ... (<see cref="IStructureGenerator"/>).
    ///                  Dungeons and other structures plug in here.
    ///
    /// Immutable after construction and safe to call from many threads.
    /// </summary>
    public sealed class NoiseWorldGenerator
        : IWorldGenerator
    {
        private readonly TerrainSampler _sampler;
        private readonly CaveCarver _carver;
        private readonly IStructureGenerator[] _structures;
        private readonly int _border;

        public TerrainConfig Config { get; }

        public NoiseWorldGenerator(TerrainConfig config)
            : this(config, null)
        {
        }

        /// <param name="config">Generator settings and seed.</param>
        /// <param name="extraStructures">
        /// Additional structure passes (e.g. dungeons). They run after
        /// the built-in surface features.
        /// </param>
        public NoiseWorldGenerator(
            TerrainConfig config,
            IEnumerable<IStructureGenerator> extraStructures)
        {
            Config = config;
            _sampler = new TerrainSampler(config);
            _carver = new CaveCarver(config);

            var structures = new List<IStructureGenerator>();

            if (config.StructuresEnabled)
            {
                structures.Add(new SurfaceFeatureGenerator(config));

                if (extraStructures != null)
                    structures.AddRange(extraStructures);
            }

            _structures = structures.ToArray();

            int reach = 0;
            foreach (IStructureGenerator structure in _structures)
            {
                if (structure.MaxHorizontalReach > reach)
                    reach = structure.MaxHorizontalReach;
            }

            // +1 so slope checks at the edge of the reach have neighbours.
            _border = reach + 1;
        }

        public ColumnSample SampleColumn(int worldX, int worldZ)
        {
            return _sampler.Sample(worldX, worldZ);
        }

        public void GenerateChunk(ChunkData chunk)
        {
            var context = new ChunkGenerationContext(
                chunk,
                Config,
                _sampler,
                _carver,
                _border);

            int maxHeight = GenerateTerrain(context);

            if (_carver.Enabled)
            {
                context.PrepareCaves(maxHeight);
                CarveCaves(context);
            }

            if (Config.OresEnabled)
                OreGenerator.Generate(context);

            for (int i = 0; i < _structures.Length; i++)
                _structures[i].Generate(context);
        }

        private static int GenerateTerrain(ChunkGenerationContext context)
        {
            ChunkData chunk = context.Chunk;
            int maxHeight = 0;

            for (int z = 0; z < chunk.SizeZ; z++)
            for (int x = 0; x < chunk.SizeX; x++)
            {
                SurfaceRules.FillColumn(context, x, z);

                int height = context.GetColumn(context.MinWorldX + x, context.MinWorldZ + z).Height;
                if (height > maxHeight)
                    maxHeight = height;
            }

            return maxHeight;
        }

        private static void CarveCaves(ChunkGenerationContext context)
        {
            ChunkData chunk = context.Chunk;
            ChunkCaveField caves = context.Caves;

            for (int z = 0; z < chunk.SizeZ; z++)
            for (int x = 0; x < chunk.SizeX; x++)
            {
                int worldX = context.MinWorldX + x;
                int worldZ = context.MinWorldZ + z;
                int height = context.GetColumn(worldX, worldZ).Height;

                for (int y = 1; y <= height; y++)
                {
                    if (!caves.IsCarved(worldX, y, worldZ, height))
                        continue;

                    BlockId existing = chunk.GetBlock(x, y, z);

                    if (existing == BlockId.Bedrock || existing == BlockId.Water || existing == BlockId.Ice)
                        continue;

                    chunk.SetBlockRaw(x, y, z, BlockId.Air);
                }

                // Grass that ends up directly above a cave ceiling opening
                // stays; dirt exposed at the top of a tunnel entrance becomes
                // grass again so entrances look natural.
                int top = height;
                while (top > 0 && chunk.GetBlock(x, top, z) == BlockId.Air)
                    top--;

                if (top < height && top > context.Config.SeaLevel &&
                    chunk.GetBlock(x, top, z) == BlockId.Dirt &&
                    BiomeRegistry.Get((BiomeId)chunk.GetBiome(x, z)).SurfaceBlock == BlockId.Grass)
                {
                    chunk.SetBlockRaw(x, top, z, BlockId.Grass);
                }
            }
        }
    }
}
