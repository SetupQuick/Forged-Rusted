using System;
using System.Collections.Generic;
using UnityEngine;
using World;
using World.Blocks;

namespace Networking
{
    /// <summary>
    /// <see cref="IGameplayWorld"/> backed by the scene's WorldManager and
    /// the networked player objects. The WorldManager is looked up lazily
    /// because the network runtime starts before the world exists.
    /// </summary>
    public sealed class WorldGameplayAccess : IGameplayWorld
    {
        private readonly NetworkWorld _networkWorld;
        private WorldManager _subscribedWorld;
        private Action<Vector3Int, BlockId> _blockChanged;

        public WorldGameplayAccess(NetworkWorld networkWorld)
        {
            _networkWorld = networkWorld;
        }

        private WorldManager World
        {
            get
            {
                WorldManager world = WorldManager.Active;

                if (world != _subscribedWorld)
                {
                    if (_subscribedWorld != null)
                        _subscribedWorld.BlockChanged -= OnBlockChanged;

                    _subscribedWorld = world;

                    if (world != null)
                        world.BlockChanged += OnBlockChanged;
                }

                return world;
            }
        }

        public bool IsReady => World != null && World.IsReady;

        public float VoxelSize => World != null ? World.VoxelSize : 1f;

        public event Action<Vector3Int, BlockId> BlockChanged
        {
            add
            {
                _ = World; // make sure we are subscribed to the world
                _blockChanged += value;
            }
            remove => _blockChanged -= value;
        }

        /// <summary>Call regularly so a world created later is picked up.</summary>
        public void Refresh()
        {
            _ = World;
        }

        public BlockId GetBlock(Vector3Int voxel)
        {
            return IsReady ? World.GetBlock(voxel.x, voxel.y, voxel.z) : BlockId.Air;
        }

        public void SetBlock(Vector3Int voxel, BlockId block)
        {
            if (IsReady)
                World.SetBlock(voxel.x, voxel.y, voxel.z, block);
        }

        public List<KeyValuePair<Vector3Int, BlockId>> GetAllEdits()
        {
            return IsReady ? World.GetAllEdits() : new List<KeyValuePair<Vector3Int, BlockId>>();
        }

        public bool TryGetPlayerEye(uint peerId, out Vector3 eye)
        {
            eye = default;

            if (_networkWorld == null)
                return false;

            foreach (NetObject netObject in _networkWorld.Objects)
            {
                if (netObject == null || !netObject.IsSpawned || netObject.OwnerPeerId != peerId)
                    continue;

                if (!netObject.TryGetComponent(out CharacterController controller))
                    continue;

                Vector3 top = netObject.transform.position + controller.center + Vector3.up * (controller.height * 0.5f);
                eye = top - Vector3.up * 0.2f;
                return true;
            }

            return false;
        }

        public bool OverlapsAnyPlayer(Bounds bounds)
        {
            if (_networkWorld == null)
                return false;

            foreach (NetObject netObject in _networkWorld.Objects)
            {
                if (netObject == null || !netObject.IsSpawned || netObject.OwnerPeerId == 0)
                    continue;

                if (!netObject.TryGetComponent(out CharacterController controller))
                    continue;

                Vector3 center = netObject.transform.position + controller.center;
                var body = new Bounds(center, new Vector3(controller.radius * 2f, controller.height, controller.radius * 2f));

                if (body.Intersects(bounds))
                    return true;
            }

            return false;
        }

        private void OnBlockChanged(Vector3Int voxel, BlockId block)
        {
            _blockChanged?.Invoke(voxel, block);
        }
    }
}
