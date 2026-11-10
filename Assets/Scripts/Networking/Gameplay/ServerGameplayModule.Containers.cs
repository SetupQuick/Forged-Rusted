using System.Collections.Generic;
using Gameplay;
using Items;
using UnityEngine;
using World.Blocks;

namespace Networking
{
    /// <summary>
    /// Chests: storage kept by the server per block position, opened by
    /// right-clicking (reach checked), shown to every player who has it
    /// open, closed when walking away or when it is broken (contents spill
    /// onto the ground), saved with the world.
    /// </summary>
    public sealed partial class ServerGameplayModule
    {
        public const float ChestReach = 6f;

        private readonly Dictionary<Vector3Int, InventoryContainer> _chests = new Dictionary<Vector3Int, InventoryContainer>();
        private readonly HashSet<Vector3Int> _dirtyChests = new HashSet<Vector3Int>();

        /// <summary>Chest contents (tests, saving).</summary>
        public InventoryContainer GetChest(Vector3Int position) =>
            _chests.TryGetValue(position, out InventoryContainer chest) ? chest : null;

        private InventoryContainer OpenChestOf(PlayerState state) =>
            state.ChestOpen && _chests.TryGetValue(state.ChestPosition, out InventoryContainer chest) ? chest : null;

        private static List<int> ToSlots(byte[] slots)
        {
            var list = new List<int>();
            if (slots != null)
                foreach (byte slot in slots)
                    list.Add(slot);
            return list;
        }

        private void TryOpenChest(PlayerState state, Vector3Int position)
        {
            if (state.Dead || !_world.IsReady || _world.GetBlock(position) != BlockId.Chest || !InChestReach(state, position))
                return;

            if (!_chests.TryGetValue(position, out InventoryContainer chest))
            {
                chest = new InventoryContainer(ContainerLogic.ChestSize, 0);
                _chests[position] = chest;
            }

            state.ChestOpen = true;
            state.ChestPosition = position;
            SendChest(state, chest);
        }

        private void CloseChest(PlayerState state, bool notify)
        {
            if (!state.ChestOpen)
                return;

            state.ChestOpen = false;

            if (notify && state.Peer.State == ServerPeerState.Connected)
            {
                Vector3Int position = state.ChestPosition;
                _server.Send(state.Peer, NetworkMessageType.ContainerSnapshot,
                    writer => GameplayMessages.WriteContainer(writer, false, position, null),
                    NetworkDelivery.ReliableOrdered);
            }
        }

        private void ChestChanged(PlayerState state)
        {
            if (state.ChestOpen)
            {
                _dirtyChests.Add(state.ChestPosition);
                PlayersVersion++; // saving: something changed
            }
        }

        private void TickChests()
        {
            foreach (PlayerState state in _players.Values)
            {
                if (!state.ChestOpen)
                    continue;

                // Walked away, died, or the chest is gone.
                if (state.Dead || !_chests.ContainsKey(state.ChestPosition) ||
                    _world.GetBlock(state.ChestPosition) != BlockId.Chest || !InChestReach(state, state.ChestPosition))
                {
                    CloseChest(state, notify: true);
                }
            }

            if (_dirtyChests.Count == 0)
                return;

            foreach (Vector3Int position in _dirtyChests)
            {
                if (!_chests.TryGetValue(position, out InventoryContainer chest))
                    continue;

                foreach (PlayerState state in _players.Values)
                    if (state.ChestOpen && state.ChestPosition == position)
                        SendChest(state, chest);
            }

            _dirtyChests.Clear();
        }

        private void SendChest(PlayerState state, InventoryContainer chest)
        {
            if (state.Peer.State != ServerPeerState.Connected)
                return;

            Vector3Int position = state.ChestPosition;
            ItemStack[] slots = chest.Snapshot();
            _server.Send(state.Peer, NetworkMessageType.ContainerSnapshot,
                writer => GameplayMessages.WriteContainer(writer, true, position, slots),
                NetworkDelivery.ReliableOrdered);
        }

        private bool InChestReach(PlayerState state, Vector3Int voxel)
        {
            if (!_world.TryGetPlayerEye(state.Peer.Id, out Vector3 eye))
                return false;

            float size = _world.VoxelSize;
            var center = new Vector3((voxel.x + 0.5f) * size, (voxel.y + 0.5f) * size, (voxel.z + 0.5f) * size);
            return (center - eye).sqrMagnitude <= ChestReach * ChestReach;
        }

        /// <summary>A broken chest drops everything on the ground.</summary>
        private void SpillChest(Vector3Int position)
        {
            if (!_chests.TryGetValue(position, out InventoryContainer chest))
                return;

            _chests.Remove(position);
            PlayersVersion++;

            if (_entities == null)
                return;

            float size = _world.VoxelSize;
            var center = new Vector3((position.x + 0.5f) * size, (position.y + 0.5f) * size, (position.z + 0.5f) * size);
            var random = new System.Random(position.GetHashCode());

            for (int i = 0; i < chest.Count; i++)
            {
                ItemStack stack = chest[i];
                if (stack.IsEmpty)
                    continue;

                var velocity = new Vector3((float)(random.NextDouble() - 0.5) * 3f, 3f, (float)(random.NextDouble() - 0.5) * 3f);
                _entities.SpawnItem(stack.Item.Id, stack.Count, center, velocity, 0, 0.5f);
            }
        }

        private void TryDropHeld(PlayerState state, bool oneItem)
        {
            if (state.Dead || state.Held.IsEmpty || _entities == null)
                return;

            if (!_world.TryGetPlayerEye(state.Peer.Id, out Vector3 eye) ||
                !_world.TryGetPlayerBody(state.Peer.Id, out _, out float yaw, out _, out _, out _))
            {
                return;
            }

            int count = oneItem ? 1 : state.Held.Count;
            ItemData item = state.Held.Item;
            state.Held = state.Held.WithCount(state.Held.Count - count);
            state.InventoryDirty = true;
            PlayersVersion++;

            Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            _entities.SpawnItem(item.Id, count, eye - Vector3.up * 0.35f + forward * 0.5f, forward * 4f + Vector3.up * 2f, state.Peer.Id, 1.5f);
        }

        // ------------------------------------------------------------
        // Saving
        // ------------------------------------------------------------

        public Dictionary<Vector3Int, List<(string ItemId, int Count)>> ExportChests()
        {
            var result = new Dictionary<Vector3Int, List<(string, int)>>();

            foreach (KeyValuePair<Vector3Int, InventoryContainer> chest in _chests)
            {
                var slots = new List<(string, int)>();
                for (int i = 0; i < chest.Value.Count; i++)
                {
                    ItemStack stack = chest.Value[i];
                    slots.Add(stack.IsEmpty ? (null, 0) : (stack.Item.Id, stack.Count));
                }

                result[chest.Key] = slots;
            }

            return result;
        }

        public void ImportChests(Dictionary<Vector3Int, List<(string ItemId, int Count)>> chests)
        {
            if (chests == null)
                return;

            foreach (KeyValuePair<Vector3Int, List<(string ItemId, int Count)>> saved in chests)
            {
                var chest = new InventoryContainer(ContainerLogic.ChestSize, 0);
                for (int i = 0; i < chest.Count && i < saved.Value.Count; i++)
                {
                    (string itemId, int count) = saved.Value[i];
                    if (itemId != null && count > 0 && ItemDatabase.TryGet(itemId, out ItemData item))
                        chest.Set(i, new ItemStack(item, System.Math.Min(count, System.Math.Max(1, item.maxStackSize))));
                }

                _chests[saved.Key] = chest;
            }
        }
    }
}
