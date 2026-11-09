using System.Collections.Generic;
using Items;
using Networking;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GameUI
{
    /// <summary>
    /// The inventory screen (Tab or I to open, Esc to close).
    ///
    ///   left click        pick up / put down / swap / merge a stack
    ///   right click       pick up half / put down one
    ///   shift + click     move between hotbar and backpack
    ///
    /// While open, the player stops moving and the cursor is free.
    /// Clicks go through <see cref="PlayerInventory"/>, which predicts them
    /// and lets the server confirm them. Built by <see cref="GameHud"/>;
    /// needs no scene setup.
    /// </summary>
    public class InventoryUI : MonoBehaviour
    {
        private const float SlotSize = 64f;
        private const float Spacing = 6f;
        private const int Columns = 9;

        private readonly List<ItemSlotUI> _slots = new List<ItemSlotUI>();

        private PlayerInventory _inventory;
        private RectTransform _canvasRect;
        private GameObject _root;
        private Image _heldIcon;
        private TextMeshProUGUI _heldCount;
        private RectTransform _tooltip;
        private TextMeshProUGUI _tooltipText;

        private ItemStack Held => _inventory != null ? _inventory.Held : ItemStack.Empty;

        public bool IsOpen => _root != null && _root.activeSelf;

        private static int _escapeFrame = -1;

        /// <summary>True in the frame the inventory used Esc to close itself.</summary>
        public static bool EscapeHandledThisFrame => _escapeFrame == Time.frameCount;

        public void Build(PlayerInventory inventory, RectTransform canvasRect)
        {
            _inventory = inventory;
            _canvasRect = canvasRect;

            // Dim the game behind the panel; clicking outside the panel
            // does nothing (the held stack stays on the cursor).
            Image dim = UiBuilder.Image("Inventory", canvasRect, new Color(0f, 0f, 0f, 0.45f), raycast: true);
            UiBuilder.Stretch(dim.rectTransform);
            _root = dim.gameObject;

            InventoryContainer container = inventory.Container;
            int backpackRows = Mathf.CeilToInt((container.Count - container.HotbarSize) / (float)Columns);

            float gridWidth = Columns * SlotSize + (Columns - 1) * Spacing;
            float backpackHeight = backpackRows * SlotSize + Mathf.Max(0, backpackRows - 1) * Spacing;
            float panelWidth = gridWidth + 48f;
            float panelHeight = 70f + backpackHeight + 30f + SlotSize + 34f;

            Image panel = UiBuilder.Image("Panel", dim.rectTransform, UiBuilder.PanelColor, raycast: true);
            UiBuilder.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(panelWidth, panelHeight));
            UiBuilder.AddOutline(panel, new Color(0f, 0f, 0f, 0.6f), 3f);

            TextMeshProUGUI title = UiBuilder.Text("Title", panel.rectTransform, 30, TextAlignmentOptions.TopLeft, UiBuilder.TextColor);
            UiBuilder.Place(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -16), new Vector2(400, 40));
            title.text = "Inventory";
            title.fontStyle = FontStyles.Bold;

            TextMeshProUGUI help = UiBuilder.Text("Help", panel.rectTransform, 16, TextAlignmentOptions.TopRight, UiBuilder.SubtleTextColor);
            UiBuilder.Place(help.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, -24), new Vector2(520, 30));
            help.text = "Click: move   Right-click: split   Shift-click: quick move";

            // Backpack grid.
            RectTransform backpack = CreateGrid("Backpack", panel.rectTransform, new Vector2(0, -70), backpackRows);

            for (int i = container.HotbarSize; i < container.Count; i++)
                AddSlot(backpack, i);

            TextMeshProUGUI hotbarLabel = UiBuilder.Text("Hotbar Label", panel.rectTransform, 16, TextAlignmentOptions.BottomLeft, UiBuilder.SubtleTextColor);
            UiBuilder.Place(hotbarLabel.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -70 - backpackHeight - 4), new Vector2(200, 24));
            hotbarLabel.text = "Hotbar";

            RectTransform hotbar = CreateGrid("Hotbar", panel.rectTransform, new Vector2(0, -70 - backpackHeight - 30), 1);

            for (int i = 0; i < container.HotbarSize; i++)
                AddSlot(hotbar, i);

            // Stack held on the cursor.
            _heldIcon = UiBuilder.Image("Held Item", canvasRect, Color.white);
            _heldIcon.rectTransform.sizeDelta = new Vector2(SlotSize * 0.8f, SlotSize * 0.8f);
            _heldIcon.preserveAspect = true;
            _heldIcon.enabled = false;

            _heldCount = UiBuilder.Text("Count", _heldIcon.rectTransform, 20, TextAlignmentOptions.BottomRight, UiBuilder.TextColor);
            UiBuilder.Stretch(_heldCount.rectTransform);
            _heldCount.fontStyle = FontStyles.Bold;
            UiBuilder.AddOutline(_heldCount, new Color(0, 0, 0, 0.8f), 1.5f);

            // Tooltip with item name and description.
            Image tooltip = UiBuilder.Image("Tooltip", canvasRect, new Color(0.05f, 0.05f, 0.07f, 0.95f));
            _tooltip = tooltip.rectTransform;
            _tooltip.pivot = new Vector2(0, 1);
            _tooltipText = UiBuilder.Text("Text", _tooltip, 18, TextAlignmentOptions.TopLeft, UiBuilder.TextColor);
            UiBuilder.Stretch(_tooltipText.rectTransform);
            _tooltipText.rectTransform.offsetMin = new Vector2(10, 8);
            _tooltipText.rectTransform.offsetMax = new Vector2(-10, -8);
            _tooltip.gameObject.SetActive(false);

            container.Changed += Refresh;
            inventory.HeldChanged += Refresh;
            _root.SetActive(false);
            Refresh();
        }

        private void OnDestroy()
        {
            // The panel may already be destroyed with us; just make sure
            // the player is not left unable to move.
            PlayerInputReader.GameplayInputBlocked = false;

            if (_inventory != null && _inventory.Container != null)
            {
                _inventory.Container.Changed -= Refresh;
                _inventory.HeldChanged -= Refresh;
            }
        }

        public void SetOpen(bool open)
        {
            if (_root == null || open == IsOpen)
                return;

            _root.SetActive(open);
            PlayerInputReader.GameplayInputBlocked = open;

            if (!open)
            {
                _inventory.CloseInventory();
                _tooltip.gameObject.SetActive(false);
            }

            Refresh();
        }

        private void Update()
        {
            if (_inventory == null)
                return;

            Keyboard keyboard = Keyboard.current;

            bool inMenus = Game.GameFlow.Instance != null && Game.GameFlow.Instance.InMenus;

            if (inMenus && IsOpen)
                SetOpen(false);

            if (keyboard != null && !inMenus)
            {
                if (keyboard.tabKey.wasPressedThisFrame || keyboard.iKey.wasPressedThisFrame)
                {
                    SetOpen(!IsOpen);
                }
                else if (IsOpen && keyboard.escapeKey.wasPressedThisFrame)
                {
                    // Esc closes the inventory, not also opens the pause menu.
                    _escapeFrame = Time.frameCount;
                    SetOpen(false);
                }
            }

            if (IsOpen)
            {
                FollowMouse(_heldIcon.rectTransform, Vector2.zero);
                FollowMouse(_tooltip, new Vector2(18, -18));
            }
        }

        private RectTransform CreateGrid(string name, RectTransform parent, Vector2 topOffset, int rows)
        {
            RectTransform grid = UiBuilder.Rect(name, parent);
            float width = Columns * SlotSize + (Columns - 1) * Spacing;
            float height = rows * SlotSize + (rows - 1) * Spacing;
            UiBuilder.Place(grid, new Vector2(0.5f, 1), new Vector2(0.5f, 1), topOffset, new Vector2(width, height));

            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(SlotSize, SlotSize);
            layout.spacing = new Vector2(Spacing, Spacing);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = Columns;

            return grid;
        }

        private void AddSlot(RectTransform parent, int index)
        {
            ItemSlotUI slot = ItemSlotUI.Create(parent, index, SlotSize, interactive: true);
            slot.Clicked += OnSlotClicked;
            slot.HoverChanged += OnSlotHover;
            _slots.Add(slot);
        }

        private void OnSlotClicked(ItemSlotUI slot, PointerEventData eventData)
        {
            Keyboard keyboard = Keyboard.current;
            bool shift = keyboard != null && keyboard.shiftKey.isPressed;

            int button =
                eventData.button == PointerEventData.InputButton.Left ? 0 :
                eventData.button == PointerEventData.InputButton.Right ? 1 : -1;

            if (button < 0)
                return;

            _inventory.Click(slot.SlotIndex, button, shift);

            Refresh();
            OnSlotHover(slot);
        }

        private void OnSlotHover(ItemSlotUI slot)
        {
            if (_tooltip == null)
                return;

            ItemStack stack = slot.IsHovered ? _inventory.Container[slot.SlotIndex] : ItemStack.Empty;

            if (stack.IsEmpty || !Held.IsEmpty)
            {
                _tooltip.gameObject.SetActive(false);
                return;
            }

            string description = stack.Item.description;
            _tooltipText.text = string.IsNullOrEmpty(description)
                ? $"<b>{stack.Item.DisplayName}</b>"
                : $"<b>{stack.Item.DisplayName}</b>\n<size=15><color=#B8BCC6>{description}</color></size>";

            float height = string.IsNullOrEmpty(description) ? 40f : 70f;
            _tooltip.sizeDelta = new Vector2(260f, height);
            _tooltip.gameObject.SetActive(true);
            _tooltip.SetAsLastSibling();
        }

        private void Refresh()
        {
            if (_inventory == null || _inventory.Container == null)
                return;

            foreach (ItemSlotUI slot in _slots)
                slot.SetStack(_inventory.Container[slot.SlotIndex]);

            ItemStack held = Held;

            if (_heldIcon != null)
            {
                _heldIcon.enabled = !held.IsEmpty;

                if (!held.IsEmpty)
                {
                    _heldIcon.sprite = ItemIcons.Get(held.Item);
                    _heldCount.text = held.Count > 1 ? held.Count.ToString() : string.Empty;
                    _heldIcon.transform.SetAsLastSibling();
                }
                else
                {
                    _heldCount.text = string.Empty;
                }
            }
        }

        private void FollowMouse(RectTransform rect, Vector2 offset)
        {
            Mouse mouse = Mouse.current;

            if (mouse == null || rect == null)
                return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRect,
                mouse.position.ReadValue(),
                null,
                out Vector2 local);

            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = local + offset;
        }
    }
}
