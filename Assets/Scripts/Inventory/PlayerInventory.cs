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

        /// <summary>The open chest's contents as the server last sent them (null = no chest open).</summary>
        public InventoryContainer Chest { get; private set; }
        public Vector3Int ChestPosition { get; private set; }

        /// <summary>A chest window was opened or closed by the server.</summary>
        public event Action<bool> ChestChanged;

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

        /// <summary>Raised when a break or place was performed (for hand swing).</summary>
        public event Action<PlayerActionType> ActionPerformed;

        /// <summary>Raised when the held (cursor) stack may have changed.</summary>
        public event Action HeldChanged;

        private void Awake()
        {
            _netObject = GetComponent<NetObject>();
        }

        private void OnDestroy()
        {
            if (_network != null)
            {
                _network.InventorySnapshotReceived -= OnSnapshot;
                _network.ContainerChanged -= OnContainer;
            }

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
                _network.ContainerChanged += OnContainer;

                if (_network.HasSnapshot)
                    OnSnapshot(_network.LastAcknowledged, _network.LastHotbarSize, _network.LastSlots, _network.LastHeld);
            }
            else
            {
                InventoryLayout.AddStarterItems(Container);
            }

            if (GetComponent<BlockInteraction>() == null)
                gameObject.AddComponent<BlockInteraction>();

            if (GetComponent<FirstPersonArms>() == null)
                gameObject.AddComponent<FirstPersonArms>();

            if (GetComponent<PlayerAbilities>() == null)
                gameObject.AddComponent<PlayerAbilities>();

            GameHud.EnsureFor(this);
        }

        // ------------------------------------------------------------
        // Actions
        // ------------------------------------------------------------

        public void Click(int slot, int button, bool shift) => PerformInventory(PlayerAction.Click(slot, button, shift));

        public void Spread(IReadOnlyList<int> slots, bool oneEach) => PerformInventory(PlayerAction.Spread(slots, oneEach));

        public void CollectToCursor(int slot) => PerformInventory(PlayerAction.CollectToCursor(slot));

        public void SwapWithHotbar(int slot, int hotbarIndex) => PerformInventory(PlayerAction.SwapWithHotbar(slot, hotbarIndex));

        public void DropHeld(bool oneItem)
        {
            if (_held.IsEmpty)
                return;

            if (!IsNetworked)
            {
                GameHud.Instance?.ShowMessage("Dropping items needs a game (Host Game).");
                return;
            }

            PerformInventory(PlayerAction.DropCursor(oneItem));
        }

        public void ChestCommand(ContainerLogic.Command command)
        {
            if (Chest != null)
                PerformInventory(PlayerAction.ChestCommand(command));
        }

        /// <summary>Asks the server to open the chest at a block (its contents arrive as a snapshot).</summary>
        public void OpenChest(Vector3Int position)
        {
            if (!IsNetworked)
            {
                GameHud.Instance?.ShowMessage("Chests need a game (Host Game).");
                return;
            }

            Perform(PlayerAction.OpenChest(position));
        }

        public void CloseChest()
        {
            if (Chest == null)
                return;

            Chest = null;
            Perform(PlayerAction.CloseChest());
            ChestChanged?.Invoke(false);
        }

        /// <summary>
        /// Inventory and chest actions are predicted: applied at once, then
        /// reconciled with the server's snapshot (which carries the open
        /// chest at the same acknowledged sequence, see OnSnapshot).
        /// </summary>
        private void PerformInventory(PlayerAction action)
        {
            Perform(action);
        }

        private void OnContainer(bool open, Vector3Int position, ItemStack[] slots)
        {
            if (!open)
            {
                if (Chest != null)
                {
                    Chest = null;
                    ChestChanged?.Invoke(false);
                }
                return;
            }

            bool opened = Chest == null;
            if (Chest == null || Chest.Count != slots.Length)
                Chest = new InventoryContainer(Math.Max(1, slots.Length), 0);

            ChestPosition = position;
            Chest.SetAll(slots);

            if (opened)
                ChestChanged?.Invoke(true);
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
            action.Slot = (byte)SelectedSlot; // client-side: which tool broke it (for the drop)

            return Perform(action);
        }

        // ------------------------------------------------------------
        // Combat, items on the ground, classes and abilities
        // ------------------------------------------------------------

        /// <summary>Plays the hand swing without an action (e.g. attacking).</summary>
        public void NotifySwing() => ActionPerformed?.Invoke(PlayerActionType.Attack);

        /// <summary>Hits a creature (by its network id).</summary>
        public void AttackEntity(uint networkId)
        {
            if (networkId != 0)
                Perform(PlayerAction.AttackEntity(networkId));
        }

        /// <summary>Drops one item (or the whole stack) from a slot on the ground.</summary>
        public void DropFromSlot(int slot, bool wholeStack)
        {
            if (Container == null || slot < 0 || slot >= Container.Count || Container[slot].IsEmpty)
                return;

            if (!IsNetworked)
            {
                GameHud.Instance?.ShowMessage("Dropping items needs a game (Host Game).");
                return;
            }

            Perform(PlayerAction.Drop(slot, wholeStack));
        }

        /// <summary>Eats the selected item if it is food.</summary>
        public void UseSelected()
        {
            ItemStack stack = SelectedStack;
            if (stack.IsEmpty || stack.Item.healAmount <= 0f)
                return;

            Perform(PlayerAction.Use(SelectedSlot));
        }

        public void ChooseClass(int classIndex) => Perform(PlayerAction.PickClass(classIndex));

        public void UnlockSkill(int skillIndex) => Perform(PlayerAction.Unlock(skillIndex));

        public void UseAbility(AbilityCode ability, Vector3 direction) => Perform(PlayerAction.Ability(ability, direction));

        /// <summary>Tells the server we started hitting a block (it times the break).</summary>
        public void StartBreaking(Vector3Int voxel)
        {
            if (Container == null)
                return;

            PlayerAction action = PlayerAction.StartBreaking(voxel);
            Perform(action);
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

            if (action.Type == PlayerActionType.BreakBlock || action.Type == PlayerActionType.PlaceBlock)
                ActionPerformed?.Invoke(action.Type);

            return true;
        }

        private void ApplyInventoryEffect(PlayerAction action) => ApplyInventoryEffect(action, Chest);

        private void ApplyInventoryEffect(PlayerAction action, InventoryContainer chest)
        {
            ItemStack heldBefore = _held;

            switch (action.Type)
            {
                case PlayerActionType.InventoryClick:
                    ContainerLogic.Click(Container, chest, ref _held, action.Slot, action.Button, action.Shift);
                    break;

                case PlayerActionType.InventorySpread:
                    var slots = new List<int>();
                    if (action.Slots != null)
                        foreach (byte slot in action.Slots)
                            slots.Add(slot);
                    ContainerLogic.Spread(Container, chest, ref _held, slots, action.Button == 1);
                    break;

                case PlayerActionType.InventoryCollect:
                    ContainerLogic.Collect(Container, chest, ref _held, action.Slot);
                    break;

                case PlayerActionType.InventorySwap:
                    ContainerLogic.SwapWithHotbar(Container, chest, action.Slot, action.Button);
                    break;

                case PlayerActionType.ContainerCommand:
                    if (chest != null)
                        ContainerLogic.RunCommand(Container, chest, (ContainerLogic.Command)action.Button);
                    break;

                case PlayerActionType.DropHeld:
                    _held = _held.WithCount(action.Button == 1 ? _held.Count - 1 : 0);
                    break;

                case PlayerActionType.InventoryClose:
                    InventoryLogic.Close(Container, ref _held);
                    break;

                case PlayerActionType.BreakBlock:
                    InventoryLogic.AddDrop(Container, action.PredictedBlock, Container[action.Slot].Item);
                    break;

                case PlayerActionType.PlaceBlock:
                    if (InventoryLogic.TryGetPlaceableBlock(Container, action.Slot, out _))
                        InventoryLogic.ConsumeForPlace(Container, action.Slot);
                    break;

                case PlayerActionType.DropItem:
                    if (action.Slot < Container.Count && !Container[action.Slot].IsEmpty)
                        Container.RemoveFromSlot(action.Slot, action.Button == 1 ? Container[action.Slot].Count : 1);
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

            // The open chest too (the snapshot has it at the same sequence).
            // Without one in the snapshot the server's close message follows.
            ItemStack[] chestSlots = _network?.LastChestSlots;
            bool chestSynced = Chest != null && chestSlots != null &&
                               _network.LastChestPosition == ChestPosition && Chest.Count == chestSlots.Length;
            if (chestSynced)
                Chest.SetAll(chestSlots, notify: false);

            _pending.RemoveAll(action => action.Sequence <= acknowledged);

            // A chest the snapshot did not reset must not get the pending
            // clicks applied a second time.
            foreach (PlayerAction action in _pending)
                ApplyInventoryEffect(action, chestSynced ? Chest : null);

            Container.NotifyChanged();
            if (chestSynced)
                Chest.NotifyChanged();
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

            // The server shows other players what we are holding.
            if (_network != null)
            {
                PlayerAction action = PlayerAction.Select(index);
                if (_network.Send(ref action) != 0)
                    _pending.Add(action);
            }
        }

        private void HandleHotbarInput()
        {
            Keyboard keyboard = Keyboard.current;

            // Q drops one of the held item, Ctrl+Q the whole stack.
            if (keyboard != null && keyboard.qKey.wasPressedThisFrame)
                DropFromSlot(SelectedSlot, keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);

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
