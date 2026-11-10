using System.Collections.Generic;
using UnityEngine;
using World.Blocks;
using World.Generation;

namespace Items
{
    /// <summary>
    /// Generates icons for items that have none: a small isometric voxel
    /// cube in the block's colours for block items, and a coloured tile
    /// for anything else. Cached per item.
    /// </summary>
    public static class ItemIcons
    {
        private const int Size = 48;

        private static readonly Dictionary<string, Sprite> Cache =
            new Dictionary<string, Sprite>();

        public static Sprite Get(ItemData item)
        {
            if (item == null)
                return null;

            if (item.icon != null)
                return item.icon;

            if (Cache.TryGetValue(item.Id, out Sprite cached) && cached != null)
                return cached;

            string[] shape = ItemShapes.For(item);
            Texture2D texture = item.placesBlock
                ? DrawBlock(item.block)
                : shape != null ? DrawShape(shape, item) : DrawTile(item.Id);

            texture.name = "Icon_" + item.Id;

            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0, 0, Size, Size),
                new Vector2(0.5f, 0.5f),
                100f);

            sprite.name = texture.name;
            Cache[item.Id] = sprite;

            return sprite;
        }

        private static Texture2D NewTexture()
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            var clear = new Color32[Size * Size];
            texture.SetPixels32(clear);
            return texture;
        }

        private static Texture2D DrawBlock(BlockId block)
        {
            Texture2D texture = NewTexture();
            BlockDefinition definition = BlockRegistry.Get(block);
            BiomeDefinition plains = BiomeRegistry.Get(BiomeId.Plains);

            Color top = Tinted(definition.TopColor, definition, plains);
            Color side = Tinted(definition.SideColor, definition, plains);

            const float margin = 3f;
            float a = (Size - margin * 2f) / 2.1f;   // half width
            float hTop = a * 0.5f;                   // half height of the top rhombus
            float h = a * 1.1f;                      // side height
            float cx = Size * 0.5f;
            float cyTop = margin + h + hTop;

            var pixels = new Color32[Size * Size];

            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float px = x + 0.5f;
                float py = y + 0.5f;
                float dx = px - cx;

                Color? color = null;

                if (Mathf.Abs(dx) / a + Mathf.Abs(py - cyTop) / hTop <= 1f)
                {
                    color = top;
                }
                else if (Mathf.Abs(dx) <= a)
                {
                    // Upper edge of the side faces follows the bottom of the rhombus.
                    float upper = cyTop - hTop * (1f - Mathf.Abs(dx) / a);
                    float lower = upper - h;

                    if (py <= upper && py >= lower)
                        color = dx < 0 ? side * 0.82f : side * 0.64f;
                }

                if (color.HasValue)
                {
                    Color c = color.Value;
                    c.a = 1f;
                    pixels[x + y * Size] = c;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        /// <summary>Pixel-art icon from an ItemShapes pattern, with a dark outline.</summary>
        private static Texture2D DrawShape(string[] shape, ItemData item)
        {
            Texture2D texture = NewTexture();
            int grid = ItemShapes.GridSize;
            int scale = (Size - 4) / grid;
            int offset = (Size - grid * scale) / 2;
            var pixels = new Color32[Size * Size];
            var outline = new Color32(20, 16, 12, 230);

            bool Filled(int gx, int gy) =>
                gx >= 0 && gy >= 0 && gx < grid && gy < grid && shape[gy][gx] != '.';

            for (int gy = 0; gy < grid; gy++)
            for (int gx = 0; gx < grid; gx++)
            {
                Color32 color = Filled(gx, gy) ? ItemShapes.ColorOf(shape[gy][gx], item) : default;
                bool edge = !Filled(gx, gy) &&
                            (Filled(gx - 1, gy) || Filled(gx + 1, gy) || Filled(gx, gy - 1) || Filled(gx, gy + 1));

                if (color.a == 0 && !edge)
                    continue;

                for (int py = 0; py < scale; py++)
                for (int px = 0; px < scale; px++)
                {
                    int x = offset + gx * scale + px;
                    int y = Size - 1 - (offset + gy * scale + py); // row 0 at the top
                    pixels[x + y * Size] = color.a == 0 ? outline : color;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static Texture2D DrawTile(string id)
        {
            Texture2D texture = NewTexture();

            // Stable colour from the id.
            uint hash = 2166136261;
            foreach (char ch in id)
            {
                hash ^= ch;
                hash *= 16777619;
            }

            Color color = Color.HSVToRGB((hash % 360) / 360f, 0.55f, 0.85f);
            Color dark = color * 0.6f;
            dark.a = 1f;

            var pixels = new Color32[Size * Size];

            for (int y = 6; y < Size - 6; y++)
            for (int x = 6; x < Size - 6; x++)
            {
                bool border = x < 9 || y < 9 || x >= Size - 9 || y >= Size - 9;
                pixels[x + y * Size] = border ? dark : color;
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static Color Tinted(BlockColor color, BlockDefinition definition, BiomeDefinition biome)
        {
            var result = new Color(color.R / 255f, color.G / 255f, color.B / 255f, 1f);

            BlockColor? tint =
                definition.Tint == BiomeTint.Grass ? biome.GrassTint :
                definition.Tint == BiomeTint.Foliage ? biome.FoliageTint :
                (BlockColor?)null;

            if (tint.HasValue)
            {
                result.r *= tint.Value.R / 255f;
                result.g *= tint.Value.G / 255f;
                result.b *= tint.Value.B / 255f;
            }

            return result;
        }
    }
}
