namespace Gameplay
{
    /// <summary>
    /// A player's vital stats and progress, owned by the server and sent
    /// to that player when anything changes (HUD, skill tree, quest log).
    /// </summary>
    public struct PlayerStatus
    {
        public float Health;
        public float MaxHealth;
        public bool Dead;
        public float RespawnSeconds;

        public float Mana;
        public float MaxMana;          // 0 until the class unlocks mana

        public int Level;
        public int Experience;         // towards the next level
        public int ExperienceToNext;
        public int SkillPoints;

        public string ClassId;         // empty = not chosen yet
        public string UnlockedSkills;  // comma separated skill ids
        public bool Invisible;

        /// <summary>Quest progress, "questId:progress:state;..." (see Quests).</summary>
        public string QuestState;

        public bool HasSkill(string skillId)
        {
            if (string.IsNullOrEmpty(UnlockedSkills) || string.IsNullOrEmpty(skillId))
                return false;

            foreach (string id in UnlockedSkills.Split(','))
                if (id == skillId)
                    return true;
            return false;
        }
    }
}
