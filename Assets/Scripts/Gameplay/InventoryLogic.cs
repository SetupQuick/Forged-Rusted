using Items;
using World.Blocks;

namespace Gameplay
{
    /// <summary>
    /// What each player action does to an inventory. The server runs it
    /// authoritatively; the client runs exactly the same code to predict,
    /// so both end up with the same result.
    /// </summary>
    public static class InventoryLogic
    {
        public static void Click(InventoryContainer container, ref ItemStack held, int slot, int button, bool shift)
        {
            if (slot < 0 || slot >= container.Count)
                return;

            if (button == 0)
            {
                if (shift && held.IsEmpty)
                    container.QuickMove(slot);
                else
                    held = container.PlaceOrSwap(slot, held);
            }
            else if (button == 1)
            {
                held = held.IsEmpty
                    ? container.TakeHalf(slot)
                    : container.PlaceOne(slot, held);
            }
        }

        /// <summary>Puts the stack on the cursor back into the inventory.</summary>
        public static void Close(InventoryContainer container, ref ItemStack held)
        {
            if (held.IsEmpty)
                return;

            int leftover = container.Add(held.Item, held.Count);
            held = held.WithCount(leftover);
        }

        /// <summary>The block a hotbar slot would place, if it holds one.</summary>
        public static bool TryGetPlaceableBlock(InventoryContainer container, int slot, out BlockId block)
        {
            block = BlockId.Air;

            if (slot < 0 || slot >= container.HotbarSize)
                return false;

            ItemStack stack = container[slot];

            if (stack.IsEmpty || !stack.Item.placesBlock)
                return false;

            block = stack.Item.block;
            return true;
        }

        public static void ConsumeForPlace(InventoryContainer container, int slot)
        {
            container.RemoveFromSlot(slot, 1);
        }

        /// <summary>
        /// Adds what a broken block drops straight into the inventory -
        /// nothing if the held item cannot harvest it (stone by hand).
        /// </summary>
        public static void AddDrop(InventoryContainer container, BlockId broken, ItemData heldTool)
        {
            if (!Mining.CanHarvest(broken, heldTool))
                return;

            BlockId drop = BlockEditRules.DropFor(broken);

            if (drop == BlockId.Air)
                return;

            ItemData item = ItemDatabase.GetBlockItem(drop);

            if (item != null)
                container.Add(item, 1);
        }
    }
}
