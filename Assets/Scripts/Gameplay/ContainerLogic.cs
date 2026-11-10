using System;
using System.Collections.Generic;
using Items;

namespace Gameplay
{
    /// <summary>
    /// Minecraft-style inventory handling over the player's inventory
    /// (slots 0..35: hotbar 0..8, backpack 9..35) plus, while one is open,
    /// a chest (slots 36..). Run by the server (authoritative) and by the
    /// client for prediction, so both agree.
    ///
    ///   click / right-click        pick up, put down, swap / half, one
    ///   shift + click              quick move: chest <-> inventory, or hotbar <-> backpack
    ///   drag with a stack          left: spread evenly, right: one per slot
    ///   double-click               collect the same item onto the cursor
    ///   1-9 over a slot            swap with that hotbar slot
    ///   chest buttons              quick stack, deposit all, take all
    /// </summary>
    public static class ContainerLogic
    {
        public const int PlayerSlots = InventoryLayout.SlotCount;
        public const int ChestSize = 27;

        public enum Command : byte
        {
            QuickStack = 0,   // put items the chest already has into it
            DepositAll = 1,   // put the whole backpack into the chest
            TakeAll = 2       // take everything out of the chest
        }

        private static bool Resolve(InventoryContainer player, InventoryContainer chest, int slot, out InventoryContainer container, out int index)
        {
            if (slot >= 0 && slot < player.Count)
            {
                container = player;
                index = slot;
                return true;
            }

            if (chest != null && slot >= PlayerSlots && slot < PlayerSlots + chest.Count)
            {
                container = chest;
                index = slot - PlayerSlots;
                return true;
            }

            container = null;
            index = -1;
            return false;
        }

        public static void Click(InventoryContainer player, InventoryContainer chest, ref ItemStack held, int slot, int button, bool shift)
        {
            if (!Resolve(player, chest, slot, out InventoryContainer container, out int index))
                return;

            if (button == 0)
            {
                if (shift && held.IsEmpty)
                    QuickMove(player, chest, container, index);
                else
                    held = container.PlaceOrSwap(index, held);
            }
            else if (button == 1)
            {
                held = held.IsEmpty ? container.TakeHalf(index) : container.PlaceOne(index, held);
            }
        }

        /// <summary>Shift-click: to the other container, or hotbar <-> backpack without a chest.</summary>
        private static void QuickMove(InventoryContainer player, InventoryContainer chest, InventoryContainer from, int index)
        {
            ItemStack stack = from[index];
            if (stack.IsEmpty)
                return;

            if (chest == null)
            {
                from.QuickMove(index);
                return;
            }

            InventoryContainer to = from == chest ? player : chest;
            int leftover = to.Add(stack.Item, stack.Count);
            from.Set(index, stack.WithCount(leftover));
        }

        /// <summary>
        /// Drag with a stack on the cursor: left spreads it evenly over the
        /// slots, right puts one in each. Only empty slots or slots with the
        /// same item and room are used.
        /// </summary>
        public static void Spread(InventoryContainer player, InventoryContainer chest, ref ItemStack held, IReadOnlyList<int> slots, bool oneEach)
        {
            if (held.IsEmpty || slots == null || slots.Count == 0)
                return;

            var targets = new List<(InventoryContainer Container, int Index)>();
            var seen = new HashSet<int>();

            foreach (int slot in slots)
            {
                if (!seen.Add(slot) || !Resolve(player, chest, slot, out InventoryContainer container, out int index))
                    continue;

                ItemStack there = container[index];
                if (there.IsEmpty || (there.CanStackWith(held) && there.Count < there.MaxStack))
                    targets.Add((container, index));
            }

            if (targets.Count == 0)
                return;

            int each = oneEach ? 1 : Math.Max(1, held.Count / targets.Count);

            foreach ((InventoryContainer container, int index) in targets)
            {
                if (held.IsEmpty)
                    break;

                ItemStack there = container[index];
                int room = (there.IsEmpty ? held.MaxStack : there.MaxStack - there.Count);
                int put = Math.Min(Math.Min(each, room), held.Count);
                if (put <= 0)
                    continue;

                container.Set(index, new ItemStack(held.Item, (there.IsEmpty ? 0 : there.Count) + put));
                held = held.WithCount(held.Count - put);
            }
        }

        /// <summary>Double-click: gather the same item from every slot onto the cursor (up to a full stack).</summary>
        public static void Collect(InventoryContainer player, InventoryContainer chest, ref ItemStack held, int slot)
        {
            if (held.IsEmpty)
            {
                if (!Resolve(player, chest, slot, out InventoryContainer container, out int index) || container[index].IsEmpty)
                    return;

                held = container[index];
                container.Set(index, ItemStack.Empty);
            }

            // Chest first, then the backpack, then the hotbar (like Minecraft).
            var order = new List<InventoryContainer>();
            if (chest != null) order.Add(chest);
            order.Add(player);

            foreach (InventoryContainer container in order)
            {
                for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < container.Count && held.Count < held.MaxStack; i++)
                {
                    bool hotbar = container == player && i < player.HotbarSize;
                    if ((pass == 0) == hotbar)
                        continue;

                    ItemStack there = container[i];
                    if (!there.CanStackWith(held))
                        continue;

                    int take = Math.Min(there.Count, held.MaxStack - held.Count);
                    container.Set(i, there.WithCount(there.Count - take));
                    held = held.WithCount(held.Count + take);
                }
            }
        }

        /// <summary>Number key over a slot: swap it with that hotbar slot.</summary>
        public static void SwapWithHotbar(InventoryContainer player, InventoryContainer chest, int slot, int hotbarIndex)
        {
            if (hotbarIndex < 0 || hotbarIndex >= player.HotbarSize ||
                !Resolve(player, chest, slot, out InventoryContainer container, out int index))
            {
                return;
            }

            if (container == player && index == hotbarIndex)
                return;

            ItemStack a = container[index];
            ItemStack b = player[hotbarIndex];
            container.Set(index, b);
            player.Set(hotbarIndex, a);
        }

        public static void RunCommand(InventoryContainer player, InventoryContainer chest, Command command)
        {
            if (chest == null)
                return;

            switch (command)
            {
                case Command.QuickStack:
                    for (int i = player.HotbarSize; i < player.Count; i++)
                    {
                        ItemStack stack = player[i];
                        if (!stack.IsEmpty && chest.CountOf(stack.Item.Id) > 0)
                            player.Set(i, stack.WithCount(chest.Add(stack.Item, stack.Count)));
                    }
                    break;

                case Command.DepositAll:
                    for (int i = player.HotbarSize; i < player.Count; i++)
                    {
                        ItemStack stack = player[i];
                        if (!stack.IsEmpty)
                            player.Set(i, stack.WithCount(chest.Add(stack.Item, stack.Count)));
                    }
                    break;

                case Command.TakeAll:
                    for (int i = 0; i < chest.Count; i++)
                    {
                        ItemStack stack = chest[i];
                        if (!stack.IsEmpty)
                            chest.Set(i, stack.WithCount(player.Add(stack.Item, stack.Count)));
                    }
                    break;
            }
        }
    }
}
