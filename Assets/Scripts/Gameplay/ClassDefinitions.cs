using System.Collections.Generic;

namespace Gameplay
{
    /// <summary>A skill in a class's tree.</summary>
    public sealed class SkillNode
    {
        public string Id;
        public string Name;
        public string Description;
        public int Cost = 1;
        public string[] Requires = new string[0];
        public int Column;   // layout in the skill tree screen
        public int Row;
    }

    public sealed class QuestDefinition
    {
        public string Id;
        public string Name;
        public string Description;
        public QuestGoal Goal;
        public string Target;     // entity kind or block id name
        public int Amount;
        public int RewardExperience;
    }

    public enum QuestGoal : byte
    {
        Kill = 1,
        Mine = 2
    }

    public sealed class ClassDefinition
    {
        public string Id;
        public string Name;
        public string Description;
        public UnityEngine.Color Color;
        public List<SkillNode> Skills = new List<SkillNode>();
        public List<QuestDefinition> Quests = new List<QuestDefinition>();
    }

    /// <summary>
    /// Ability ids that the game code understands. Skill trees unlock them;
    /// different classes may reach the same ability through different nodes.
    /// </summary>
    public static class Abilities
    {
        public const string Mana = "mana";
        public const string Dash = "dash";
        public const string DoubleJump = "double_jump";
        public const string Invisibility = "invisibility";
        public const string Grapple = "grapple";
        public const string Fireball = "fireball";
        public const string Toughness = "toughness";   // +damage
        public const string ManaFlow = "mana_flow";    // more mana, faster regen
    }

    /// <summary>
    /// PLACEHOLDER classes, skill trees and quests, so the systems can be
    /// tested end to end. The real classes, trees and quests replace this
    /// table; the code around it (unlocking, costs, prerequisites, quest
    /// progress) stays the same.
    /// </summary>
    public static class ClassDefinitions
    {
        public static readonly List<ClassDefinition> All = new List<ClassDefinition>
        {
            new ClassDefinition
            {
                Id = "warrior", Name = "Warrior", Color = new UnityEngine.Color(0.85f, 0.35f, 0.25f),
                Description = "Close combat. Tough, fast to engage, hooks onto cliffs.",
                Skills =
                {
                    Node(Abilities.Toughness, "Toughness", "+25% melee damage.", 1, 0, 0),
                    Node(Abilities.Dash, "Charge", "Dash forward (F).", 1, 1, 0),
                    Node(Abilities.DoubleJump, "Leap", "Jump again in mid-air.", 1, 1, 1, Abilities.Dash),
                    Node(Abilities.Grapple, "Grappling Hook", "Pull yourself to a block (G).", 2, 2, 1, Abilities.DoubleJump),
                    Node(Abilities.Mana, "Battle Focus", "Unlocks mana (50).", 1, 0, 1, Abilities.Toughness),
                },
                Quests =
                {
                    Quest("w_zombies", "Clear the Dead", "Defeat 3 zombies.", QuestGoal.Kill, "Zombie", 3, 60),
                    Quest("w_stone", "Quarry Work", "Mine 20 stone.", QuestGoal.Mine, "Stone", 20, 40),
                }
            },
            new ClassDefinition
            {
                Id = "mage", Name = "Mage", Color = new UnityEngine.Color(0.35f, 0.5f, 0.95f),
                Description = "Mana and spells. Fragile, but hits from range.",
                Skills =
                {
                    Node(Abilities.Mana, "Arcane Well", "Unlocks mana (50).", 1, 0, 0),
                    Node(Abilities.Fireball, "Fireball", "Ranged fire spell (R), 20 mana.", 1, 0, 1, Abilities.Mana),
                    Node(Abilities.ManaFlow, "Mana Flow", "+50 mana, faster regeneration.", 1, 1, 1, Abilities.Mana),
                    Node(Abilities.Invisibility, "Veil", "Become invisible for 8 s (V), 30 mana.", 2, 1, 2, Abilities.ManaFlow),
                    Node(Abilities.DoubleJump, "Levitate", "Jump again in mid-air.", 1, 2, 0),
                },
                Quests =
                {
                    Quest("m_zombies", "Burn the Dead", "Defeat 3 zombies.", QuestGoal.Kill, "Zombie", 3, 60),
                    Quest("m_crystal", "Crystal Study", "Mine 2 crystal ore.", QuestGoal.Mine, "CrystalOre", 2, 80),
                }
            },
            new ClassDefinition
            {
                Id = "rogue", Name = "Rogue", Color = new UnityEngine.Color(0.35f, 0.75f, 0.45f),
                Description = "Mobility and stealth. Strikes and disappears.",
                Skills =
                {
                    Node(Abilities.Dash, "Shadow Step", "Dash forward (F).", 1, 0, 0),
                    Node(Abilities.DoubleJump, "Acrobatics", "Jump again in mid-air.", 1, 1, 0),
                    Node(Abilities.Mana, "Shadow Energy", "Unlocks mana (50).", 1, 0, 1, Abilities.Dash),
                    Node(Abilities.Invisibility, "Vanish", "Become invisible for 8 s (V), 30 mana.", 1, 0, 2, Abilities.Mana),
                    Node(Abilities.Grapple, "Grappling Hook", "Pull yourself to a block (G).", 1, 1, 1, Abilities.DoubleJump),
                },
                Quests =
                {
                    Quest("r_hunt", "Hunter", "Hunt 3 animals.", QuestGoal.Kill, "Animal", 3, 40),
                    Quest("r_zombies", "Silent Blade", "Defeat 2 zombies.", QuestGoal.Kill, "Zombie", 2, 50),
                }
            },
        };

        public static ClassDefinition Get(string id)
        {
            foreach (ClassDefinition definition in All)
                if (definition.Id == id)
                    return definition;
            return null;
        }

        private static SkillNode Node(string id, string name, string description, int cost, int column, int row, params string[] requires) =>
            new SkillNode { Id = id, Name = name, Description = description, Cost = cost, Column = column, Row = row, Requires = requires };

        private static QuestDefinition Quest(string id, string name, string description, QuestGoal goal, string target, int amount, int xp) =>
            new QuestDefinition { Id = id, Name = name, Description = description, Goal = goal, Target = target, Amount = amount, RewardExperience = xp };
    }
}
