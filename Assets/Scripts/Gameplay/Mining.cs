using Items;
using World.Blocks;

namespace Gameplay
{
    /// <summary>
    /// How long blocks take to break and what they drop, depending on the
    /// held tool. Shared by the server (validation) and clients (break
    /// progress, prediction), so both always agree.
    ///
    ///   time = hardness x (1.5 if the block can be harvested, else 5) / speed
    ///   speed = tool speed if it is the right tool type, else 1 (hand)
    ///
    /// Blocks that need a tool (stone, ores) drop nothing without the
    /// right type and tier - breaking them by hand is also very slow.
    /// </summary>
    public static class Mining
    {
        public struct BlockInfo
        {
            /// <summary>Base hardness (seconds-ish); 0 = instant, negative = unbreakable.</summary>
            public float Hardness;

            /// <summary>Tool that mines it fast.</summary>
            public ToolType Tool;

            /// <summary>Minimum tool tier to get a drop (0 = anything, even the hand).</summary>
            public int RequiredTier;
        }

        public static BlockInfo Get(BlockId block)
        {
            switch (block)
            {
                case BlockId.Air:
                case BlockId.Water:
                case BlockId.Bedrock:
                    return new BlockInfo { Hardness = -1f };

                case BlockId.Grass: return Info(0.6f, ToolType.Shovel);
                case BlockId.Dirt: return Info(0.5f, ToolType.Shovel);
                case BlockId.Sand: return Info(0.5f, ToolType.Shovel);
                case BlockId.Gravel: return Info(0.6f, ToolType.Shovel);
                case BlockId.Clay: return Info(0.6f, ToolType.Shovel);
                case BlockId.Snow: return Info(0.2f, ToolType.Shovel);

                case BlockId.Stone: return Info(1.5f, ToolType.Pickaxe, 1);
                case BlockId.MossyStone: return Info(1.5f, ToolType.Pickaxe, 1);
                case BlockId.Sandstone: return Info(0.8f, ToolType.Pickaxe, 1);
                case BlockId.Ice: return Info(0.5f, ToolType.Pickaxe);

                case BlockId.CoalOre: return Info(3f, ToolType.Pickaxe, 1);
                case BlockId.CopperOre: return Info(3f, ToolType.Pickaxe, 2);
                case BlockId.IronOre: return Info(3f, ToolType.Pickaxe, 2);
                case BlockId.GoldOre: return Info(3f, ToolType.Pickaxe, 3);
                case BlockId.CrystalOre: return Info(3.5f, ToolType.Pickaxe, 3);

                case BlockId.Log:
                case BlockId.PineLog: return Info(2f, ToolType.Axe);
                case BlockId.Cactus: return Info(0.4f, ToolType.Axe);

                case BlockId.Leaves:
                case BlockId.PineLeaves:
                case BlockId.AutumnLeaves:
                case BlockId.RedLeaves: return Info(0.2f, ToolType.Sword);

                default:
                    // Decorations (grass tufts, flowers) and anything new: instant.
                    return BlockRegistry.Get(block).Shape != BlockShape.Cube
                        ? Info(0f, ToolType.None)
                        : Info(1f, ToolType.None);
            }
        }

        private static BlockInfo Info(float hardness, ToolType tool, int tier = 0) =>
            new BlockInfo { Hardness = hardness, Tool = tool, RequiredTier = tier };

        public static bool IsRightTool(BlockId block, ItemData held)
        {
            BlockInfo info = Get(block);
            return held != null && info.Tool != ToolType.None && held.toolType == info.Tool;
        }

        /// <summary>Whether breaking the block with this item gives its drop.</summary>
        public static bool CanHarvest(BlockId block, ItemData held)
        {
            BlockInfo info = Get(block);
            if (info.RequiredTier <= 0)
                return true;

            return IsRightTool(block, held) && held.toolTier >= info.RequiredTier;
        }

        /// <summary>Seconds to break the block with this item; negative = cannot be broken.</summary>
        public static float BreakSeconds(BlockId block, ItemData held)
        {
            BlockInfo info = Get(block);
            if (info.Hardness < 0f)
                return -1f;

            if (info.Hardness == 0f)
                return 0f;

            float speed = IsRightTool(block, held) ? System.Math.Max(1f, held.toolSpeed) : 1f;
            float factor = CanHarvest(block, held) ? 1.5f : 5f;

            return info.Hardness * factor / speed;
        }

        /// <summary>
        /// The server accepts a break this early (fraction of the full
        /// time), so network delay never makes honest players fail.
        /// </summary>
        public const float ServerTimeTolerance = 0.7f;
        public const float ServerLatencyAllowance = 0.15f;
    }
}
