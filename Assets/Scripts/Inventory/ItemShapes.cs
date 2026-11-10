using UnityEngine;

namespace Items
{
    /// <summary>
    /// 12x12 pixel shapes for items without their own art (tools, food).
    /// The same shape draws the inventory icon (ItemIcons) and, extruded
    /// into small voxels, the model held in the hand (HeldItemModel), so
    /// both always match. Row 0 is the top; the handle ends bottom-left,
    /// which is where the hand holds it.
    ///   m = material colour, d = darker material, h = handle (wood),
    ///   g = guard (dark)
    /// </summary>
    public static class ItemShapes
    {
        public const int GridSize = 12;

        private static readonly string[] Pickaxe =
        {
            "...mmmmmm...",
            "..md....dm..",
            ".md....h.dm.",
            ".m....h...m.",
            "......h.....",
            ".....h......",
            "....h.......",
            "...h........",
            "..h.........",
            ".h..........",
            "h...........",
            "............",
        };

        private static readonly string[] Axe =
        {
            "......mmm...",
            ".....mmmmm..",
            ".....mmdhm..",
            "......mh....",
            "......h.....",
            ".....h......",
            "....h.......",
            "...h........",
            "..h.........",
            ".h..........",
            "h...........",
            "............",
        };

        private static readonly string[] Shovel =
        {
            ".........mm.",
            "........mmmm",
            ".......mmmdm",
            "........mdm.",
            ".......h....",
            "......h.....",
            ".....h......",
            "....h.......",
            "...h........",
            "..h.........",
            ".h..........",
            "h...........",
        };

        private static readonly string[] Sword =
        {
            "..........mm",
            ".........mdm",
            "........mdm.",
            ".......mdm..",
            "......mdm...",
            ".....mdm....",
            "..g.mdm.....",
            "...gdm......",
            "...hg.......",
            "..h..g......",
            ".h..........",
            "h...........",
        };

        private static readonly string[] Meat =
        {
            "............",
            "............",
            "....mmmm....",
            "...mmmmmm...",
            "..mmmddmmm..",
            "..mmddddmm..",
            "..mmmddmmm..",
            "...mmmmmmh..",
            "....mmmm.hh.",
            "..........h.",
            "............",
            "............",
        };

        /// <summary>The shape for an item, or null (use the default tile).</summary>
        public static string[] For(ItemData item)
        {
            if (item == null)
                return null;

            switch (item.toolType)
            {
                case ToolType.Pickaxe: return Pickaxe;
                case ToolType.Axe: return Axe;
                case ToolType.Shovel: return Shovel;
                case ToolType.Sword: return Sword;
            }

            return item.healAmount > 0f ? Meat : null;
        }

        /// <summary>Colour of one shape pixel (alpha 0 = empty).</summary>
        public static Color32 ColorOf(char pixel, ItemData item)
        {
            Color material = item.toolType != ToolType.None ? GameItems.ToolColor(item) : FoodColor(item);

            switch (pixel)
            {
                case 'm': return material;
                case 'd': return new Color(material.r * 0.72f, material.g * 0.72f, material.b * 0.72f, 1f);
                case 'h': return item.toolType != ToolType.None ? new Color(0.48f, 0.33f, 0.19f) : new Color(0.93f, 0.9f, 0.85f);
                case 'g': return new Color(0.3f, 0.26f, 0.22f);
                default: return new Color32(0, 0, 0, 0);
            }
        }

        private static Color FoodColor(ItemData item)
        {
            return item.Id.ToLowerInvariant().Contains("chicken")
                ? new Color(0.95f, 0.78f, 0.62f)
                : new Color(0.9f, 0.45f, 0.45f);
        }
    }
}
