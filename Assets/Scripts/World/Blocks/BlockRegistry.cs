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
            float colorNoise = 0.06f)
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

            Register(new BlockDefinition(
                BlockId.Air, "Air", false, false, false,
                C(0), C(0), C(0)));

            // Grass colours are multiplied by the biome tint.
            Register(new BlockDefinition(
                BlockId.Grass, "Grass", true, true, false,
                C(0xFFFFFF), C(0xC8C8C8), C(0x7A5536),
                BiomeTint.Grass));

            Register(Simple(BlockId.Dirt, "Dirt", C(0x7A5536)));
            Register(Simple(BlockId.Stone, "Stone", C(0x8A8A8E)));
            Register(Simple(BlockId.Bedrock, "Bedrock", C(0x2E2E33), 0.12f));

            Register(new BlockDefinition(
                BlockId.Water, "Water", false, false, true,
                C(0x2F6FCF), C(0x2A63BA), C(0x2558A6),
                BiomeTint.None, 0.02f));

            Register(Simple(BlockId.Sand, "Sand", C(0xDCCB8C)));
            Register(new BlockDefinition(
                BlockId.Sandstone, "Sandstone", true, true, false,
                C(0xD6C083), C(0xC9B072), C(0xD6C083)));
            Register(Simple(BlockId.Gravel, "Gravel", C(0x8D847C), 0.12f));
            Register(Simple(BlockId.Clay, "Clay", C(0x9EA4B0)));
            Register(Simple(BlockId.Snow, "Snow", C(0xF4F7FB), 0.03f));

            Register(new BlockDefinition(
                BlockId.Ice, "Ice", true, true, false,
                C(0xA9D2F5), C(0x9CC6EB), C(0xA9D2F5),
                BiomeTint.None, 0.03f));

            Register(new BlockDefinition(
                BlockId.Log, "Log", true, true, false,
                C(0xA4814F), C(0x6B4A2B), C(0xA4814F)));

            Register(new BlockDefinition(
                BlockId.Leaves, "Leaves", true, true, false,
                C(0xFFFFFF), C(0xFFFFFF), C(0xE0E0E0),
                BiomeTint.Foliage, 0.1f));

            Register(new BlockDefinition(
                BlockId.PineLog, "Pine Log", true, true, false,
                C(0x8C6A45), C(0x4F3A26), C(0x8C6A45)));

            Register(Simple(BlockId.PineLeaves, "Pine Leaves", C(0x2F5A3A), 0.1f));

            Register(new BlockDefinition(
                BlockId.Cactus, "Cactus", true, true, false,
                C(0x5C9A3C), C(0x4A8530), C(0x5C9A3C)));

            Register(Simple(BlockId.CoalOre, "Coal Ore", C(0x4A4A4E), 0.18f));
            Register(Simple(BlockId.CopperOre, "Copper Ore", C(0xB06A3E), 0.15f));
            Register(Simple(BlockId.IronOre, "Iron Ore", C(0xB79A85), 0.15f));
            Register(Simple(BlockId.GoldOre, "Gold Ore", C(0xE0B73A), 0.12f));
            Register(Simple(BlockId.CrystalOre, "Crystal Ore", C(0x5FD3D8), 0.12f));
            Register(Simple(BlockId.MossyStone, "Mossy Stone", C(0x6F8466), 0.12f));
        }

        public static BlockDefinition Get(BlockId id)
        {
            return Definitions[(byte)id] ??
                   Definitions[(byte)BlockId.Stone];
        }

        public static bool IsOpaque(BlockId id) => Opaque[(byte)id];

        public static bool IsSolid(BlockId id) => Solid[(byte)id];

        public static bool IsLiquid(BlockId id) => Liquid[(byte)id];

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
