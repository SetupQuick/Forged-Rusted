namespace Items
{
    /// <summary>A number of identical items in one slot.</summary>
    public readonly struct ItemStack
    {
        public static readonly ItemStack Empty = default;

        public ItemData Item { get; }

        public int Count { get; }

        public bool IsEmpty => Item == null || Count <= 0;

        public int MaxStack => Item == null ? 0 : (Item.maxStackSize < 1 ? 1 : Item.maxStackSize);

        public ItemStack(ItemData item, int count)
        {
            if (item == null || count <= 0)
            {
                Item = null;
                Count = 0;
            }
            else
            {
                Item = item;
                Count = count;
            }
        }

        public ItemStack WithCount(int count)
        {
            return new ItemStack(Item, count);
        }

        public bool CanStackWith(ItemStack other)
        {
            return !IsEmpty && !other.IsEmpty && Item.Id == other.Item.Id;
        }

        public override string ToString()
        {
            return IsEmpty ? "(empty)" : $"{Count}x {Item.DisplayName}";
        }
    }
}
