using System.Collections.Generic;
using UnityEngine;
using World.Blocks;

namespace Items
{
    /// <summary>
    /// Looks up item definitions by id: every ItemData asset in a
    /// Resources/Items folder, plus one generated item per placeable
    /// block.
    /// </summary>
    public static class ItemDatabase
    {
        private static Dictionary<string, ItemData> _items;
        private static Dictionary<BlockId, ItemData> _blockItems;

        public static IEnumerable<ItemData> All
        {
            get
            {
                EnsureLoaded();
                return _items.Values;
            }
        }

        public static bool TryGet(string id, out ItemData item)
        {
            EnsureLoaded();
            return _items.TryGetValue(id, out item);
        }

        /// <summary>The item for a block, or null for blocks that cannot be held (air, water, bedrock).</summary>
        public static ItemData GetBlockItem(BlockId block)
        {
            EnsureLoaded();
            _blockItems.TryGetValue(block, out ItemData item);
            return item;
        }

        private static void EnsureLoaded()
        {
            if (_items != null)
                return;

            _items = new Dictionary<string, ItemData>();
            _blockItems = new Dictionary<BlockId, ItemData>();

            foreach (ItemData item in Resources.LoadAll<ItemData>("Items"))
            {
                if (_items.ContainsKey(item.Id))
                {
                    Debug.LogWarning($"Duplicate item id '{item.Id}' ({item.name}).");
                    continue;
                }

                _items.Add(item.Id, item);

                if (item.placesBlock && !_blockItems.ContainsKey(item.block))
                    _blockItems.Add(item.block, item);
            }

            foreach (BlockId block in System.Enum.GetValues(typeof(BlockId)))
            {
                if (!IsHoldable(block) || _blockItems.ContainsKey(block))
                    continue;

                ItemData item = ItemData.CreateBlockItem(block);
                _items[item.Id] = item;
                _blockItems.Add(block, item);
            }
        }

        private static bool IsHoldable(BlockId block)
        {
            return block != BlockId.Air &&
                   block != BlockId.Bedrock &&
                   !BlockRegistry.IsLiquid(block);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _items = null;
            _blockItems = null;
        }
    }
}
