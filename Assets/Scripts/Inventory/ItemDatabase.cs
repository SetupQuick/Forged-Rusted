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

        // Network ids: index into the id-sorted item list, plus one (0 = empty).
        private static List<ItemData> _byNetworkId;
        private static Dictionary<string, ushort> _networkIds;

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

        /// <summary>
        /// Compact id for network messages (0 = no item). Identical on every
        /// machine running the same build, because it is the position in
        /// the list of all item ids sorted alphabetically.
        /// </summary>
        public static ushort GetNetworkId(ItemData item)
        {
            EnsureLoaded();

            if (item == null)
                return 0;

            return _networkIds.TryGetValue(item.Id, out ushort id) ? id : (ushort)0;
        }

        public static ItemData GetByNetworkId(ushort networkId)
        {
            EnsureLoaded();

            if (networkId == 0 || networkId > _byNetworkId.Count)
                return null;

            return _byNetworkId[networkId - 1];
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

            // Built-in items (tools); an asset with the same id wins.
            foreach (ItemData item in GameItems.CreateAll())
            {
                if (!_items.ContainsKey(item.Id))
                    _items.Add(item.Id, item);
            }

            foreach (BlockId block in System.Enum.GetValues(typeof(BlockId)))
            {
                if (!IsHoldable(block) || _blockItems.ContainsKey(block))
                    continue;

                ItemData item = ItemData.CreateBlockItem(block);
                _items[item.Id] = item;
                _blockItems.Add(block, item);
            }

            var ids = new List<string>(_items.Keys);
            ids.Sort(System.StringComparer.Ordinal);

            _byNetworkId = new List<ItemData>(ids.Count);
            _networkIds = new Dictionary<string, ushort>(ids.Count);

            foreach (string id in ids)
            {
                _byNetworkId.Add(_items[id]);
                _networkIds[id] = (ushort)_byNetworkId.Count;
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
            _byNetworkId = null;
            _networkIds = null;
        }
    }
}
