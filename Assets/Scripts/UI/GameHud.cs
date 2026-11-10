using System.Collections.Generic;
using Items;
using Networking;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace GameUI
{
    /// <summary>
    /// In-game HUD for the local player: crosshair, hotbar, selected item
    /// name, pickup prompt, short messages, underwater tint, and the
    /// inventory screen.
    ///
    /// Created from code by <see cref="PlayerInventory"/> when the local
    /// player spawns, so scenes need no UI setup.
    /// </summary>
    public sealed class GameHud : MonoBehaviour
    {
        private const float HotbarSlotSize = 64f;
        private const float HotbarSpacing = 6f;

        private readonly List<ItemSlotUI> _hotbarSlots = new List<ItemSlotUI>();

        private PlayerInventory _inventory;
        private CharacterMotor _motor;
        private Gameplay.BlockInteraction _interaction;
        private InventoryUI _inventoryUI;

        private RectTransform _crosshair;
        private Image _breakBack;
        private Image _breakFill;
        private Image _underwater;
        private TextMeshProUGUI _itemName;
        private TextMeshProUGUI _prompt;
        private TextMeshProUGUI _message;
        private float _itemNameTimer;
        private float _messageTimer;

        public static GameHud Instance { get; private set; }

        public InventoryUI Inventory => _inventoryUI;

        /// <summary>Skill tree and quest log (K / J).</summary>
        public CharacterScreens Character { get; private set; }

        private Image _damageFlash;
        private float _damageFlashTimer;

        /// <summary>A short red flash when the player is hurt.</summary>
        public void FlashDamage()
        {
            _damageFlashTimer = 0.35f;
        }

        /// <summary>Creates the HUD for a player's inventory if it does not exist yet.</summary>
        public static GameHud EnsureFor(PlayerInventory inventory)
        {
            if (Instance != null)
            {
                if (Instance._inventory == inventory)
                    return Instance;

                // A new local player (respawn/reconnect): rebuild cleanly.
                Destroy(Instance.gameObject);
                Instance = null;
            }

            EnsureEventSystem();

            var go = new GameObject("Game HUD", typeof(RectTransform));
            go.layer = 5;

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();

            var hud = go.AddComponent<GameHud>();
            hud.Build((RectTransform)go.transform);
            hud.Bind(inventory);

            go.AddComponent<PlayerStatusHud>().Build((RectTransform)go.transform);
            hud.Character = go.AddComponent<CharacterScreens>();
            hud.Character.Build((RectTransform)go.transform, inventory);

            return hud;
        }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            Unbind();
        }

        public void ShowMessage(string text, float seconds = 2.5f)
        {
            if (_message == null)
                return;

            _message.text = text;
            _messageTimer = seconds;
        }

        private void Build(RectTransform root)
        {
            // Underwater tint, behind everything else.
            // Light tint only; the underwater fog (UnderwaterEffect) does the real work.
            _underwater = UiBuilder.Image("Underwater", root, new Color(0.05f, 0.3f, 0.4f, 0.12f));
            UiBuilder.Stretch(_underwater.rectTransform);
            _underwater.enabled = false;

            // Crosshair.
            _crosshair = UiBuilder.Rect("Crosshair", root);
            UiBuilder.Place(_crosshair, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24, 24));

            Image horizontal = UiBuilder.Image("H", _crosshair, new Color(1, 1, 1, 0.9f));
            UiBuilder.Place(horizontal.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(22, 3));
            UiBuilder.AddOutline(horizontal, new Color(0, 0, 0, 0.5f), 1f);

            Image vertical = UiBuilder.Image("V", _crosshair, new Color(1, 1, 1, 0.9f));
            UiBuilder.Place(vertical.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(3, 22));
            UiBuilder.AddOutline(vertical, new Color(0, 0, 0, 0.5f), 1f);

            // Break progress under the crosshair.
            _breakBack = UiBuilder.Image("Break Progress", root, new Color(0f, 0f, 0f, 0.55f));
            UiBuilder.Place(_breakBack.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -26f), new Vector2(64f, 6f));
            _breakFill = UiBuilder.Image("Fill", _breakBack.rectTransform, new Color(1f, 0.85f, 0.4f, 0.95f));
            UiBuilder.Place(_breakFill.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(1f, 0f), new Vector2(0f, 4f));
            _breakBack.gameObject.SetActive(false);

            // Pickup prompt under the crosshair.
            _prompt = UiBuilder.Text("Prompt", root, 24, TextAlignmentOptions.Center, UiBuilder.TextColor);
            UiBuilder.Place(_prompt.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(0, -40), new Vector2(800, 40));
            UiBuilder.AddOutline(_prompt, new Color(0, 0, 0, 0.7f), 1.5f);

            // Messages (e.g. "Inventory is full").
            _message = UiBuilder.Text("Message", root, 26, TextAlignmentOptions.Center, UiBuilder.AccentColor);
            UiBuilder.Place(_message.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), new Vector2(0, 120), new Vector2(900, 40));
            UiBuilder.AddOutline(_message, new Color(0, 0, 0, 0.7f), 1.5f);

            // Name of the selected item above the hotbar.
            _itemName = UiBuilder.Text("Item Name", root, 24, TextAlignmentOptions.Center, UiBuilder.TextColor);
            UiBuilder.Place(_itemName.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 150), new Vector2(600, 34));
            UiBuilder.AddOutline(_itemName, new Color(0, 0, 0, 0.7f), 1.5f);

            // Controls hint.
            // Bottom right, clear of the hotbar in the middle.
            TextMeshProUGUI hint = UiBuilder.Text("Hint", root, 16, TextAlignmentOptions.BottomRight, new Color(1, 1, 1, 0.55f));
            UiBuilder.Place(hint.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-16, 12), new Vector2(560, 44));
            hint.text = "LMB break/attack   RMB place/eat   Q drop   1-9 hotbar   Tab inventory   K skills   J quests";

            _damageFlash = UiBuilder.Image("Damage", root, new Color(0.8f, 0.05f, 0.05f, 0f));
            UiBuilder.Stretch(_damageFlash.rectTransform);
        }

        private void Bind(PlayerInventory inventory)
        {
            _inventory = inventory;
            _motor = inventory.GetComponent<CharacterMotor>();
            _interaction = inventory.GetComponent<Gameplay.BlockInteraction>();

            var root = (RectTransform)transform;
            BuildHotbar(root);

            _inventoryUI = gameObject.GetComponent<InventoryUI>();
            if (_inventoryUI == null)
                _inventoryUI = gameObject.AddComponent<InventoryUI>();

            _inventoryUI.Build(inventory, root);

            inventory.Container.Changed += RefreshHotbar;
            inventory.SelectedSlotChanged += OnSelectedSlotChanged;

            RefreshHotbar();
            OnSelectedSlotChanged(inventory.SelectedSlot);
        }

        private void Unbind()
        {
            if (_inventory == null)
                return;

            if (_inventory.Container != null)
                _inventory.Container.Changed -= RefreshHotbar;

            _inventory.SelectedSlotChanged -= OnSelectedSlotChanged;
            _inventory = null;
        }

        private void BuildHotbar(RectTransform root)
        {
            foreach (ItemSlotUI slot in _hotbarSlots)
            {
                if (slot != null)
                    Destroy(slot.transform.parent.gameObject);
            }

            _hotbarSlots.Clear();

            int count = _inventory.Container.HotbarSize;
            float width = count * HotbarSlotSize + (count - 1) * HotbarSpacing;

            Image bar = UiBuilder.Image("Hotbar", root, UiBuilder.PanelColor);
            UiBuilder.Place(bar.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 20), new Vector2(width + 16, HotbarSlotSize + 16));

            var layout = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = HotbarSpacing;
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            for (int i = 0; i < count; i++)
            {
                ItemSlotUI slot = ItemSlotUI.Create(bar.rectTransform, i, HotbarSlotSize, interactive: false);

                TextMeshProUGUI number = UiBuilder.Text("Key", slot.transform, 14, TextAlignmentOptions.TopLeft, new Color(1, 1, 1, 0.5f));
                UiBuilder.Stretch(number.rectTransform);
                number.rectTransform.offsetMin = new Vector2(5, 2);
                number.rectTransform.offsetMax = new Vector2(-2, -3);
                number.text = (i + 1).ToString();

                _hotbarSlots.Add(slot);
            }
        }

        private void RefreshHotbar()
        {
            if (_inventory == null)
                return;

            for (int i = 0; i < _hotbarSlots.Count; i++)
                _hotbarSlots[i].SetStack(_inventory.Container[i]);

            ShowSelectedName();
        }

        private void OnSelectedSlotChanged(int index)
        {
            for (int i = 0; i < _hotbarSlots.Count; i++)
                _hotbarSlots[i].SetSelected(i == index);

            ShowSelectedName();
        }

        private void ShowSelectedName()
        {
            ItemStack stack = _inventory.SelectedStack;
            string text = stack.IsEmpty ? string.Empty : stack.Item.DisplayName;

            if (_itemName.text != text)
            {
                _itemName.text = text;
                _itemNameTimer = 2f;
            }
        }

        private void Update()
        {
            if (_inventory == null)
            {
                // The local player despawned; hide gameplay UI.
                _crosshair.gameObject.SetActive(false);
                return;
            }

            bool inventoryOpen = (_inventoryUI != null && _inventoryUI.IsOpen) || (Character != null && Character.IsOpen);

            _crosshair.gameObject.SetActive(!inventoryOpen);
            _underwater.enabled = World.Rendering.UnderwaterEffect.CameraUnderwater;

            if (_interaction == null)
                _interaction = _inventory.GetComponent<Gameplay.BlockInteraction>();

            bool breaking = !inventoryOpen && _interaction != null && _interaction.BreakProgress > 0f && _interaction.BreakSeconds > 0.15f;
            if (_breakBack.gameObject.activeSelf != breaking)
                _breakBack.gameObject.SetActive(breaking);
            if (breaking)
                _breakFill.rectTransform.sizeDelta = new Vector2(62f * _interaction.BreakProgress, 4f);

            PickupItem target = _inventory.PickupTarget;

            if (inventoryOpen)
                _prompt.text = string.Empty;
            else if (target != null)
                _prompt.text = $"[E] Pick up {target.itemData.DisplayName}" + (target.amount > 1 ? $" x{target.amount}" : string.Empty);
            else if (_interaction != null && _interaction.HasTarget)
                _prompt.text = $"<size=18><color=#FFFFFFAA>{World.Blocks.BlockRegistry.Get(_interaction.Target.Block).Name}</color></size>";
            else
                _prompt.text = string.Empty;

            _itemNameTimer -= Time.unscaledDeltaTime;
            _itemName.alpha = Mathf.Clamp01(_itemNameTimer / 0.5f);

            _damageFlashTimer = Mathf.Max(0f, _damageFlashTimer - Time.unscaledDeltaTime);
            _damageFlash.color = new Color(0.8f, 0.05f, 0.05f, _damageFlashTimer * 0.9f);

            _messageTimer -= Time.unscaledDeltaTime;
            _message.alpha = Mathf.Clamp01(_messageTimer / 0.5f);
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindFirstObjectByType<EventSystem>() != null)
                return;

            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }
    }
}
