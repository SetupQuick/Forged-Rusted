using World.Blocks;

namespace World.Generation
{
    /// <summary>
    /// Biome identifiers. Stored per column in chunk data, so only
    /// append new values.
    /// </summary>
    public enum BiomeId : byte
    {
        Ocean = 0,
        FrozenOcean = 1,
        Beach = 2,
        Plains = 3,
        Forest = 4,
        Desert = 5,
        Savanna = 6,
        Taiga = 7,
        Tundra = 8,
        Mountains = 9,
        SnowyPeaks = 10,
        Swamp = 11
    }

    public sealed class BiomeDefinition
    {
        public BiomeId Id { get; }
        public string Name { get; }

        public BlockId SurfaceBlock { get; }
        public BlockId FillerBlock { get; }
        public int FillerDepth { get; }

        /// <summary>Layer under the filler before stone (e.g. sandstone under desert sand).</summary>
        public BlockId DeepFillerBlock { get; }
        public int DeepFillerDepth { get; }

        public BlockId UnderwaterBlock { get; }

        /// <summary>Colour multiplied onto grass.</summary>
        public BlockColor GrassTint { get; }

        /// <summary>Colour multiplied onto leaves.</summary>
        public BlockColor FoliageTint { get; }

        /// <summary>Water surface freezes to ice.</summary>
        public bool FreezesWater { get; }

        public BiomeDefinition(
            BiomeId id,
            string name,
            BlockId surface,
            BlockId filler,
            int fillerDepth,
            BlockId underwater,
            uint grassTint,
            uint foliageTint,
            BlockId deepFiller = BlockId.Stone,
            int deepFillerDepth = 0,
            bool freezesWater = false)
        {
            Id = id;
            Name = name;
            SurfaceBlock = surface;
            FillerBlock = filler;
            FillerDepth = fillerDepth;
            DeepFillerBlock = deepFiller;
            DeepFillerDepth = deepFillerDepth;
            UnderwaterBlock = underwater;
            GrassTint = BlockColor.FromHex(grassTint);
            FoliageTint = BlockColor.FromHex(foliageTint);
            FreezesWater = freezesWater;
        }
    }

    /// <summary>
    /// Read-only table of biome definitions; safe on worker threads.
    /// Tints are placeholder colours for the voxel look.
    /// </summary>
    public static class BiomeRegistry
    {
        private static readonly BiomeDefinition[] Definitions =
            new BiomeDefinition[256];

        static BiomeRegistry()
        {
            Add(new BiomeDefinition(
                BiomeId.Ocean, "Ocean",
                BlockId.Sand, BlockId.Sand, 3, BlockId.Sand,
                0x6DAA45, 0x4F8A2E));

            Add(new BiomeDefinition(
                BiomeId.FrozenOcean, "Frozen Ocean",
                BlockId.Gravel, BlockId.Gravel, 2, BlockId.Gravel,
                0x80A890, 0x5E8A6E, freezesWater: true));

            Add(new BiomeDefinition(
                BiomeId.Beach, "Beach",
                BlockId.Sand, BlockId.Sand, 3, BlockId.Sand,
                0x7DB04A, 0x5E9636,
                BlockId.Sandstone, 2));

            Add(new BiomeDefinition(
                BiomeId.Plains, "Plains",
                BlockId.Grass, BlockId.Dirt, 3, BlockId.Dirt,
                0x79C05A, 0x5BA33B));

            Add(new BiomeDefinition(
                BiomeId.Forest, "Forest",
                BlockId.Grass, BlockId.Dirt, 4, BlockId.Dirt,
                0x5DA244, 0x3F8A2C));

            Add(new BiomeDefinition(
                BiomeId.Desert, "Desert",
                BlockId.Sand, BlockId.Sand, 4, BlockId.Sand,
                0xBFB755, 0xAEA74A,
                BlockId.Sandstone, 5));

            Add(new BiomeDefinition(
                BiomeId.Savanna, "Savanna",
                BlockId.Grass, BlockId.Dirt, 3, BlockId.Dirt,
                0xB5B35A, 0x9AA03E));

            Add(new BiomeDefinition(
                BiomeId.Taiga, "Taiga",
                BlockId.Grass, BlockId.Dirt, 3, BlockId.Gravel,
                0x6A9A64, 0x4C7A55));

            Add(new BiomeDefinition(
                BiomeId.Tundra, "Tundra",
                BlockId.Snow, BlockId.Dirt, 3, BlockId.Gravel,
                0x8EAE8E, 0x6A8F72, freezesWater: true));

            Add(new BiomeDefinition(
                BiomeId.Mountains, "Mountains",
                BlockId.Stone, BlockId.Stone, 1, BlockId.Gravel,
                0x7FA36A, 0x5C8452));

            Add(new BiomeDefinition(
                BiomeId.SnowyPeaks, "Snowy Peaks",
                BlockId.Snow, BlockId.Stone, 1, BlockId.Gravel,
                0x8EAE8E, 0x6A8F72, freezesWater: true));

            Add(new BiomeDefinition(
                BiomeId.Swamp, "Swamp",
                BlockId.Grass, BlockId.Dirt, 3, BlockId.Clay,
                0x5E7D3A, 0x4A6B2A));
        }

        public static BiomeDefinition Get(BiomeId id)
        {
            return Definitions[(byte)id] ?? Definitions[(byte)BiomeId.Plains];
        }

        private static void Add(BiomeDefinition definition)
        {
            Definitions[(byte)definition.Id] = definition;
        }
    }
}
