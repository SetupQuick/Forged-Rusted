using System;
using System.Collections.Generic;
using UnityEngine;
using World.Blocks;

namespace Networking
{
    /// <summary>
    /// What the gameplay modules need from the world and the players.
    /// The game uses <see cref="WorldGameplayAccess"/>; tests can supply
    /// a fake.
    /// </summary>
    public interface IGameplayWorld
    {
        bool IsReady { get; }

        float VoxelSize { get; }

        /// <summary>Block at a voxel (generates the chunk if needed).</summary>
        BlockId GetBlock(Vector3Int voxel);

        void SetBlock(Vector3Int voxel, BlockId block);

        /// <summary>Raised after any block change (voxel, new block).</summary>
        event Action<Vector3Int, BlockId> BlockChanged;

        List<KeyValuePair<Vector3Int, BlockId>> GetAllEdits();

        /// <summary>Eye position (metres) of the player a peer controls.</summary>
        bool TryGetPlayerEye(uint peerId, out Vector3 eye);

        /// <summary>True if any player's body overlaps the box (metres).</summary>
        bool OverlapsAnyPlayer(Bounds bounds);

        /// <summary>Feet position, facing and movement state of a player's body.</summary>
        bool TryGetPlayerBody(uint peerId, out Vector3 feet, out float yaw, out float verticalVelocity, out bool grounded, out bool inWater);

        /// <summary>Moves a player (respawn). The client follows through movement reconciliation.</summary>
        void TeleportPlayer(uint peerId, Vector3 feet);

        /// <summary>Where players (re)spawn, on solid ground.</summary>
        Vector3 SpawnPoint { get; }

        /// <summary>Night time (more monsters).</summary>
        bool IsNight { get; }

        /// <summary>Creates the network object for a creature or dropped item; returns its id (0 = failed).</summary>
        uint SpawnEntity(Gameplay.Entities.ServerEntity entity);

        /// <summary>Copies an entity's position and state to its network object.</summary>
        void UpdateEntity(Gameplay.Entities.ServerEntity entity);

        void DespawnEntity(uint id);

        /// <summary>Which movement abilities (dash, double jump, hook) a player's motor allows.</summary>
        void SetMovementAbilities(uint peerId, CharacterMotor.MovementAbilities abilities);
    }
}
