using System;

namespace Items
{
    /// <summary>
    /// A fixed number of item slots with stacking. Plain C# (no Unity
    /// objects needed beyond the item definitions), so the same class
    /// can later run on the server for an authoritative inventory.
    ///
    /// The first <see cref="HotbarSize"/> slots are the hotbar.
    /// </summary>
    public sealed class InventoryContainer
    {
        private readonly ItemStack[] _slots;

        public int Count => _slots.Length;

        public int HotbarSize { get; }

        /// <summary>Raised after any change.</summary>
        public event Action Changed;

        public InventoryContainer(int slotCount, int hotbarSize)
        {
            if (slotCount < 1)
                throw new ArgumentOutOfRangeException(nameof(slotCount));

            _slots = new ItemStack[slotCount];
            HotbarSize = Math.Max(0, Math.Min(hotbarSize, slotCount));
        }

        public ItemStack this[int index] => _slots[index];

        public void Set(int index, ItemStack stack)
        {
            _slots[index] = stack.IsEmpty ? ItemStack.Empty : stack;
            Changed?.Invoke();
        }

        /// <summary>
        /// Adds items, filling existing stacks first (hotbar first), then
        /// empty slots. Returns how many did not fit.
        /// </summary>
        public int Add(ItemData item, int amount)
        {
            if (item == null || amount <= 0)
                return amount;

            int remaining = amount;
            int max = new ItemStack(item, 1).MaxStack;

            for (int i = 0; i < _slots.Length && remaining > 0; i++)
            {
                ItemStack slot = _slots[i];

                if (slot.IsEmpty || slot.Item.Id != item.Id || slot.Count >= max)
                    continue;

                int moved = Math.Min(remaining, max - slot.Count);
                _slots[i] = slot.WithCount(slot.Count + moved);
                remaining -= moved;
            }

            for (int i = 0; i < _slots.Length && remaining > 0; i++)
            {
                if (!_slots[i].IsEmpty)
                    continue;

                int moved = Math.Min(remaining, max);
                _slots[i] = new ItemStack(item, moved);
                remaining -= moved;
            }

            if (remaining != amount)
                Changed?.Invoke();

            return remaining;
        }

        /// <summary>Removes up to <paramref name="amount"/> from one slot. Returns how many were removed.</summary>
        public int RemoveFromSlot(int index, int amount)
        {
            ItemStack slot = _slots[index];

            if (slot.IsEmpty || amount <= 0)
                return 0;

            int removed = Math.Min(amount, slot.Count);
            _slots[index] = slot.WithCount(slot.Count - removed);
            Changed?.Invoke();

            return removed;
        }

        /// <summary>Total number of an item across all slots.</summary>
        public int CountOf(string itemId)
        {
            int total = 0;

            foreach (ItemStack slot in _slots)
            {
                if (!slot.IsEmpty && slot.Item.Id == itemId)
                    total += slot.Count;
            }

            return total;
        }

        /// <summary>
        /// Puts a held stack into a slot: merges into a matching stack,
        /// or swaps with whatever is there. Returns what is held after.
        /// </summary>
        public ItemStack PlaceOrSwap(int index, ItemStack held)
        {
            ItemStack slot = _slots[index];

            if (held.IsEmpty)
            {
                _slots[index] = ItemStack.Empty;
                Changed?.Invoke();
                return slot;
            }

            if (slot.CanStackWith(held))
            {
                int moved = Math.Min(held.Count, slot.MaxStack - slot.Count);
                _slots[index] = slot.WithCount(slot.Count + moved);
                Changed?.Invoke();
                return held.WithCount(held.Count - moved);
            }

            _slots[index] = held;
            Changed?.Invoke();
            return slot;
        }

        /// <summary>Takes half of a slot (rounded up). Returns the taken stack.</summary>
        public ItemStack TakeHalf(int index)
        {
            ItemStack slot = _slots[index];

            if (slot.IsEmpty)
                return ItemStack.Empty;

            int taken = (slot.Count + 1) / 2;
            _slots[index] = slot.WithCount(slot.Count - taken);
            Changed?.Invoke();

            return slot.WithCount(taken);
        }

        /// <summary>
        /// Puts a single item from the held stack into a slot if it is
        /// empty or holds the same item. Returns what is held after.
        /// </summary>
        public ItemStack PlaceOne(int index, ItemStack held)
        {
            if (held.IsEmpty)
                return held;

            ItemStack slot = _slots[index];

            if (slot.IsEmpty)
            {
                _slots[index] = held.WithCount(1);
            }
            else if (slot.CanStackWith(held) && slot.Count < slot.MaxStack)
            {
                _slots[index] = slot.WithCount(slot.Count + 1);
            }
            else
            {
                return held;
            }

            Changed?.Invoke();
            return held.WithCount(held.Count - 1);
        }

        /// <summary>
        /// Moves a whole slot between the hotbar and the rest of the
        /// inventory (shift-click). Returns true if anything moved.
        /// </summary>
        public bool QuickMove(int index)
        {
            ItemStack slot = _slots[index];

            if (slot.IsEmpty)
                return false;

            bool fromHotbar = index < HotbarSize;
            int start = fromHotbar ? HotbarSize : 0;
            int end = fromHotbar ? _slots.Length : HotbarSize;

            int remaining = slot.Count;

            for (int i = start; i < end && remaining > 0; i++)
            {
                ItemStack target = _slots[i];

                if (!target.CanStackWith(slot) || target.Count >= target.MaxStack)
                    continue;

                int moved = Math.Min(remaining, target.MaxStack - target.Count);
                _slots[i] = target.WithCount(target.Count + moved);
                remaining -= moved;
            }

            for (int i = start; i < end && remaining > 0; i++)
            {
                if (!_slots[i].IsEmpty)
                    continue;

                _slots[i] = slot.WithCount(remaining);
                remaining = 0;
            }

            if (remaining == slot.Count)
                return false;

            _slots[index] = slot.WithCount(remaining);
            Changed?.Invoke();
            return true;
        }
    }
}
