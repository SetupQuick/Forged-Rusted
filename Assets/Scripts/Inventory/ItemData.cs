using UnityEngine;
using World.Blocks;

namespace Items
{
    /// <summary>
    /// Definition of an item type (not an item instance - counts live in
    /// <see cref="ItemStack"/>).
    ///
    /// Item assets go in a Resources/Items folder so
    /// <see cref="ItemDatabase"/> can find them by id. Block items are
    /// created automatically for every placeable block.
    /// </summary>
    [CreateAssetMenu(fileName = "New Item", menuName = "Inventory/Item")]
    public class ItemData : ScriptableObject
    {
        [Tooltip("Unique id used in saves and network messages. " +
                 "Defaults to the asset name.")]
        public string id;

        public string itemName = "New Item";

        [Tooltip("Optional. A voxel-style icon is generated when empty.")]
        public Sprite icon;

        [TextArea]
        public string description;

        [Min(1)]
        public int maxStackSize = 64;

        [Header("Block")]
        [Tooltip("Placing this item puts a block into the world.")]
        public bool placesBlock;

        public BlockId block;

        /// <summary>Stable id; falls back to the asset name.</summary>
        public string Id =>
            string.IsNullOrEmpty(id) ? name : id;

        public string DisplayName =>
            string.IsNullOrEmpty(itemName) ? name : itemName;

        /// <summary>Creates the item that represents a block.</summary>
        public static ItemData CreateBlockItem(BlockId blockId)
        {
            BlockDefinition definition = BlockRegistry.Get(blockId);

            var item = CreateInstance<ItemData>();
            item.name = "block_" + blockId;
            item.id = "block:" + blockId.ToString().ToLowerInvariant();
            item.itemName = definition.Name;
            item.placesBlock = true;
            item.block = blockId;
            item.maxStackSize = 64;
            item.hideFlags = HideFlags.DontSave;

            return item;
        }
    }
}
