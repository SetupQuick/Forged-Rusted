using System.Collections.Generic;
using Gameplay;
using Items;
using Networking;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GameUI
{
    /// <summary>
    /// K - skill tree (or the class picker while no class is chosen),
    /// J - quest log. Everything shown comes from the PlayerStatus the
    /// server sends; buttons send actions the server validates (points,
    /// prerequisites, class chosen only once). Created by GameHud.
    /// The classes, trees and quests are placeholders from
    /// Gameplay.ClassDefinitions.
    /// </summary>
    public sealed class CharacterScreens : MonoBehaviour
    {
        private enum Screen { None, Skills, Quests }

        private PlayerInventory _inventory;
        private RectTransform _root;
        private GameObject _window;
        private RectTransform _content;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _details;
        private Screen _open = Screen.None;
        private string _builtFor = string.Empty;
        private int _selected = -1;

        public static bool EscapeHandledThisFrame => _escapeFrame == Time.frameCount;
        private static int _escapeFrame = -1;

        public bool IsOpen => _open != Screen.None;

        public void Build(RectTransform root, PlayerInventory inventory)
        {
            _root = root;
            _inventory = inventory;

            Image window = UiBuilder.Image("Character Window", root, UiBuilder.PanelColor, raycast: true);
            UiBuilder.Place(window.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(980f, 640f));
            _window = window.gameObject;

            _title = UiBuilder.Text("Title", window.rectTransform, 30, TextAlignmentOptions.Center, UiBuilder.AccentColor);
            UiBuilder.Place(_title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(900f, 44f));

            _content = UiBuilder.Rect("Content", window.rectTransform);
            UiBuilder.Place(_content, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -76f), new Vector2(920f, 430f));

            _details = UiBuilder.Text("Details", window.rectTransform, 18, TextAlignmentOptions.TopLeft, UiBuilder.TextColor);
            UiBuilder.Place(_details.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(920f, 110f));

            _window.SetActive(false);
        }

        private void OnDestroy()
        {
            PlayerInputReader.SetGameplayInputBlocked(this, false);
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            bool inMenus = Game.GameFlow.Instance != null && Game.GameFlow.Instance.InMenus;
            bool inventoryOpen = GameHud.Instance != null && GameHud.Instance.Inventory != null && GameHud.Instance.Inventory.IsOpen;

            if (inMenus && IsOpen)
                SetOpen(Screen.None);

            if (keyboard != null && !inMenus && !inventoryOpen)
            {
                if (keyboard.kKey.wasPressedThisFrame)
                    SetOpen(_open == Screen.Skills ? Screen.None : Screen.Skills);
                else if (keyboard.jKey.wasPressedThisFrame)
                    SetOpen(_open == Screen.Quests ? Screen.None : Screen.Quests);
                else if (IsOpen && keyboard.escapeKey.wasPressedThisFrame)
                {
                    _escapeFrame = Time.frameCount;
                    SetOpen(Screen.None);
                }
            }

            if (!IsOpen)
                return;

            // Rebuild when the status changed (points spent, class chosen, quest progress).
            PlayerStatus status = PlayerAbilities.Status;
            string key = _open + "|" + status.ClassId + "|" + status.UnlockedSkills + "|" + status.SkillPoints + "|" + status.Level + "|" + status.QuestState + "|" + _selected;
            if (key != _builtFor)
            {
                _builtFor = key;
                Rebuild(status);
            }
        }

        private void SetOpen(Screen screen)
        {
            _open = screen;
            _selected = -1;
            _builtFor = string.Empty;
            _window.SetActive(screen != Screen.None);
            PlayerInputReader.SetGameplayInputBlocked(this, screen != Screen.None);
        }

        private void Clear()
        {
            for (int i = _content.childCount - 1; i >= 0; i--)
                Destroy(_content.GetChild(i).gameObject);
        }

        private void Rebuild(PlayerStatus status)
        {
            Clear();
            _details.text = string.Empty;

            if (_open == Screen.Quests)
                BuildQuests(status);
            else if (string.IsNullOrEmpty(status.ClassId))
                BuildClassPicker();
            else
                BuildSkillTree(status);
        }

        // ------------------------------------------------------------
        // Class picker
        // ------------------------------------------------------------

        private void BuildClassPicker()
        {
            _title.text = "Choose your class";
            _details.text = "Your class decides your skill tree and quests. It is chosen once. (Placeholder classes - the real ones come later.)";

            List<ClassDefinition> classes = ClassDefinitions.All;
            float width = 280f;

            for (int i = 0; i < classes.Count; i++)
            {
                int index = i;
                ClassDefinition definition = classes[i];

                Image card = UiBuilder.Image(definition.Name, _content, new Color(0.14f, 0.15f, 0.18f, 0.95f), raycast: true);
                UiBuilder.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2((i - (classes.Count - 1) * 0.5f) * (width + 24f), 0f), new Vector2(width, 380f));

                Image stripe = UiBuilder.Image("Colour", card.rectTransform, definition.Color);
                UiBuilder.Place(stripe.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(width, 8f));

                TextMeshProUGUI name = UiBuilder.Text("Name", card.rectTransform, 28, TextAlignmentOptions.Center, UiBuilder.TextColor);
                UiBuilder.Place(name.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(width - 20f, 40f));
                name.text = definition.Name;

                TextMeshProUGUI text = UiBuilder.Text("Description", card.rectTransform, 17, TextAlignmentOptions.TopLeft, UiBuilder.SubtleTextColor);
                UiBuilder.Place(text.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -76f), new Vector2(width - 32f, 220f));
                var skills = new List<string>();
                foreach (SkillNode node in definition.Skills)
                    skills.Add(node.Name);
                text.text = definition.Description + "\n\n<color=#E8C05A>Skills:</color> " + string.Join(", ", skills);

                RectTransform buttonHolder = UiBuilder.Rect("Button", card.rectTransform);
                UiBuilder.Place(buttonHolder, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(width - 40f, 50f));
                var layout = buttonHolder.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                MenuWidgets.Button(buttonHolder, "Choose " + definition.Name, () => _inventory.ChooseClass(index), primary: true, height: 48f);
            }
        }

        // ------------------------------------------------------------
        // Skill tree
        // ------------------------------------------------------------

        private void BuildSkillTree(PlayerStatus status)
        {
            ClassDefinition definition = ClassDefinitions.Get(status.ClassId);
            _title.text = $"{definition.Name} skills - {status.SkillPoints} point{(status.SkillPoints == 1 ? "" : "s")} to spend";

            const float cellW = 250f, cellH = 120f;
            var positions = new Dictionary<string, Vector2>();
            for (int i = 0; i < definition.Skills.Count; i++)
            {
                SkillNode node = definition.Skills[i];
                positions[node.Id] = new Vector2((node.Column - 1) * (cellW + 40f), 150f - node.Row * (cellH + 40f));
            }

            // Lines for prerequisites, under the nodes.
            foreach (SkillNode node in definition.Skills)
                foreach (string required in node.Requires)
                    if (positions.TryGetValue(required, out Vector2 from))
                        Line(from, positions[node.Id], status.HasSkill(required));

            for (int i = 0; i < definition.Skills.Count; i++)
            {
                int index = i;
                SkillNode node = definition.Skills[i];
                bool owned = status.HasSkill(node.Id);
                bool available = CanUnlock(status, definition, node, out _);

                Color color = owned ? new Color(0.2f, 0.45f, 0.24f, 0.95f)
                    : available ? new Color(0.45f, 0.36f, 0.12f, 0.95f)
                    : new Color(0.16f, 0.17f, 0.2f, 0.95f);

                Image box = UiBuilder.Image(node.Name, _content, color, raycast: true);
                UiBuilder.Place(box.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), positions[node.Id], new Vector2(cellW, cellH * 0.7f));
                if (index == _selected)
                    UiBuilder.AddOutline(box, UiBuilder.AccentColor, 3f);

                TextMeshProUGUI text = UiBuilder.Text("Text", box.rectTransform, 18, TextAlignmentOptions.Center, UiBuilder.TextColor);
                UiBuilder.Stretch(text.rectTransform);
                text.text = $"<b>{node.Name}</b>\n<size=14>{(owned ? "Unlocked" : $"Cost {node.Cost}")}</size>";

                Button button = box.gameObject.AddComponent<Button>();
                button.onClick.AddListener(() =>
                {
                    _selected = index;
                    if (CanUnlock(PlayerAbilities.Status, definition, node, out _))
                        _inventory.UnlockSkill(index);
                });
            }

            if (_selected >= 0 && _selected < definition.Skills.Count)
            {
                SkillNode node = definition.Skills[_selected];
                CanUnlock(status, definition, node, out string reason);
                _details.text = $"<b>{node.Name}</b> - {node.Description}\n" +
                                (status.HasSkill(node.Id) ? "<color=#8BE07A>Unlocked.</color>" : reason == null ? "Click to unlock." : $"<color=#E08B7A>{reason}</color>");
            }
            else
            {
                _details.text = "Click a skill to unlock it. Gold = can unlock now, green = unlocked. Skill points: 2 to start, +1 per level (XP from creatures and quests).\n" +
                                "Keys: F dash, Space in mid-air double jump, G grappling hook, R fireball, V invisibility.";
            }
        }

        private static bool CanUnlock(PlayerStatus status, ClassDefinition definition, SkillNode node, out string reason)
        {
            reason = null;
            if (status.HasSkill(node.Id)) reason = "Already unlocked.";
            else if (status.SkillPoints < node.Cost) reason = $"Needs {node.Cost} skill point(s).";
            else
            {
                foreach (string required in node.Requires)
                {
                    if (!status.HasSkill(required))
                    {
                        string name = required;
                        foreach (SkillNode other in definition.Skills)
                            if (other.Id == required) name = other.Name;
                        reason = $"Needs {name} first.";
                        break;
                    }
                }
            }

            return reason == null;
        }

        private void Line(Vector2 from, Vector2 to, bool active)
        {
            Image line = UiBuilder.Image("Line", _content, active ? new Color(0.5f, 0.85f, 0.45f, 0.9f) : new Color(1f, 1f, 1f, 0.25f));
            Vector2 delta = to - from;
            UiBuilder.Place(line.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), (from + to) * 0.5f, new Vector2(delta.magnitude, 4f));
            line.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }

        // ------------------------------------------------------------
        // Quest log
        // ------------------------------------------------------------

        private void BuildQuests(PlayerStatus status)
        {
            _title.text = "Quests";

            List<QuestEntry> quests = QuestEntry.Parse(status);
            if (quests.Count == 0)
            {
                TextMeshProUGUI empty = UiBuilder.Text("Empty", _content, 22, TextAlignmentOptions.Center, UiBuilder.SubtleTextColor);
                UiBuilder.Stretch(empty.rectTransform);
                empty.text = "Choose a class (K) to get its quests.";
                return;
            }

            for (int i = 0; i < quests.Count; i++)
            {
                QuestEntry quest = quests[i];
                Image row = UiBuilder.Image(quest.Definition.Name, _content, new Color(0.14f, 0.15f, 0.18f, 0.95f));
                UiBuilder.Place(row.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -i * 96f), new Vector2(900f, 84f));

                TextMeshProUGUI text = UiBuilder.Text("Text", row.rectTransform, 19, TextAlignmentOptions.Left, UiBuilder.TextColor);
                UiBuilder.Place(text.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(18f, 0f), new Vector2(620f, 80f));
                text.text = $"<b>{quest.Definition.Name}</b>  <size=15><color=#B8BCC4>{quest.Definition.Description}</color></size>\n" +
                            $"<size=15>Reward: {quest.Definition.RewardExperience} XP</size>";

                Image back = UiBuilder.Image("Progress", row.rectTransform, new Color(0f, 0f, 0f, 0.5f));
                UiBuilder.Place(back.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-18f, 0f), new Vector2(220f, 22f));
                Image fill = UiBuilder.Image("Fill", back.rectTransform, quest.Done ? new Color(0.4f, 0.8f, 0.35f) : UiBuilder.AccentColor);
                UiBuilder.Place(fill.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(220f * Mathf.Clamp01((float)quest.Progress / quest.Definition.Amount), 22f));
                TextMeshProUGUI count = UiBuilder.Text("Count", back.rectTransform, 15, TextAlignmentOptions.Center, UiBuilder.TextColor);
                UiBuilder.Stretch(count.rectTransform);
                count.text = quest.Done ? "Complete" : $"{quest.Progress} / {quest.Definition.Amount}";
            }

            _details.text = "Quests advance by themselves while you play and give XP when complete. (Placeholder quests.)";
        }
    }
}
