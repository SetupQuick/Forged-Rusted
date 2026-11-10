using System;
using System.Collections.Generic;
using System.Text;
using Gameplay.Entities;
using World.Blocks;

namespace Gameplay
{
    /// <summary>
    /// A player's class, level, experience, skills, mana and quests - the
    /// server's copy (authoritative). Pure code: the server module calls it,
    /// tests call it directly. Saved with the world.
    /// </summary>
    public sealed class PlayerProgress
    {
        public const int StartingSkillPoints = 2;
        public const float BaseMana = 50f;
        public const float ManaFlowBonus = 50f;
        public const float InvisibilitySeconds = 8f;
        public const float InvisibilityCost = 30f;
        public const float InvisibilityCooldown = 20f;
        public const float FireballCost = 20f;
        public const float FireballCooldown = 1f;
        public const float FireballDamage = 8f;
        public const float FireballRange = 30f;

        private readonly HashSet<string> _skills = new HashSet<string>();
        private readonly Dictionary<string, int> _questProgress = new Dictionary<string, int>();
        private readonly HashSet<string> _questsDone = new HashSet<string>();
        private readonly Dictionary<string, float> _cooldowns = new Dictionary<string, float>();

        public string ClassId { get; private set; } = string.Empty;
        public int Level { get; private set; } = 1;
        public int Experience { get; private set; }
        public int SkillPoints { get; private set; } = StartingSkillPoints;
        public float Mana { get; private set; }
        public float InvisibleTimer { get; private set; }

        public bool Invisible => InvisibleTimer > 0f;
        public ClassDefinition Class => ClassDefinitions.Get(ClassId);
        public IEnumerable<string> Skills => _skills;

        public static int ExperienceForLevel(int level) => 50 * level;
        public int ExperienceToNext => ExperienceForLevel(Level);

        public bool Has(string skill) => _skills.Contains(skill);

        public float MaxMana =>
            !Has(Abilities.Mana) ? 0f : BaseMana + (Has(Abilities.ManaFlow) ? ManaFlowBonus : 0f);

        public float DamageMultiplier => Has(Abilities.Toughness) ? 1.25f : 1f;

        // ------------------------------------------------------------
        // Class and skills
        // ------------------------------------------------------------

        /// <summary>Picks the class (only once).</summary>
        public bool ChooseClass(string classId)
        {
            if (!string.IsNullOrEmpty(ClassId) || ClassDefinitions.Get(classId) == null)
                return false;

            ClassId = classId;
            return true;
        }

        public bool CanUnlock(string skillId, out string reason)
        {
            reason = null;
            SkillNode node = FindNode(skillId);

            if (node == null) reason = "Not in your class's tree.";
            else if (Has(skillId)) reason = "Already unlocked.";
            else if (SkillPoints < node.Cost) reason = $"Needs {node.Cost} skill point(s).";
            else
            {
                foreach (string required in node.Requires)
                {
                    if (!Has(required))
                    {
                        reason = $"Needs {FindNode(required)?.Name ?? required} first.";
                        break;
                    }
                }
            }

            return reason == null;
        }

        public bool Unlock(string skillId)
        {
            if (!CanUnlock(skillId, out _))
                return false;

            SkillPoints -= FindNode(skillId).Cost;
            _skills.Add(skillId);

            if (skillId == Abilities.Mana || skillId == Abilities.ManaFlow)
                Mana = MaxMana;

            return true;
        }

        private SkillNode FindNode(string skillId)
        {
            ClassDefinition definition = Class;
            if (definition == null)
                return null;

            foreach (SkillNode node in definition.Skills)
                if (node.Id == skillId)
                    return node;
            return null;
        }

        // ------------------------------------------------------------
        // Experience and quests
        // ------------------------------------------------------------

        /// <summary>Adds XP; levels up (skill point, full mana) as often as it fills.</summary>
        public int AddExperience(int amount, ref bool dirty)
        {
            if (amount <= 0)
                return 0;

            Experience += amount;
            int levels = 0;

            while (Experience >= ExperienceToNext)
            {
                Experience -= ExperienceToNext;
                Level++;
                SkillPoints++;
                levels++;
            }

            if (levels > 0)
                Mana = MaxMana;

            dirty = true;
            return levels;
        }

        public void OnKilled(EntityKind kind, ref bool dirty)
        {
            string name = kind.ToString();
            bool animal = kind == EntityKind.Pig || kind == EntityKind.Chicken;
            Count(QuestGoal.Kill, t => t == name || (animal && t == "Animal"), ref dirty);
        }

        public void OnMined(BlockId block, ref bool dirty)
        {
            string name = block.ToString();
            Count(QuestGoal.Mine, t => t == name, ref dirty);
        }

        private void Count(QuestGoal goal, Func<string, bool> matches, ref bool dirty)
        {
            ClassDefinition definition = Class;
            if (definition == null)
                return;

            foreach (QuestDefinition quest in definition.Quests)
            {
                if (quest.Goal != goal || _questsDone.Contains(quest.Id) || !matches(quest.Target))
                    continue;

                _questProgress.TryGetValue(quest.Id, out int progress);
                progress++;
                _questProgress[quest.Id] = progress;
                dirty = true;

                if (progress >= quest.Amount)
                {
                    _questsDone.Add(quest.Id);
                    AddExperience(quest.RewardExperience, ref dirty);
                }
            }
        }

        public int QuestProgress(string questId) =>
            _questProgress.TryGetValue(questId, out int progress) ? progress : 0;

        public bool QuestDone(string questId) => _questsDone.Contains(questId);

        // ------------------------------------------------------------
        // Mana, cooldowns, invisibility
        // ------------------------------------------------------------

        public void Tick(float dt, ref bool dirty)
        {
            float max = MaxMana;
            if (Mana < max)
            {
                float before = Mana;
                Mana = Math.Min(max, Mana + (Has(Abilities.ManaFlow) ? 4f : 2f) * dt);

                // Tell the client in whole points, not every tick.
                if ((int)Mana != (int)before)
                    dirty = true;
            }

            if (InvisibleTimer > 0f)
            {
                InvisibleTimer = Math.Max(0f, InvisibleTimer - dt);
                if (InvisibleTimer == 0f)
                    dirty = true;
            }

            if (_cooldowns.Count > 0)
            {
                var keys = new List<string>(_cooldowns.Keys);
                foreach (string key in keys)
                {
                    _cooldowns[key] -= dt;
                    if (_cooldowns[key] <= 0f)
                        _cooldowns.Remove(key);
                }
            }
        }

        public float Cooldown(string ability) => _cooldowns.TryGetValue(ability, out float left) ? left : 0f;

        /// <summary>Checks skill, cooldown and mana, then spends them. False = not possible now.</summary>
        public bool TryUse(string ability, float manaCost, float cooldown, ref bool dirty)
        {
            if (!Has(ability) || Cooldown(ability) > 0f || Mana < manaCost)
                return false;

            Mana -= manaCost;
            if (cooldown > 0f)
                _cooldowns[ability] = cooldown;

            if (ability == Abilities.Invisibility)
                InvisibleTimer = InvisibilitySeconds;

            dirty = true;
            return true;
        }

        /// <summary>Attacking ends invisibility.</summary>
        public void BreakInvisibility(ref bool dirty)
        {
            if (InvisibleTimer > 0f)
            {
                InvisibleTimer = 0f;
                dirty = true;
            }
        }

        // ------------------------------------------------------------
        // Status and saving
        // ------------------------------------------------------------

        public PlayerStatus ToStatus()
        {
            var quests = new StringBuilder();
            ClassDefinition definition = Class;
            if (definition != null)
            {
                foreach (QuestDefinition quest in definition.Quests)
                {
                    if (quests.Length > 0) quests.Append(';');
                    quests.Append(quest.Id).Append(':').Append(Math.Min(QuestProgress(quest.Id), quest.Amount)).Append(':').Append(QuestDone(quest.Id) ? 1 : 0);
                }
            }

            return new PlayerStatus
            {
                Mana = Mana,
                MaxMana = MaxMana,
                Level = Level,
                Experience = Experience,
                ExperienceToNext = ExperienceToNext,
                SkillPoints = SkillPoints,
                ClassId = ClassId,
                UnlockedSkills = string.Join(",", _skills),
                Invisible = Invisible,
                QuestState = quests.ToString()
            };
        }

        /// <summary>Compact text for saves: class|level|xp|points|skills|questId=progress,...|done,...</summary>
        public string Serialize()
        {
            var progress = new List<string>();
            foreach (KeyValuePair<string, int> quest in _questProgress)
                progress.Add(quest.Key + "=" + quest.Value);

            return string.Join("|", ClassId, Level, Experience, SkillPoints,
                string.Join(",", _skills), string.Join(",", progress), string.Join(",", _questsDone));
        }

        public static PlayerProgress Deserialize(string text)
        {
            var progress = new PlayerProgress();
            if (string.IsNullOrEmpty(text))
                return progress;

            string[] parts = text.Split('|');
            if (parts.Length < 7)
                return progress;

            progress.ClassId = ClassDefinitions.Get(parts[0]) != null ? parts[0] : string.Empty;
            int.TryParse(parts[1], out int level);
            int.TryParse(parts[2], out int xp);
            int.TryParse(parts[3], out int points);
            progress.Level = Math.Max(1, level);
            progress.Experience = Math.Max(0, xp);
            progress.SkillPoints = Math.Max(0, points);

            foreach (string skill in parts[4].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                progress._skills.Add(skill);

            foreach (string entry in parts[5].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] kv = entry.Split('=');
                if (kv.Length == 2 && int.TryParse(kv[1], out int value))
                    progress._questProgress[kv[0]] = value;
            }

            foreach (string done in parts[6].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                progress._questsDone.Add(done);

            progress.Mana = progress.MaxMana;
            return progress;
        }
    }
}
