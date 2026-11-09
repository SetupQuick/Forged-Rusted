using System;
using System.Collections.Generic;
using Gameplay;
using Items;
using UnityEngine;
using World.Blocks;

namespace Networking
{
    /// <summary>
    /// Client side of gameplay: sends player actions to the server,
    /// applies block changes from the server to the local world, and
    /// hands inventory snapshots to the local <see cref="PlayerInventory"/>.
    ///
    /// On a listen server the client shares the server's world, so block
    /// changes from the network are ignored there (the server already
    /// applied them to the same world).
    /// </summary>
    public sealed class ClientGameplayModule : INetworkModule
    {
        public int TickOrder => 5;
        public int DisposeOrder => -5;

        private readonly GameClient _client;
        private readonly ClientMessageRouter _router;
        private readonly Func<IGameplayWorld> _world;
        private readonly List<BlockChange> _buffer = new List<BlockChange>();
        private readonly Dictionary<uint, ItemData> _heldByPeer = new Dictionary<uint, ItemData>();

        /// <summary>(peer id, item now held or null) - for other players' avatars.</summary>
        public event Action<uint, ItemData> HeldItemChanged;

        /// <summary>What another player is holding, as last told by the server.</summary>
        public ItemData GetHeldItem(uint peerId)
        {
            return _heldByPeer.TryGetValue(peerId, out ItemData item) ? item : null;
        }
        private uint _nextSequence = 1;
        private bool _disposed;
        private bool _profileSent;

        /// <summary>Player id/name sent to the server (defaults to this PC's profile; tests override).</summary>
        public string ProfileId { get; set; }
        public string ProfileName { get; set; }

        /// <summary>True on a listen server: the world is the server's own.</summary>
        public bool SharesWorldWithServer { get; }

        /// <summary>(acknowledged sequence, hotbar size, slots, held stack)</summary>
        public event Action<uint, int, ItemStack[], ItemStack> InventorySnapshotReceived;

        /// <summary>Last snapshot, for listeners that subscribe late.</summary>
        public bool HasSnapshot { get; private set; }
        public uint LastAcknowledged { get; private set; }
        public int LastHotbarSize { get; private set; }
        public ItemStack[] LastSlots { get; private set; }
        public ItemStack LastHeld { get; private set; }

        public ClientGameplayModule(
            GameClient client,
            ClientMessageRouter router,
            Func<IGameplayWorld> world,
            bool sharesWorldWithServer)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _router = router ?? throw new ArgumentNullException(nameof(router));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            SharesWorldWithServer = sharesWorldWithServer;

            _router.Register(NetworkMessageType.BlockChanged, OnBlockChanged);
            _router.Register(NetworkMessageType.WorldEdits, OnWorldEdits);
            _router.Register(NetworkMessageType.InventorySnapshot, OnInventorySnapshot);
            _router.Register(NetworkMessageType.Equipment, OnEquipment);
            _router.Register(NetworkMessageType.TimeSync, OnTimeSync);
        }

        private void OnTimeSync(byte[] payload)
        {
            // A listen server's client already runs the server's clock.
            if (SharesWorldWithServer)
                return;

            if (GameplayMessages.TryReadTimeSync(payload, out double seconds, out float dayLength, out float start))
                global::World.WorldClock.ApplyServerTime(seconds, dayLength, start);
        }

        private void OnEquipment(byte[] payload)
        {
            if (!GameplayMessages.TryReadEquipment(payload, out uint peerId, out ItemData held))
                return;

            _heldByPeer[peerId] = held;
            HeldItemChanged?.Invoke(peerId, held);
        }

        /// <summary>Sends an action; returns its sequence number (0 if not connected).</summary>
        public uint Send(ref PlayerAction action)
        {
            if (_disposed || !_client.IsConnected)
                return 0;

            action.Sequence = _nextSequence;
            PlayerAction copy = action;

            bool sent = _client.Send(
                NetworkMessageType.PlayerAction,
                writer => GameplayMessages.WriteAction(writer, copy),
                NetworkDelivery.ReliableOrdered);

            if (!sent)
            {
                // Not sent (e.g. reliable window full): the caller must not
                // predict it, and the sequence number is reused.
                action.Sequence = 0;
                return 0;
            }

            _nextSequence++;
            return action.Sequence;
        }

        public void Tick(double now, double deltaTime)
        {
            // Tell the server who we are, once, so a saved world can give
            // our inventory back.
            if (!_profileSent && !_disposed && _client.IsConnected)
            {
                string id = ProfileId ?? LocalPlayerProfile.Id;
                string name = ProfileName ?? LocalPlayerProfile.Name;

                _profileSent = _client.Send(
                    NetworkMessageType.PlayerProfile,
                    writer => GameplayMessages.WriteProfile(writer, id, name),
                    NetworkDelivery.ReliableOrdered);
            }

            // Changes that arrived before the world had its seed.
            if (_buffer.Count > 0)
            {
                IGameplayWorld world = _world();

                if (world != null && world.IsReady)
                {
                    foreach (BlockChange change in _buffer)
                        world.SetBlock(change.Position, change.Block);

                    _buffer.Clear();
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _router.Unregister(NetworkMessageType.BlockChanged, OnBlockChanged);
            _router.Unregister(NetworkMessageType.WorldEdits, OnWorldEdits);
            _router.Unregister(NetworkMessageType.InventorySnapshot, OnInventorySnapshot);
            _router.Unregister(NetworkMessageType.Equipment, OnEquipment);
            _router.Unregister(NetworkMessageType.TimeSync, OnTimeSync);
        }

        private void OnBlockChanged(byte[] payload)
        {
            if (GameplayMessages.TryReadBlockChange(payload, out BlockChange change))
                Apply(change);
        }

        private void OnWorldEdits(byte[] payload)
        {
            var changes = new List<BlockChange>();

            if (!GameplayMessages.TryReadEdits(payload, changes))
                return;

            foreach (BlockChange change in changes)
                Apply(change);
        }

        private void Apply(BlockChange change)
        {
            if (SharesWorldWithServer)
                return;

            IGameplayWorld world = _world();

            if (world == null || !world.IsReady || _buffer.Count > 0)
            {
                _buffer.Add(change);
                return;
            }

            world.SetBlock(change.Position, change.Block);
        }

        private void OnInventorySnapshot(byte[] payload)
        {
            if (!GameplayMessages.TryReadInventory(
                    payload,
                    out uint acknowledged,
                    out int hotbarSize,
                    out ItemStack[] slots,
                    out ItemStack held))
            {
                return;
            }

            HasSnapshot = true;
            LastAcknowledged = acknowledged;
            LastHotbarSize = hotbarSize;
            LastSlots = slots;
            LastHeld = held;

            InventorySnapshotReceived?.Invoke(acknowledged, hotbarSize, slots, held);
        }
    }
}
