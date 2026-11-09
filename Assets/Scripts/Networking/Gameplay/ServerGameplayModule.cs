using System;
using System.Collections.Generic;
using Gameplay;
using Items;
using UnityEngine;
using World.Blocks;

namespace Networking
{
    /// <summary>
    /// Server-authoritative gameplay: owns every player's inventory,
    /// validates block breaking/placing, applies valid edits to the world
    /// and replicates them, and sends a joining player every earlier edit.
    ///
    /// Flow for one action:
    ///   client predicts + sends PlayerAction (reliable, ordered)
    ///   server validates and applies it to its copy of the inventory and
    ///   the world -> BlockChanged to everyone (or a correction to the
    ///   sender if rejected) -> InventorySnapshot to the sender with the
    ///   last processed action, which the client reconciles against.
    /// </summary>
    public sealed class ServerGameplayModule : INetworkModule
    {
        public int TickOrder => 110;
        public int DisposeOrder => 95;

        private readonly GameServer _server;
        private readonly ServerMessageRouter _router;
        private readonly IGameplayWorld _world;

        private readonly Dictionary<uint, PlayerState> _players = new Dictionary<uint, PlayerState>();
        private readonly List<BlockChange> _pendingBroadcast = new List<BlockChange>();
        private bool _subscribedToWorld;
        private bool _disposed;

        private sealed class PlayerState
        {
            public Peer Peer;
            public InventoryContainer Inventory;
            public ItemStack Held;
            public uint LastSequence;
            public bool InventoryDirty;

            // Selected hotbar slot and the item last told to everyone.
            public int SelectedSlot;
            public ushort BroadcastHeld = ushort.MaxValue;

            // Earlier world edits still to send to this player, in order.
            public List<BlockChange> EditBacklog;
            public int EditBacklogIndex;
        }

        /// <summary>Number of actions rejected (for diagnostics and tests).</summary>
        public int RejectedActions { get; private set; }

        public ServerGameplayModule(GameServer server, ServerMessageRouter router, IGameplayWorld world)
        {
            _server = server ?? throw new ArgumentNullException(nameof(server));
            _router = router ?? throw new ArgumentNullException(nameof(router));
            _world = world ?? throw new ArgumentNullException(nameof(world));

            _server.PeerConnected += OnPeerConnected;
            _server.PeerDisconnected += OnPeerDisconnected;
            _router.Register(NetworkMessageType.PlayerAction, OnPlayerAction);
        }

        /// <summary>Server copy of a player's inventory (tests, saving later).</summary>
        public InventoryContainer GetInventory(uint peerId)
        {
            return _players.TryGetValue(peerId, out PlayerState state) ? state.Inventory : null;
        }

        public void Tick(double now, double deltaTime)
        {
            if (_disposed)
                return;

            if (!_subscribedToWorld)
            {
                _world.BlockChanged += OnWorldBlockChanged;
                _subscribedToWorld = true;
            }

            if (_world is WorldGameplayAccess access)
                access.Refresh();

            FlushBroadcasts();

            foreach (PlayerState state in _players.Values)
            {
                SendEditBacklog(state);

                if (state.InventoryDirty)
                    SendInventory(state);

                BroadcastEquipmentIfChanged(state);
            }
        }

        /// <summary>Tells everyone when a player's held item changed.</summary>
        private void BroadcastEquipmentIfChanged(PlayerState state)
        {
            ItemStack held = state.Inventory[state.SelectedSlot];
            ushort id = held.IsEmpty ? (ushort)0 : ItemDatabase.GetNetworkId(held.Item);

            if (id == state.BroadcastHeld)
                return;

            ItemData item = held.IsEmpty ? null : held.Item;
            uint peerId = state.Peer.Id;
            bool all = true;

            foreach (PlayerState other in _players.Values)
            {
                if (other.Peer.State != ServerPeerState.Connected)
                    continue;

                all &= _server.Send(
                    other.Peer,
                    NetworkMessageType.Equipment,
                    writer => GameplayMessages.WriteEquipment(writer, peerId, item),
                    NetworkDelivery.ReliableOrdered);
            }

            if (all)
                state.BroadcastHeld = id;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _server.PeerConnected -= OnPeerConnected;
            _server.PeerDisconnected -= OnPeerDisconnected;
            _router.Unregister(NetworkMessageType.PlayerAction, OnPlayerAction);

            if (_subscribedToWorld)
                _world.BlockChanged -= OnWorldBlockChanged;

            _players.Clear();
        }

        // ------------------------------------------------------------
        // Connections
        // ------------------------------------------------------------

        private void OnPeerConnected(Peer peer)
        {
            InventoryContainer inventory = InventoryLayout.CreateContainer();
            InventoryLayout.AddStarterItems(inventory);

            var backlog = new List<BlockChange>();
            foreach (KeyValuePair<Vector3Int, BlockId> edit in _world.GetAllEdits())
                backlog.Add(new BlockChange(edit.Key, edit.Value));

            var state = new PlayerState
            {
                Peer = peer,
                Inventory = inventory,
                Held = ItemStack.Empty,
                InventoryDirty = true,
                EditBacklog = backlog
            };

            _players[peer.Id] = state;

            // Start right away so edits arrive right after the world seed.
            SendEditBacklog(state);
            SendInventory(state);

            // What everyone else is holding.
            foreach (PlayerState other in _players.Values)
            {
                if (other == state || other.BroadcastHeld == ushort.MaxValue)
                    continue;

                ItemData item = ItemDatabase.GetByNetworkId(other.BroadcastHeld);
                uint otherId = other.Peer.Id;

                _server.Send(
                    peer,
                    NetworkMessageType.Equipment,
                    writer => GameplayMessages.WriteEquipment(writer, otherId, item),
                    NetworkDelivery.ReliableOrdered);
            }
        }

        private void OnPeerDisconnected(uint peerId)
        {
            _players.Remove(peerId);
        }

        // ------------------------------------------------------------
        // Actions
        // ------------------------------------------------------------

        private void OnPlayerAction(Peer peer, byte[] payload)
        {
            if (!_players.TryGetValue(peer.Id, out PlayerState state))
                return;

            if (!GameplayMessages.TryReadAction(payload, out PlayerAction action))
                return;

            // Reliable-ordered delivery means sequences only grow; ignore
            // anything else (duplicates or a misbehaving client).
            if (action.Sequence <= state.LastSequence)
                return;

            state.LastSequence = action.Sequence;
            state.InventoryDirty = true; // always acknowledge

            switch (action.Type)
            {
                case PlayerActionType.InventoryClick:
                    InventoryLogic.Click(state.Inventory, ref state.Held, action.Slot, action.Button, action.Shift);
                    break;

                case PlayerActionType.InventoryClose:
                    InventoryLogic.Close(state.Inventory, ref state.Held);
                    break;

                case PlayerActionType.BreakBlock:
                    if (!TryBreak(state, action.Position))
                        Reject(state, action.Position);
                    break;

                case PlayerActionType.PlaceBlock:
                    if (!TryPlace(state, action.Position, action.Slot))
                        Reject(state, action.Position);
                    break;

                case PlayerActionType.SelectSlot:
                    if (action.Slot < state.Inventory.HotbarSize)
                        state.SelectedSlot = action.Slot;
                    break;
            }
        }

        private bool TryBreak(PlayerState state, Vector3Int voxel)
        {
            if (!_world.IsReady || !InReach(state, voxel))
                return false;

            BlockId block = _world.GetBlock(voxel);

            if (!BlockEditRules.CanBreak(block))
                return false;

            _world.SetBlock(voxel, BlockId.Air);
            InventoryLogic.AddDrop(state.Inventory, block);
            return true;
        }

        private bool TryPlace(PlayerState state, Vector3Int voxel, int slot)
        {
            if (!_world.IsReady || !InReach(state, voxel))
                return false;

            if (!InventoryLogic.TryGetPlaceableBlock(state.Inventory, slot, out BlockId block))
                return false;

            if (!BlockEditRules.CanPlaceInto(_world.GetBlock(voxel)))
                return false;

            if (!HasSupport(voxel))
                return false;

            float size = _world.VoxelSize;
            var bounds = new Bounds(
                new Vector3((voxel.x + 0.5f) * size, (voxel.y + 0.5f) * size, (voxel.z + 0.5f) * size),
                Vector3.one * (size * 0.98f));

            if (BlockRegistry.IsSolid(block) && _world.OverlapsAnyPlayer(bounds))
                return false;

            _world.SetBlock(voxel, block);
            InventoryLogic.ConsumeForPlace(state.Inventory, slot);
            return true;
        }

        /// <summary>Blocks must touch an existing block (no floating placement).</summary>
        private bool HasSupport(Vector3Int voxel)
        {
            Vector3Int[] directions =
            {
                new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0),
                new Vector3Int(0, 1, 0), new Vector3Int(0, -1, 0),
                new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1)
            };

            foreach (Vector3Int direction in directions)
            {
                if (!BlockEditRules.CanPlaceInto(_world.GetBlock(voxel + direction)))
                    return true;
            }

            return false;
        }

        private bool InReach(PlayerState state, Vector3Int voxel)
        {
            if (!_world.TryGetPlayerEye(state.Peer.Id, out Vector3 eye))
                return false;

            float size = _world.VoxelSize;
            var center = new Vector3((voxel.x + 0.5f) * size, (voxel.y + 0.5f) * size, (voxel.z + 0.5f) * size);
            float maxDistance = BlockEditRules.ReachMetres + BlockEditRules.ServerReachTolerance + size;

            return (center - eye).sqrMagnitude <= maxDistance * maxDistance;
        }

        /// <summary>Tells the sender the real block so its prediction is undone.</summary>
        private void Reject(PlayerState state, Vector3Int voxel)
        {
            RejectedActions++;

            BlockId actual = _world.IsReady ? _world.GetBlock(voxel) : BlockId.Air;
            var change = new BlockChange(voxel, actual);

            _server.Send(
                state.Peer,
                NetworkMessageType.BlockChanged,
                writer => change.Write(writer),
                NetworkDelivery.ReliableOrdered);
        }

        // ------------------------------------------------------------
        // Replication
        // ------------------------------------------------------------

        private void OnWorldBlockChanged(Vector3Int voxel, BlockId block)
        {
            _pendingBroadcast.Add(new BlockChange(voxel, block));

            // Send immediately so the change goes out before the
            // inventory snapshot that acknowledges the action.
            FlushBroadcasts();
        }

        private void FlushBroadcasts()
        {
            if (_pendingBroadcast.Count == 0)
                return;

            foreach (BlockChange change in _pendingBroadcast)
            {
                foreach (PlayerState state in _players.Values)
                {
                    if (state.Peer.State != ServerPeerState.Connected)
                        continue;

                    // A player still receiving the backlog gets it there
                    // (in order) instead.
                    if (state.EditBacklogIndex < state.EditBacklog.Count)
                    {
                        state.EditBacklog.Add(change);
                        continue;
                    }

                    BlockChange captured = change;
                    if (!_server.Send(state.Peer, NetworkMessageType.BlockChanged, writer => captured.Write(writer), NetworkDelivery.ReliableOrdered))
                        state.EditBacklog.Add(change); // send window full: retry next tick
                }
            }

            _pendingBroadcast.Clear();
        }

        private void SendEditBacklog(PlayerState state)
        {
            // Paced: the reliable channel has a limited window, so send
            // what fits each tick.
            while (state.EditBacklogIndex < state.EditBacklog.Count)
            {
                int start = state.EditBacklogIndex;
                int count = Math.Min(GameplayMessages.EditsPerMessage, state.EditBacklog.Count - start);
                List<BlockChange> backlog = state.EditBacklog;

                bool sent = _server.Send(
                    state.Peer,
                    NetworkMessageType.WorldEdits,
                    writer => GameplayMessages.WriteEdits(writer, backlog, start, count),
                    NetworkDelivery.ReliableOrdered);

                if (!sent)
                    return;

                state.EditBacklogIndex += count;
            }

            if (state.EditBacklog.Count > 0)
            {
                state.EditBacklog.Clear();
                state.EditBacklogIndex = 0;
            }
        }

        private void SendInventory(PlayerState state)
        {
            bool sent = _server.Send(
                state.Peer,
                NetworkMessageType.InventorySnapshot,
                writer => GameplayMessages.WriteInventory(writer, state.LastSequence, state.Inventory, state.Held),
                NetworkDelivery.ReliableOrdered);

            if (sent)
                state.InventoryDirty = false;
        }
    }
}
