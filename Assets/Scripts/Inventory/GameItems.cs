using UnityEngine;

namespace Items
{
    /// <summary>
    /// Items defined in code (until they get their own assets): the test
    /// tool sets. Materials and recipes will change - these exist so mining
    /// and combat can be tested. An ItemData asset in Resources/Items with
    /// the same id replaces the built-in one.
    /// </summary>
    public static class GameItems
    {
        public struct Material
        {
            public string Id;
            public string Name;
            public int Tier;
            public float Speed;
            public float Damage;
            public Color Color;
        }

        public static readonly Material[] Materials =
        {
            new Material { Id = "wood", Name = "Wooden", Tier = 1, Speed = 2f, Damage = 4f, Color = new Color(0.62f, 0.45f, 0.26f) },
            new Material { Id = "stone", Name = "Stone", Tier = 2, Speed = 4f, Damage = 5f, Color = new Color(0.55f, 0.55f, 0.58f) },
            new Material { Id = "iron", Name = "Iron", Tier = 3, Speed = 6f, Damage = 6f, Color = new Color(0.85f, 0.83f, 0.8f) },
            new Material { Id = "gold", Name = "Golden", Tier = 4, Speed = 9f, Damage = 7f, Color = new Color(0.98f, 0.8f, 0.25f) },
        };

        public static string ToolId(string material, ToolType type) =>
            "tool:" + material + "_" + type.ToString().ToLowerInvariant();

        /// <summary>Colour of a tool's head (icons and the held model).</summary>
        public static Color ToolColor(ItemData item)
        {
            foreach (Material material in Materials)
                if (item.Id.StartsWith("tool:" + material.Id + "_"))
                    return material.Color;
            return Color.gray;
        }

        private static ItemData Food(string id, string name, float heal)
        {
            var item = ScriptableObject.CreateInstance<ItemData>();
            item.id = id;
            item.name = id;
            item.itemName = name;
            item.healAmount = heal;
            item.maxStackSize = 64;
            item.hideFlags = HideFlags.DontSave;
            return item;
        }

        public static System.Collections.Generic.IEnumerable<ItemData> CreateAll()
        {
            // Food dropped by animals. The assets in Resources/Items (same
            // ids) replace these; they exist so loot never refers to an
            // unknown item.
            yield return Food("Pork", "Pork", 6f);
            yield return Food("Chicken", "Chicken", 4f);

            foreach (Material material in Materials)
            {
                foreach (ToolType type in new[] { ToolType.Pickaxe, ToolType.Axe, ToolType.Shovel, ToolType.Sword })
                {
                    var item = ScriptableObject.CreateInstance<ItemData>();
                    item.id = ToolId(material.Id, type);
                    item.name = item.id;
                    item.itemName = material.Name + " " + type;
                    item.maxStackSize = 1;
                    item.toolType = type;
                    item.toolTier = material.Tier;
                    item.toolSpeed = type == ToolType.Sword ? 1.5f : material.Speed;

                    // Swords hit hardest; axes are decent weapons too.
                    item.attackDamage =
                        type == ToolType.Sword ? material.Damage :
                        type == ToolType.Axe ? material.Damage - 1f :
                        2f + material.Tier * 0.5f;

                    item.hideFlags = HideFlags.DontSave;
                    yield return item;
                }
            }
        }
    }
}
