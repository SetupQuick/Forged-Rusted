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

        // ------------------------------------------------------------
        // Players
        // ------------------------------------------------------------

        private CharacterMotor FindMotor(uint peerId)
        {
            if (_networkWorld == null)
                return null;

            foreach (NetObject netObject in _networkWorld.Objects)
            {
                if (netObject != null && netObject.IsSpawned && netObject.OwnerPeerId == peerId &&
                    netObject.TryGetComponent(out CharacterMotor motor))
                {
                    return motor;
                }
            }

            return null;
        }

        public bool TryGetPlayerBody(uint peerId, out Vector3 feet, out float yaw, out float verticalVelocity, out bool grounded, out bool inWater)
        {
            feet = default;
            yaw = 0f;
            verticalVelocity = 0f;
            grounded = false;
            inWater = false;

            CharacterMotor motor = FindMotor(peerId);
            if (motor == null)
                return false;

            CharacterMotorState state = motor.CaptureState();
            feet = state.Position;
            yaw = state.Rotation.eulerAngles.y;
            verticalVelocity = state.VerticalVelocity;
            grounded = motor.WasGrounded;
            inWater = motor.IsSwimming;
            return true;
        }

        public void TeleportPlayer(uint peerId, Vector3 feet)
        {
            CharacterMotor motor = FindMotor(peerId);
            if (motor == null)
                return;

            CharacterMotorState state = motor.CaptureState();
            // Let go of a grappling hook (it would pull back to the old spot).
            MovementAbilityState abilities = state.AbilityState;
            abilities.Grappling = false;
            motor.RestoreState(new CharacterMotorState(feet, state.Rotation, 0f, state.ControllerHeight, abilities));
        }

        public Vector3 SpawnPoint =>
            World != null && World.IsReady ? World.FindSpawnPosition(Vector3.zero) + Vector3.up * 0.1f : Vector3.up * 60f;

        public bool IsNight
        {
            get
            {
                float t = global::World.WorldClock.TimeOfDay;
                return t < 0.23f || t > 0.77f;
            }
        }

        // ------------------------------------------------------------
        // Creatures and dropped items
        // ------------------------------------------------------------

        private readonly Dictionary<uint, NetObject> _entityObjects = new Dictionary<uint, NetObject>();

        public uint SpawnEntity(Gameplay.Entities.ServerEntity entity)
        {
            if (_networkWorld == null)
                return 0;

            Gameplay.Entities.EntityPrefabs.Register(_networkWorld);

            NetObject netObject = _networkWorld.SpawnAuthoritative(
                Gameplay.Entities.EntityPrefabs.Template,
                entity.Position,
                Quaternion.Euler(0f, entity.Yaw, 0f));

            _entityObjects[netObject.NetworkId] = netObject;
            entity.Id = netObject.NetworkId;
            entity.StateDirty = true;
            UpdateEntity(entity);
            return netObject.NetworkId;
        }

        public void UpdateEntity(Gameplay.Entities.ServerEntity entity)
        {
            if (!_entityObjects.TryGetValue(entity.Id, out NetObject netObject) || netObject == null)
                return;

            netObject.transform.SetPositionAndRotation(entity.Position, Quaternion.Euler(0f, entity.Yaw, 0f));

            if (entity.StateDirty && netObject.TryGetComponent(out Gameplay.Entities.EntityState state))
            {
                ushort item = 0;
                if (entity.ItemId != null && Items.ItemDatabase.TryGet(entity.ItemId, out Items.ItemData data))
                    item = Items.ItemDatabase.GetNetworkId(data);

                state.Set(entity.Kind, Mathf.Max(0f, entity.Health), entity.Definition.MaxHealth, item, entity.Count);
                entity.StateDirty = false;
            }
        }

        public void DespawnEntity(uint id)
        {
            if (_networkWorld == null)
                return;

            _entityObjects.Remove(id);
            _networkWorld.Despawn(id);
        }

        public void SetMovementAbilities(uint peerId, CharacterMotor.MovementAbilities abilities)
        {
            CharacterMotor motor = FindMotor(peerId);
            if (motor != null)
                motor.Abilities = abilities;
        }
    }
}
