using System;

namespace World.Blocks
{
    /// <summary>
    /// A colour stored as plain bytes so block data stays usable
    /// outside Unity's main thread (meshing runs on worker threads).
    /// </summary>
    public readonly struct BlockColor
    {
        public readonly byte R;
        public readonly byte G;
        public readonly byte B;

        public BlockColor(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
        }

        public static BlockColor FromHex(uint rgb)
        {
            return new BlockColor(
                (byte)((rgb >> 16) & 0xFF),
                (byte)((rgb >> 8) & 0xFF),
                (byte)(rgb & 0xFF));
        }
    }

    /// <summary>
    /// How a block's colour reacts to the biome it is in.
    /// </summary>
    public enum BiomeTint : byte
    {
        None = 0,
        Grass = 1,
        Foliage = 2
    }

    /// <summary>How the mesher draws a block.</summary>
    public enum BlockShape : byte
    {
        /// <summary>Full voxel cube.</summary>
        Cube = 0,

        /// <summary>Transparent liquid surface.</summary>
        Liquid = 1,

        /// <summary>A small grass tuft (a few thin sub-voxel columns).</summary>
        Tuft = 2,

        /// <summary>A stem with a coloured head.</summary>
        Flower = 3
    }

    public sealed class BlockDefinition
    {
        public BlockId Id { get; }
        public string Name { get; }

        /// <summary>Players collide with it.</summary>
        public bool IsSolid { get; }

        /// <summary>Hides the faces of neighbouring blocks.</summary>
        public bool IsOpaque { get; }

        /// <summary>Rendered in the transparent liquid mesh.</summary>
        public bool IsLiquid { get; }

        public BlockColor TopColor { get; }
        public BlockColor SideColor { get; }
        public BlockColor BottomColor { get; }

        public BiomeTint Tint { get; }

        public BlockShape Shape { get; }

        /// <summary>Can be replaced by placing a block into it (air, water, grass tufts).</summary>
        public bool IsReplaceable { get; }

        /// <summary>Players can break it.</summary>
        public bool IsBreakable { get; }

        /// <summary>
        /// 0..1 strength of the per-block brightness variation
        /// that gives the low-poly look some texture.
        /// </summary>
        public float ColorNoise { get; }

        public BlockDefinition(
            BlockId id,
            string name,
            bool isSolid,
            bool isOpaque,
            bool isLiquid,
            BlockColor top,
            BlockColor side,
            BlockColor bottom,
            BiomeTint tint = BiomeTint.None,
            float colorNoise = 0.06f,
            BlockShape shape = BlockShape.Cube,
            bool replaceable = false,
            bool breakable = true)
        {
            Id = id;
            Name = name;
            IsSolid = isSolid;
            IsOpaque = isOpaque;
            IsLiquid = isLiquid;
            TopColor = top;
            SideColor = side;
            BottomColor = bottom;
            Tint = tint;
            ColorNoise = colorNoise;
            Shape = isLiquid ? BlockShape.Liquid : shape;
            IsReplaceable = replaceable || id == BlockId.Air || isLiquid;
            IsBreakable = breakable && id != BlockId.Air && !isLiquid;
        }
    }

    /// <summary>
    /// Static lookup table describing every <see cref="BlockId"/>.
    /// Read-only after the type initialiser runs, so it is safe to
    /// use from generation and meshing worker threads.
    ///
    /// Colours are placeholders for the voxel look. When the team
    /// settles on textures or a final palette, this is the one
    /// place to change.
    /// </summary>
    public static class BlockRegistry
    {
        private static readonly BlockDefinition[] Definitions =
            new BlockDefinition[256];

        private static readonly bool[] Opaque = new bool[256];
        private static readonly bool[] Solid = new bool[256];
        private static readonly bool[] Liquid = new bool[256];

        static BlockRegistry()
        {
            BlockColor C(uint hex) => BlockColor.FromHex(hex);

            // Placeholder palette for the small-voxel look: warm,
            // saturated colours with strong per-voxel variation.

            Register(new BlockDefinition(
                BlockId.Air, "Air", false, false, false,
                C(0), C(0), C(0), breakable: false));

            // Grass colours are multiplied by the biome tint.
            Register(new BlockDefinition(
                BlockId.Grass, "Grass", true, true, false,
                C(0xFFFFFF), C(0xD8D2B8), C(0x8B5A3C),
                BiomeTint.Grass, 0.12f));

            Register(Simple(BlockId.Dirt, "Dirt", C(0x8B5A3C), 0.12f));
            Register(Simple(BlockId.Stone, "Stone", C(0x9C9AA0), 0.1f));

            Register(new BlockDefinition(
                BlockId.Bedrock, "Bedrock", true, true, false,
                C(0x2E2E33), C(0x2E2E33), C(0x2E2E33),
                BiomeTint.None, 0.15f, breakable: false));

            Register(new BlockDefinition(
                BlockId.Water, "Water", false, false, true,
                C(0x3D86D8), C(0x2F72C2), C(0x2A66B0),
                BiomeTint.None, 0.03f));

            Register(Simple(BlockId.Sand, "Sand", C(0xE6D29A), 0.07f));
            Register(new BlockDefinition(
                BlockId.Sandstone, "Sandstone", true, true, false,
                C(0xDCC186), C(0xCBAE72), C(0xDCC186), BiomeTint.None, 0.08f));
            Register(Simple(BlockId.Gravel, "Gravel", C(0x958C83), 0.16f));
            Register(Simple(BlockId.Clay, "Clay", C(0xA4A9B6), 0.06f));
            Register(Simple(BlockId.Snow, "Snow", C(0xF2F6FC), 0.035f));
            Register(Simple(BlockId.Chest, "Chest", C(0xA06A35), 0.05f));

            Register(new BlockDefinition(
                BlockId.Ice, "Ice", true, true, false,
                C(0xAEDBFA), C(0x9CCBEF), C(0xAEDBFA),
                BiomeTint.None, 0.04f));

            Register(new BlockDefinition(
                BlockId.Log, "Log", true, true, false,
                C(0xB08A58), C(0x6E4A2C), C(0xB08A58), BiomeTint.None, 0.1f));

            Register(new BlockDefinition(
                BlockId.Leaves, "Leaves", true, true, false,
                C(0xFFFFFF), C(0xF2F2F2), C(0xD8D8D8),
                BiomeTint.Foliage, 0.16f));

            Register(new BlockDefinition(
                BlockId.PineLog, "Pine Log", true, true, false,
                C(0x8C6A45), C(0x523B27), C(0x8C6A45), BiomeTint.None, 0.1f));

            Register(Simple(BlockId.PineLeaves, "Pine Leaves", C(0x2F6046), 0.14f));

            Register(new BlockDefinition(
                BlockId.Cactus, "Cactus", true, true, false,
                C(0x6BAA45), C(0x55933A), C(0x6BAA45), BiomeTint.None, 0.08f));

            Register(Simple(BlockId.CoalOre, "Coal Ore", C(0x46464B), 0.2f));
            Register(Simple(BlockId.CopperOre, "Copper Ore", C(0xC0703F), 0.16f));
            Register(Simple(BlockId.IronOre, "Iron Ore", C(0xC4A48E), 0.16f));
            Register(Simple(BlockId.GoldOre, "Gold Ore", C(0xF0C23C), 0.14f));
            Register(Simple(BlockId.CrystalOre, "Crystal Ore", C(0x5FE0E6), 0.14f));
            Register(Simple(BlockId.MossyStone, "Mossy Stone", C(0x76905F), 0.16f));

            Register(Simple(BlockId.AutumnLeaves, "Autumn Leaves", C(0xE8892E), 0.2f));
            Register(Simple(BlockId.RedLeaves, "Red Leaves", C(0xC8432C), 0.2f));

            Register(Decoration(BlockId.TallGrass, "Tall Grass", C(0xFFFFFF), BlockShape.Tuft, BiomeTint.Grass));
            Register(Decoration(BlockId.DryGrass, "Dry Grass", C(0xD9C27A), BlockShape.Tuft, BiomeTint.None));
            Register(Decoration(BlockId.FlowerRed, "Red Flower", C(0xE0393A), BlockShape.Flower, BiomeTint.None));
            Register(Decoration(BlockId.FlowerYellow, "Yellow Flower", C(0xF4CD34), BlockShape.Flower, BiomeTint.None));
            Register(Decoration(BlockId.FlowerBlue, "Blue Flower", C(0x4F7CE8), BlockShape.Flower, BiomeTint.None));
            Register(Decoration(BlockId.FlowerWhite, "White Flower", C(0xF4F1EA), BlockShape.Flower, BiomeTint.None));
        }

        private static BlockDefinition Decoration(
            BlockId id,
            string name,
            BlockColor color,
            BlockShape shape,
            BiomeTint tint)
        {
            return new BlockDefinition(
                id, name, false, false, false,
                color, color, color,
                tint, 0.15f, shape, replaceable: true);
        }

        public static BlockDefinition Get(BlockId id)
        {
            return Definitions[(byte)id] ??
                   Definitions[(byte)BlockId.Stone];
        }

        public static bool IsOpaque(BlockId id) => Opaque[(byte)id];

        public static bool IsSolid(BlockId id) => Solid[(byte)id];

        public static bool IsLiquid(BlockId id) => Liquid[(byte)id];

        public static bool IsReplaceable(BlockId id) => Get(id).IsReplaceable;

        private static BlockDefinition Simple(
            BlockId id,
            string name,
            BlockColor color,
            float colorNoise = 0.06f)
        {
            return new BlockDefinition(
                id, name, true, true, false,
                color, color, color,
                BiomeTint.None, colorNoise);
        }

        private static void Register(BlockDefinition definition)
        {
            byte index = (byte)definition.Id;

            if (Definitions[index] != null)
            {
                throw new InvalidOperationException(
                    $"Block {definition.Id} is registered twice.");
            }

            Definitions[index] = definition;
            Opaque[index] = definition.IsOpaque;
            Solid[index] = definition.IsSolid;
            Liquid[index] = definition.IsLiquid;
        }
    }
}
