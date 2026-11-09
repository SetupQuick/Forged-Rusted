using Items;
using World.Blocks;

namespace Gameplay
{
    /// <summary>Inventory size and starting items, shared by server and client.</summary>
    public static class InventoryLayout
    {
        public const int HotbarSize = 9;
        public const int BackpackSize = 27;
        public const int SlotCount = HotbarSize + BackpackSize;

        /// <summary>
        /// Players start with some building blocks until mining and
        /// crafting exist. Set to false to start empty.
        /// </summary>
        public static bool GiveStarterBlocks = true;

        private static readonly BlockId[] StarterBlocks =
        {
            BlockId.Grass, BlockId.Dirt, BlockId.Stone, BlockId.Sand,
            BlockId.Log, BlockId.Leaves, BlockId.Sandstone, BlockId.AutumnLeaves, BlockId.Snow
        };

        public static InventoryContainer CreateContainer()
        {
            return new InventoryContainer(SlotCount, HotbarSize);
        }

        public static void AddStarterItems(InventoryContainer container)
        {
            if (!GiveStarterBlocks)
                return;

            foreach (BlockId block in StarterBlocks)
            {
                ItemData item = ItemDatabase.GetBlockItem(block);

                if (item != null)
                    container.Add(item, 64);
            }
        }
    }
}
