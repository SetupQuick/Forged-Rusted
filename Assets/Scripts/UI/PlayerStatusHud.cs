using Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameUI
{
    /// <summary>
    /// Health, mana and experience bars above the hotbar, the ability bar
    /// (keys and what is unlocked), the death screen, and messages for
    /// level-ups and finished quests. Reads the PlayerStatus the server
    /// sends; created by GameHud.
    /// </summary>
    public sealed class PlayerStatusHud : MonoBehaviour
    {
        private const float BarWidth = 300f;

        private Image _healthFill, _manaFill, _xpFill, _manaBack;
        private TextMeshProUGUI _healthText, _manaText, _levelText, _hintText;
        private GameObject _deathScreen;
        private TextMeshProUGUI _deathText;
        private Image _invisibleTint;
        private readonly (string Skill, string Key, string Label, Image Back, TextMeshProUGUI Text)[] _abilities =
        {
            (Abilities.Dash, "F", "Dash", null, null),
            (Abilities.DoubleJump, "Space", "2x Jump", null, null),
            (Abilities.Grapple, "G", "Hook", null, null),
            (Abilities.Fireball, "R", "Fireball", null, null),
            (Abilities.Invisibility, "V", "Invisible", null, null),
        };

        private PlayerStatus _last;
        private bool _hasLast;

        public void Build(RectTransform root)
        {
            // Bars just above the hotbar (which is 80 px tall at y = 20).
            _healthFill = Bar(root, "Health", new Vector2(-BarWidth * 0.5f - 6f, 106f), new Color(0.85f, 0.22f, 0.2f), out _healthText, out _);
            _manaFill = Bar(root, "Mana", new Vector2(BarWidth * 0.5f + 6f, 106f), new Color(0.25f, 0.45f, 0.95f), out _manaText, out _manaBack);

            Image xpBack = UiBuilder.Image("Experience", root, new Color(0f, 0f, 0f, 0.55f));
            UiBuilder.Place(xpBack.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 98f), new Vector2(BarWidth * 2f + 12f, 5f));
            _xpFill = UiBuilder.Image("Fill", xpBack.rectTransform, new Color(0.45f, 0.9f, 0.35f));
            UiBuilder.Place(_xpFill.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(0f, 5f));

            _levelText = UiBuilder.Text("Level", root, 18, TextAlignmentOptions.Center, UiBuilder.AccentColor);
            UiBuilder.Place(_levelText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 124f), new Vector2(300f, 24f));
            UiBuilder.AddOutline(_levelText, new Color(0f, 0f, 0f, 0.7f), 1.5f);

            // Ability bar, bottom left.
            for (int i = 0; i < _abilities.Length; i++)
            {
                Image back = UiBuilder.Image("Ability " + _abilities[i].Label, root, new Color(0f, 0f, 0f, 0.5f));
                UiBuilder.Place(back.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(16f + i * 76f, 16f), new Vector2(70f, 46f));
                TextMeshProUGUI text = UiBuilder.Text("Text", back.rectTransform, 14, TextAlignmentOptions.Center, UiBuilder.TextColor);
                UiBuilder.Stretch(text.rectTransform);
                text.text = $"<b>{_abilities[i].Key}</b>\n{_abilities[i].Label}";
                _abilities[i].Back = back;
                _abilities[i].Text = text;
            }

            _hintText = UiBuilder.Text("Hint", root, 16, TextAlignmentOptions.BottomLeft, new Color(1f, 1f, 1f, 0.75f));
            UiBuilder.Place(_hintText.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(16f, 68f), new Vector2(520f, 40f));
            UiBuilder.AddOutline(_hintText, new Color(0f, 0f, 0f, 0.7f), 1.5f);

            _invisibleTint = UiBuilder.Image("Invisible", root, new Color(0.35f, 0.3f, 0.6f, 0.18f));
            UiBuilder.Stretch(_invisibleTint.rectTransform);
            _invisibleTint.enabled = false;

            // Death screen.
            Image death = UiBuilder.Image("Death Screen", root, new Color(0.45f, 0f, 0f, 0.45f));
            UiBuilder.Stretch(death.rectTransform);
            _deathText = UiBuilder.Text("Text", death.rectTransform, 48, TextAlignmentOptions.Center, UiBuilder.TextColor);
            UiBuilder.Stretch(_deathText.rectTransform);
            UiBuilder.AddOutline(_deathText, new Color(0f, 0f, 0f, 0.8f), 3f);
            _deathScreen = death.gameObject;
            _deathScreen.SetActive(false);
        }

        private static Image Bar(RectTransform root, string name, Vector2 position, Color color, out TextMeshProUGUI text, out Image back)
        {
            back = UiBuilder.Image(name, root, new Color(0f, 0f, 0f, 0.55f));
            UiBuilder.Place(back.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), position, new Vector2(BarWidth, 14f));

            Image fill = UiBuilder.Image("Fill", back.rectTransform, color);
            UiBuilder.Place(fill.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(1f, 0f), new Vector2(BarWidth - 2f, 12f));

            text = UiBuilder.Text("Value", back.rectTransform, 12, TextAlignmentOptions.Center, UiBuilder.TextColor);
            UiBuilder.Stretch(text.rectTransform);
            UiBuilder.AddOutline(text, new Color(0f, 0f, 0f, 0.8f), 1f);
            return fill;
        }

        private void Update()
        {
            bool known = PlayerAbilities.HasStatus;
            PlayerStatus status = PlayerAbilities.Status;

            SetFill(_healthFill, status.MaxHealth > 0f ? status.Health / status.MaxHealth : 0f);
            _healthText.text = $"{Mathf.CeilToInt(status.Health)} / {Mathf.RoundToInt(status.MaxHealth)}";

            bool hasMana = status.MaxMana > 0f;
            _manaBack.gameObject.SetActive(hasMana);
            if (hasMana)
            {
                SetFill(_manaFill, status.Mana / status.MaxMana);
                _manaText.text = $"{Mathf.FloorToInt(status.Mana)} / {Mathf.RoundToInt(status.MaxMana)}";
            }

            _xpFill.rectTransform.sizeDelta = new Vector2((BarWidth * 2f + 12f) * (status.ExperienceToNext > 0 ? (float)status.Experience / status.ExperienceToNext : 0f), 5f);

            ClassDefinition playerClass = ClassDefinitions.Get(status.ClassId);
            _levelText.text = (playerClass != null ? playerClass.Name + "  " : string.Empty) + $"Level {status.Level}" +
                              (status.SkillPoints > 0 ? $"  <color=#9CF26B>+{status.SkillPoints} skill point{(status.SkillPoints > 1 ? "s" : "")} (K)</color>" : string.Empty);

            for (int i = 0; i < _abilities.Length; i++)
            {
                bool unlocked = status.HasSkill(_abilities[i].Skill);
                bool active = _abilities[i].Skill == Abilities.Invisibility && status.Invisible;
                _abilities[i].Back.color = active ? new Color(0.45f, 0.35f, 0.85f, 0.75f) : unlocked ? new Color(0.15f, 0.35f, 0.18f, 0.7f) : new Color(0f, 0f, 0f, 0.35f);
                _abilities[i].Text.alpha = unlocked ? 1f : 0.4f;
            }

            _hintText.text = known && string.IsNullOrEmpty(status.ClassId)
                ? "Press <b>K</b> to choose a class.   <b>J</b> quest log"
                : "<b>K</b> skills   <b>J</b> quests";

            _invisibleTint.enabled = status.Invisible;

            _deathScreen.SetActive(status.Dead);
            if (status.Dead)
                _deathText.text = $"You died\n<size=28>Respawning in {Mathf.CeilToInt(status.RespawnSeconds)}...</size>";

            if (known)
                Announce(status);
        }

        /// <summary>Level-ups and finished quests.</summary>
        private void Announce(PlayerStatus status)
        {
            if (_hasLast && GameHud.Instance != null)
            {
                if (status.Level > _last.Level)
                    GameHud.Instance.ShowMessage($"Level up! You are level {status.Level} - new skill point (K)", 4f);

                foreach (QuestEntry quest in QuestEntry.Parse(status))
                {
                    if (!quest.Done)
                        continue;

                    bool wasDone = false;
                    foreach (QuestEntry old in QuestEntry.Parse(_last))
                        if (old.Definition == quest.Definition && old.Done)
                            wasDone = true;

                    if (!wasDone && _last.ClassId == status.ClassId)
                        GameHud.Instance.ShowMessage($"Quest complete: {quest.Definition.Name} (+{quest.Definition.RewardExperience} XP)", 4f);
                }

                if (status.Health < _last.Health && !status.Dead)
                    GameHud.Instance.FlashDamage();
            }

            _last = status;
            _hasLast = true;
        }

        private static void SetFill(Image fill, float amount)
        {
            fill.rectTransform.sizeDelta = new Vector2((BarWidth - 2f) * Mathf.Clamp01(amount), 12f);
        }
    }

    /// <summary>One quest of the player's class with its progress (from PlayerStatus.QuestState).</summary>
    public struct QuestEntry
    {
        public QuestDefinition Definition;
        public int Progress;
        public bool Done;

        public static System.Collections.Generic.List<QuestEntry> Parse(PlayerStatus status)
        {
            var result = new System.Collections.Generic.List<QuestEntry>();
            ClassDefinition definition = ClassDefinitions.Get(status.ClassId);
            if (definition == null)
                return result;

            foreach (QuestDefinition quest in definition.Quests)
            {
                var entry = new QuestEntry { Definition = quest };

                if (!string.IsNullOrEmpty(status.QuestState))
                {
                    foreach (string part in status.QuestState.Split(';'))
                    {
                        string[] fields = part.Split(':');
                        if (fields.Length == 3 && fields[0] == quest.Id)
                        {
                            int.TryParse(fields[1], out entry.Progress);
                            entry.Done = fields[2] == "1";
                        }
                    }
                }

                result.Add(entry);
            }

            return result;
        }
    }
}
