using System;
using System.Collections.Generic;
using Gameplay;
using GameUI;
using Networking;
using UnityEngine;
using UnityEngine.InputSystem;
using World;
using World.Blocks;

namespace Items
{
    /// <summary>
    /// The local player's inventory and actions (inventory clicks,
    /// breaking and placing blocks).
    ///
    /// In multiplayer the server owns the real inventory. Every action is
    /// applied here immediately (prediction), sent to the server, and kept
    /// until the server's snapshot acknowledges it. When a snapshot
    /// arrives, the inventory is reset to the server's state and the
    /// still-unacknowledged actions are re-applied - the same approach
    /// the movement code uses. Offline (no network runtime) actions are
    /// simply applied.
    ///
    /// Lives on the player prefab; only does anything for the player this
    /// machine controls.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerInventory : MonoBehaviour
    {
        [Header("Pickup (offline only)")]
        [SerializeField]
        [Min(0.5f)]
        private float pickupDistance = 4f;

        [SerializeField]
        private Camera playerCamera;

        private readonly List<PlayerAction> _pending = new List<PlayerAction>();

        private NetObject _netObject;
        private bool _initialised;
        private PickupItem _pickupTarget;
        private ClientGameplayModule _network;
        private ItemStack _held;

        /// <summary>The inventory of the player this machine controls.</summary>
        public static PlayerInventory Local { get; private set; }

        public InventoryContainer Container { get; private set; }

        /// <summary>Stack currently held on the cursor in the inventory screen.</summary>
        public ItemStack Held => _held;

        public int SelectedSlot { get; private set; }

        public ItemStack SelectedStack =>
            Container == null ? ItemStack.Empty : Container[SelectedSlot];

        /// <summary>True when the server decides what the inventory holds.</summary>
        public bool IsNetworked => _network != null;

        /// <summary>The pickup the player is looking at (offline only).</summary>
        public PickupItem PickupTarget => _pickupTarget;

        public event Action<int> SelectedSlotChanged;

        /// <summary>Raised when the held (cursor) stack may have changed.</summary>
        public event Action HeldChanged;

        private void Awake()
        {
            _netObject = GetComponent<NetObject>();
        }

        private void OnDestroy()
        {
            if (_network != null)
                _network.InventorySnapshotReceived -= OnSnapshot;

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

            if (!IsNetworked)
            {
                UpdatePickupTarget();
                HandlePickupInput();
            }
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

            Container = InventoryLayout.CreateContainer();

            NetworkRuntime runtime = NetworkRuntime.Current;
            _network = runtime != null && runtime.RunsClient ? runtime.ClientGameplay : null;

            if (_network != null)
            {
                // Filled by the server's snapshot.
                _network.InventorySnapshotReceived += OnSnapshot;

                if (_network.HasSnapshot)
                    OnSnapshot(_network.LastAcknowledged, _network.LastHotbarSize, _network.LastSlots, _network.LastHeld);
            }
            else
            {
                InventoryLayout.AddStarterItems(Container);
            }

            if (GetComponent<BlockInteraction>() == null)
                gameObject.AddComponent<BlockInteraction>();

            GameHud.EnsureFor(this);
        }

        // ------------------------------------------------------------
        // Actions
        // ------------------------------------------------------------

        public void Click(int slot, int button, bool shift)
        {
            Perform(PlayerAction.Click(slot, button, shift));
        }

        /// <summary>Called when the inventory screen closes: returns the held stack.</summary>
        public void CloseInventory()
        {
            if (!_held.IsEmpty)
                Perform(PlayerAction.Close());
        }

        /// <summary>Breaks a block (voxel position). Returns false if not allowed.</summary>
        public bool TryBreak(Vector3Int voxel)
        {
            WorldManager world = WorldManager.Active;

            if (world == null || !world.IsReady)
                return false;

            BlockId block = world.GetBlock(voxel.x, voxel.y, voxel.z);

            if (!BlockEditRules.CanBreak(block))
                return false;

            PlayerAction action = PlayerAction.Break(voxel);
            action.PredictedBlock = block;

            return Perform(action);
        }

        /// <summary>Places the selected hotbar block at a voxel. Returns false if not allowed.</summary>
        public bool TryPlace(Vector3Int voxel)
        {
            WorldManager world = WorldManager.Active;

            if (world == null || !world.IsReady)
                return false;

            if (!InventoryLogic.TryGetPlaceableBlock(Container, SelectedSlot, out BlockId block))
                return false;

            if (!BlockEditRules.CanPlaceInto(world.GetBlock(voxel.x, voxel.y, voxel.z)))
                return false;

            PlayerAction action = PlayerAction.Place(voxel, SelectedSlot);
            action.PredictedBlock = block;

            return Perform(action);
        }

        private bool Perform(PlayerAction action)
        {
            if (Container == null)
                return false;

            if (_network != null)
            {
                if (_network.Send(ref action) == 0)
                    return false;

                _pending.Add(action);
            }

            ApplyInventoryEffect(action);
            ApplyWorldEffect(action);
            return true;
        }

        private void ApplyInventoryEffect(PlayerAction action)
        {
            ItemStack heldBefore = _held;

            switch (action.Type)
            {
                case PlayerActionType.InventoryClick:
                    InventoryLogic.Click(Container, ref _held, action.Slot, action.Button, action.Shift);
                    break;

                case PlayerActionType.InventoryClose:
                    InventoryLogic.Close(Container, ref _held);
                    break;

                case PlayerActionType.BreakBlock:
                    InventoryLogic.AddDrop(Container, action.PredictedBlock);
                    break;

                case PlayerActionType.PlaceBlock:
                    if (InventoryLogic.TryGetPlaceableBlock(Container, action.Slot, out _))
                        InventoryLogic.ConsumeForPlace(Container, action.Slot);
                    break;
            }

            if (heldBefore.Item != _held.Item || heldBefore.Count != _held.Count)
                HeldChanged?.Invoke();
        }

        private void ApplyWorldEffect(PlayerAction action)
        {
            if (action.Type != PlayerActionType.BreakBlock && action.Type != PlayerActionType.PlaceBlock)
                return;

            // On a listen server the world is the server's: it applies the
            // edit itself after validating it.
            if (_network != null && _network.SharesWorldWithServer)
                return;

            WorldManager world = WorldManager.Active;
            if (world == null)
                return;

            BlockId block = action.Type == PlayerActionType.BreakBlock ? BlockId.Air : action.PredictedBlock;

            // Offline this is the real change. Online it is a prediction;
            // the server confirms it (or sends the real block back).
            world.SetBlock(action.Position.x, action.Position.y, action.Position.z, block);
        }

        private void OnSnapshot(uint acknowledged, int hotbarSize, ItemStack[] slots, ItemStack held)
        {
            if (Container == null || slots == null)
                return;

            // Server state, then re-apply what the server has not seen yet.
            Container.SetAll(slots, notify: false);
            _held = held;

            _pending.RemoveAll(action => action.Sequence <= acknowledged);

            foreach (PlayerAction action in _pending)
                ApplyInventoryEffect(action);

            Container.NotifyChanged();
            HeldChanged?.Invoke();
        }

        // ------------------------------------------------------------
        // Input
        // ------------------------------------------------------------

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

        // Pickups of scene objects are offline-only: the server does not
        // know about them. Networked item drops come with networked
        // item entities.
        private void UpdatePickupTarget()
        {
            PickupItem previous = _pickupTarget;
            _pickupTarget = null;
            FindPickupTarget();

            // Outline the item being looked at (if it has an OutlineObject).
            if (previous != _pickupTarget)
            {
                if (previous != null && previous.TryGetComponent(out OutlineObject oldOutline))
                    oldOutline.EnableHighlight(false);

                if (_pickupTarget != null && _pickupTarget.TryGetComponent(out OutlineObject newOutline))
                    newOutline.EnableHighlight(true);
            }
        }

        private void FindPickupTarget()
        {

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

            int leftover = Container.Add(_pickupTarget.itemData, _pickupTarget.amount);

            if (leftover == 0)
            {
                _pickupTarget.OnPickedUp();
                _pickupTarget = null;
            }
            else
            {
                _pickupTarget.amount = leftover;
                GameHud.Instance?.ShowMessage("Inventory is full");
            }
        }
    }
}
