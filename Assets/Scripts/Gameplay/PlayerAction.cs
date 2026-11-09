using UnityEngine;
using World.Blocks;

namespace Gameplay
{
    public enum PlayerActionType : byte
    {
        InventoryClick = 1,
        InventoryClose = 2,
        BreakBlock = 3,
        PlaceBlock = 4
    }

    /// <summary>
    /// Something a player asks the server to do. Clients apply the same
    /// action locally right away (prediction) and the server's inventory
    /// snapshot later confirms or corrects it.
    /// </summary>
    public struct PlayerAction
    {
        public uint Sequence;
        public PlayerActionType Type;

        /// <summary>Inventory slot (click) or hotbar slot (place).</summary>
        public byte Slot;

        /// <summary>0 = left, 1 = right mouse button.</summary>
        public byte Button;

        public bool Shift;

        /// <summary>Voxel position for break/place.</summary>
        public Vector3Int Position;

        /// <summary>
        /// Client-side only, not sent: the block that was broken or placed
        /// when the action was predicted, for re-applying it after a
        /// server correction.
        /// </summary>
        public BlockId PredictedBlock;

        public static PlayerAction Click(int slot, int button, bool shift) => new PlayerAction
        {
            Type = PlayerActionType.InventoryClick,
            Slot = (byte)slot,
            Button = (byte)button,
            Shift = shift
        };

        public static PlayerAction Close() => new PlayerAction { Type = PlayerActionType.InventoryClose };

        public static PlayerAction Break(Vector3Int position) => new PlayerAction
        {
            Type = PlayerActionType.BreakBlock,
            Position = position
        };

        public static PlayerAction Place(Vector3Int position, int hotbarSlot) => new PlayerAction
        {
            Type = PlayerActionType.PlaceBlock,
            Position = position,
            Slot = (byte)hotbarSlot
        };
    }
}
