using System;
using System.Collections.Generic;
using Networking;
using GameUI;
using UnityEngine;
using UnityEngine.InputSystem;
using World.Blocks;

namespace Items
{
    /// <summary>
    /// The local player's inventory: a hotbar plus backpack, hotbar
    /// selection (1-9 / mouse wheel) and picking up items (E).
    ///
    /// Lives on the player prefab but only does anything for the player
    /// this machine controls; other players' copies stay idle.
    ///
    /// Note: the inventory is client-side for now. Making it server
    /// authoritative (server owns the container, client sends requests)
    /// belongs together with networked block placing/breaking.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerInventory : MonoBehaviour
    {
        [Header("Slots")]
        [SerializeField]
        [Min(1)]
        private int hotbarSize = 9;

        [Tooltip("Backpack slots in addition to the hotbar.")]
        [SerializeField]
        [Min(0)]
        private int backpackSize = 27;

        [Header("Pickup")]
        [SerializeField]
        [Min(0.5f)]
        private float pickupDistance = 4f;

        [SerializeField]
        private Camera playerCamera;

        [Header("Testing")]
        [Tooltip("Fill the hotbar with some blocks when the player " +
                 "spawns, until mining/crafting exists.")]
        [SerializeField]
        private bool giveStarterBlocks = true;

        [SerializeField]
        private List<ItemData> starterItems = new List<ItemData>();

        private NetObject _netObject;
        private bool _initialised;
        private PickupItem _pickupTarget;

        /// <summary>The inventory of the player this machine controls.</summary>
        public static PlayerInventory Local { get; private set; }

        public InventoryContainer Container { get; private set; }

        public int SelectedSlot { get; private set; }

        public ItemStack SelectedStack =>
            Container == null ? ItemStack.Empty : Container[SelectedSlot];

        /// <summary>The pickup the player is looking at, if any.</summary>
        public PickupItem PickupTarget => _pickupTarget;

        public event Action<int> SelectedSlotChanged;

        private void Awake()
        {
            _netObject = GetComponent<NetObject>();
        }

        private void OnDestroy()
        {
            if (Local == this)
            {
                Local = null;
                PlayerInputReader.GameplayInputBlocked = false;
            }
        }

        private void Update()
        {
            if (!_initialised)
            {
                if (!IsLocalPlayer())
                    return;

                Initialise();
            }

            if (PlayerInputReader.GameplayInputBlocked)
            {
                _pickupTarget = null;
                return;
            }

            HandleHotbarInput();
            UpdatePickupTarget();
            HandlePickupInput();
        }

        private bool IsLocalPlayer()
        {
            NetworkRuntime runtime = NetworkRuntime.Current;

            // Offline test scenes: whoever has this component is local.
            if (runtime == null || _netObject == null)
                return true;

            return _netObject.IsLocallyOwned;
        }

        private void Initialise()
        {
            _initialised = true;
            Local = this;

            Container = new InventoryContainer(hotbarSize + backpackSize, hotbarSize);

            foreach (ItemData item in starterItems)
            {
                if (item != null)
                    Container.Add(item, item.maxStackSize);
            }

            if (giveStarterBlocks)
            {
                BlockId[] blocks =
                {
                    BlockId.Grass, BlockId.Dirt, BlockId.Stone, BlockId.Sand,
                    BlockId.Log, BlockId.Leaves, BlockId.Snow, BlockId.Sandstone
                };

                foreach (BlockId block in blocks)
                {
                    ItemData item = ItemDatabase.GetBlockItem(block);
                    if (item != null)
                        Container.Add(item, 64);
                }
            }

            GameHud.EnsureFor(this);
        }

        public void SelectSlot(int index)
        {
            if (Container == null || Container.HotbarSize == 0)
                return;

            int size = Container.HotbarSize;
            index = ((index % size) + size) % size;

            if (index == SelectedSlot)
                return;

            SelectedSlot = index;
            SelectedSlotChanged?.Invoke(index);
        }

        /// <summary>Adds items; returns how many did not fit.</summary>
        public int AddItem(ItemData item, int amount = 1)
        {
            if (Container == null)
                return amount;

            return Container.Add(item, amount);
        }

        private void HandleHotbarInput()
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard != null)
            {
                Key[] keys =
                {
                    Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5,
                    Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9
                };

                for (int i = 0; i < keys.Length && i < Container.HotbarSize; i++)
                {
                    if (keyboard[keys[i]].wasPressedThisFrame)
                        SelectSlot(i);
                }
            }

            Mouse mouse = Mouse.current;

            if (mouse != null)
            {
                float scroll = mouse.scroll.ReadValue().y;

                if (scroll > 0.01f)
                    SelectSlot(SelectedSlot - 1);
                else if (scroll < -0.01f)
                    SelectSlot(SelectedSlot + 1);
            }
        }

        private void UpdatePickupTarget()
        {
            _pickupTarget = null;

            if (playerCamera == null || !playerCamera.isActiveAndEnabled)
                playerCamera = Camera.main;

            if (playerCamera == null)
                return;

            Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

            if (Physics.Raycast(ray, out RaycastHit hit, pickupDistance, ~0, QueryTriggerInteraction.Collide))
            {
                PickupItem pickup = hit.collider.GetComponentInParent<PickupItem>();

                if (pickup != null && pickup.itemData != null)
                    _pickupTarget = pickup;
            }
        }

        private void HandlePickupInput()
        {
            Keyboard keyboard = Keyboard.current;

            if (_pickupTarget == null || keyboard == null || !keyboard.eKey.wasPressedThisFrame)
                return;

            int leftover = AddItem(_pickupTarget.itemData, _pickupTarget.amount);

            if (leftover == 0)
            {
                _pickupTarget.OnPickedUp();
                _pickupTarget = null;
            }
            else
            {
                // Partly picked up; leave the rest on the ground.
                _pickupTarget.amount = leftover;
                GameHud.Instance?.ShowMessage("Inventory is full");
            }
        }
    }
}
