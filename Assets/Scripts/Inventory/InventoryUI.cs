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
    /// The inventory screen (Tab or I to open, Esc to close), and the
    /// chest window (right-click a chest).
    ///
    ///   left click        pick up / put down / swap / merge a stack
    ///   right click       pick up half / put down one
    ///   shift + click     quick move: chest <-> inventory, else hotbar <-> backpack
    ///   drag a stack      left: spread evenly, right: one per slot
    ///   double-click      collect the same item onto the cursor
    ///   1-9 over a slot   swap with that hotbar slot
    ///   Q over a slot     drop one (Ctrl+Q: the stack)
    ///   click outside     drop the cursor stack (right-click: one)
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
        private readonly List<ItemSlotUI> _chestSlots = new List<ItemSlotUI>();
        private readonly List<int> _dragSlots = new List<int>();
        private GameObject _chestPanel;
        private ItemSlotUI _hovered;
        private int _dragButton = -1;
        private int _lastClickSlot = -1;
        private float _lastClickTime;
        private int _spreadFrame = -1;

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
            // drops the stack on the cursor.
            Image dim = UiBuilder.Image("Inventory", canvasRect, new Color(0f, 0f, 0f, 0.45f), raycast: true);
            UiBuilder.Stretch(dim.rectTransform);
            _root = dim.gameObject;
            dim.gameObject.AddComponent<ClickCatcher>().Clicked += button => _inventory.DropHeld(oneItem: button == 1);

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
            help.text = "Shift-click: quick move   Drag: spread   Double-click: collect   1-9: to hotbar   Q: drop";
            help.fontSize = 14;

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

            BuildChestPanel(dim.rectTransform, panelHeight, gridWidth);
            inventory.ChestChanged += OnChestChanged;

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

        private void BuildChestPanel(RectTransform parent, float inventoryHeight, float gridWidth)
        {
            int rows = Mathf.CeilToInt(Gameplay.ContainerLogic.ChestSize / (float)Columns);
            float height = 64f + rows * SlotSize + (rows - 1) * Spacing + 70f;

            Image panel = UiBuilder.Image("Chest", parent, UiBuilder.PanelColor, raycast: true);
            UiBuilder.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), new Vector2(0, 20 + inventoryHeight * 0.5f + 12f), new Vector2(gridWidth + 48f, height));
            UiBuilder.AddOutline(panel, new Color(0f, 0f, 0f, 0.6f), 3f);
            _chestPanel = panel.gameObject;

            TextMeshProUGUI title = UiBuilder.Text("Title", panel.rectTransform, 30, TextAlignmentOptions.TopLeft, UiBuilder.TextColor);
            UiBuilder.Place(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -14), new Vector2(300, 40));
            title.text = "Chest";
            title.fontStyle = FontStyles.Bold;

            RectTransform grid = CreateGrid("Chest Slots", panel.rectTransform, new Vector2(0, -60), rows);
            for (int i = 0; i < Gameplay.ContainerLogic.ChestSize; i++)
            {
                ItemSlotUI slot = ItemSlotUI.Create(grid, Gameplay.ContainerLogic.PlayerSlots + i, SlotSize, interactive: true);
                slot.Clicked += OnSlotClicked;
                slot.HoverChanged += OnSlotHover;
                _chestSlots.Add(slot);
            }

            RectTransform buttons = UiBuilder.Rect("Buttons", panel.rectTransform);
            UiBuilder.Place(buttons, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 14), new Vector2(gridWidth, 44));
            var layout = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;

            MenuWidgets.Button(buttons, "Quick stack", () => _inventory.ChestCommand(Gameplay.ContainerLogic.Command.QuickStack), height: 44f);
            MenuWidgets.Button(buttons, "Deposit all", () => _inventory.ChestCommand(Gameplay.ContainerLogic.Command.DepositAll), height: 44f);
            MenuWidgets.Button(buttons, "Take all", () => _inventory.ChestCommand(Gameplay.ContainerLogic.Command.TakeAll), height: 44f);

            _chestPanel.SetActive(false);
        }

        private void OnChestChanged(bool open)
        {
            if (open)
            {
                SetOpen(true);
                _chestPanel.SetActive(true);
            }
            else
            {
                _chestPanel.SetActive(false);
                if (IsOpen)
                    SetOpen(false); // closing a chest closes the whole window, like Minecraft
            }

            Refresh();
        }

        /// <summary>The stack in a slot (inventory or chest).</summary>
        private ItemStack StackAt(int index)
        {
            if (index < Gameplay.ContainerLogic.PlayerSlots)
                return _inventory.Container[index];

            InventoryContainer chest = _inventory.Chest;
            int local = index - Gameplay.ContainerLogic.PlayerSlots;
            return chest != null && local < chest.Count ? chest[local] : ItemStack.Empty;
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
                _inventory.CloseChest();
                _chestPanel.SetActive(false);
                _tooltip.gameObject.SetActive(false);
                _dragButton = -1;
                _dragSlots.Clear();
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
                HandleSlotKeys(keyboard);
                HandleDrag();

                // The chest contents change when the server says so.
                if (_inventory.Chest != null)
                    Refresh();
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

        /// <summary>1-9 over a slot swaps it with the hotbar; Q drops from it.</summary>
        private void HandleSlotKeys(Keyboard keyboard)
        {
            if (keyboard == null || _hovered == null || !_hovered.IsHovered)
                return;

            Key[] keys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };
            for (int i = 0; i < keys.Length && i < _inventory.Container.HotbarSize; i++)
            {
                if (keyboard[keys[i]].wasPressedThisFrame)
                {
                    _inventory.SwapWithHotbar(_hovered.SlotIndex, i);
                    Refresh();
                }
            }

            if (keyboard.qKey.wasPressedThisFrame && _hovered.SlotIndex < Gameplay.ContainerLogic.PlayerSlots)
            {
                _inventory.DropFromSlot(_hovered.SlotIndex, keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);
                Refresh();
            }
        }

        /// <summary>Holding a mouse button with a stack and moving over slots spreads it.</summary>
        private void HandleDrag()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
                return;

            if (_dragButton < 0)
            {
                bool left = mouse.leftButton.wasPressedThisFrame, right = mouse.rightButton.wasPressedThisFrame;
                if ((left || right) && !Held.IsEmpty && _hovered != null && _hovered.IsHovered)
                {
                    _dragButton = left ? 0 : 1;
                    _dragSlots.Clear();
                    _dragSlots.Add(_hovered.SlotIndex);
                }

                return;
            }

            bool released = _dragButton == 0 ? mouse.leftButton.wasReleasedThisFrame : mouse.rightButton.wasReleasedThisFrame;
            if (released)
                FinishDrag();
        }

        /// <summary>Ends a drag; true if it spread the stack (then the click is not used).</summary>
        private bool FinishDrag()
        {
            int button = _dragButton;
            _dragButton = -1;

            if (_dragSlots.Count < 2)
            {
                _dragSlots.Clear();
                return false;
            }

            _inventory.Spread(new List<int>(_dragSlots), oneEach: button == 1);
            _dragSlots.Clear();
            _spreadFrame = Time.frameCount;
            Refresh();
            return true;
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

            // The release that ended a drag is not also a click.
            if (_spreadFrame == Time.frameCount || (_dragButton >= 0 && FinishDrag()))
                return;
            _dragButton = -1;
            _dragSlots.Clear();

            // Double-click with a stack on the cursor: gather the same item.
            bool doubleClick = button == 0 && !shift && slot.SlotIndex == _lastClickSlot && Time.unscaledTime - _lastClickTime < 0.3f;
            _lastClickSlot = button == 0 ? slot.SlotIndex : -1;
            _lastClickTime = Time.unscaledTime;

            if (doubleClick && !Held.IsEmpty)
            {
                _inventory.CollectToCursor(slot.SlotIndex);
                _lastClickSlot = -1;
                Refresh();
                return;
            }

            _inventory.Click(slot.SlotIndex, button, shift);

            Refresh();
            OnSlotHover(slot);
        }

        private void OnSlotHover(ItemSlotUI slot)
        {
            if (slot.IsHovered)
            {
                _hovered = slot;

                if (_dragButton >= 0 && !_dragSlots.Contains(slot.SlotIndex))
                    _dragSlots.Add(slot.SlotIndex);
            }

            if (_tooltip == null)
                return;

            ItemStack stack = slot.IsHovered ? StackAt(slot.SlotIndex) : ItemStack.Empty;

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

            foreach (ItemSlotUI slot in _chestSlots)
                slot.SetStack(StackAt(slot.SlotIndex));

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

    /// <summary>Reports clicks on the dimmed background (outside the panels).</summary>
    public sealed class ClickCatcher : MonoBehaviour, IPointerClickHandler
    {
        public event System.Action<int> Clicked;

        public void OnPointerClick(PointerEventData eventData)
        {
            // Only clicks on this object itself, not on panels above it.
            if (eventData.pointerPressRaycast.gameObject != gameObject)
                return;

            Clicked?.Invoke(eventData.button == PointerEventData.InputButton.Right ? 1 : 0);
        }
    }
}
