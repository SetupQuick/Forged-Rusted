using World.Blocks;

namespace Gameplay
{
    /// <summary>
    /// Rules for breaking and placing blocks, shared by the server
    /// (authoritative validation) and clients (prediction and UI), so
    /// both always agree.
    /// </summary>
    public static class BlockEditRules
    {
        /// <summary>How far a player can reach, in metres from the eyes.</summary>
        public const float ReachMetres = 5f;

        /// <summary>Extra distance the server tolerates for latency.</summary>
        public const float ServerReachTolerance = 1.5f;

        /// <summary>Seconds between repeated breaks/places while holding the button.</summary>
        public const float RepeatInterval = 0.2f;

        public static bool CanBreak(BlockId block)
        {
            return BlockRegistry.Get(block).IsBreakable;
        }

        /// <summary>Whether a block can be placed into a cell holding this block.</summary>
        public static bool CanPlaceInto(BlockId existing)
        {
            return BlockRegistry.IsReplaceable(existing);
        }

        /// <summary>Whether something can be aimed at (selected) with the cursor.</summary>
        public static bool IsTargetable(BlockId block)
        {
            return block != BlockId.Air && !BlockRegistry.IsLiquid(block);
        }

        /// <summary>The block item a broken block gives, or Air for nothing.</summary>
        public static BlockId DropFor(BlockId broken)
        {
            switch (broken)
            {
                case BlockId.Grass:
                    return BlockId.Dirt;

                case BlockId.Ice:
                case BlockId.TallGrass:
                case BlockId.DryGrass:
                case BlockId.FlowerRed:
                case BlockId.FlowerYellow:
                case BlockId.FlowerBlue:
                case BlockId.FlowerWhite:
                    return BlockId.Air;

                default:
                    return broken;
            }
        }
    }
}
