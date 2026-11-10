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
            BlockId.Log, BlockId.Leaves, BlockId.Sandstone, BlockId.AutumnLeaves, BlockId.Snow, BlockId.Chest
        };

        public static InventoryContainer CreateContainer()
        {
            return new InventoryContainer(SlotCount, HotbarSize);
        }

        /// <summary>Test tools in the first hotbar slots (until crafting exists).</summary>
        public static bool GiveStarterTools = true;

        public static void AddStarterItems(InventoryContainer container)
        {
            if (GiveStarterTools)
            {
                foreach (ToolType type in new[] { ToolType.Sword, ToolType.Pickaxe, ToolType.Axe, ToolType.Shovel })
                {
                    if (ItemDatabase.TryGet(GameItems.ToolId("stone", type), out ItemData tool))
                        container.Add(tool, 1);
                }
            }

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
