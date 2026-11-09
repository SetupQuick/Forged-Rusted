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
    }
}
