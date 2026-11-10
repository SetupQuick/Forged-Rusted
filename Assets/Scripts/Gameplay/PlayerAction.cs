using System;
using System.Collections.Generic;
using UnityEngine;
using World.Blocks;

namespace Gameplay
{
    public enum PlayerActionType : byte
    {
        InventoryClick = 1,
        InventoryClose = 2,
        BreakBlock = 3,
        PlaceBlock = 4,
        SelectSlot = 5,

        // Started hitting a block (the server times the break from here).
        StartBreak = 6,

        // Hit a creature (Target = its network id).
        Attack = 7,

        // Drop the stack in Slot on the ground (Button 1 = whole stack, 0 = one item).
        DropItem = 8,

        // Use the item in Slot (eat food).
        UseItem = 9,

        // Pick a class (Slot = index in ClassDefinitions.All), once.
        ChooseClass = 10,

        // Unlock a skill (Slot = index in the class's skill list).
        UnlockSkill = 11,

        // Use a server-side ability (Slot = AbilityCode), aimed along Direction.
        UseAbility = 12,

        // Inventory screen (see ContainerLogic).
        InventorySpread = 13,   // Slots, Button 0 = evenly, 1 = one each
        InventoryCollect = 14,  // Slot (double-click)
        InventorySwap = 15,     // Slot with hotbar slot Button
        DropHeld = 16,          // the cursor stack on the ground (Button 1 = one item)
        OpenContainer = 17,     // Position of a chest
        CloseContainer = 18,
        ContainerCommand = 19   // Button = ContainerLogic.Command
    }

    /// <summary>Abilities triggered through UseAbility (movement ones go through the motor input).</summary>
    public enum AbilityCode : byte
    {
        Invisibility = 1,
        Fireball = 2
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

        /// <summary>Network id of the creature for Attack.</summary>
        public uint Target;

        /// <summary>Aim direction for UseAbility (normalised).</summary>
        public Vector3 Direction;

        /// <summary>Slots for InventorySpread.</summary>
        public byte[] Slots;

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

        public static PlayerAction StartBreaking(Vector3Int position) => new PlayerAction
        {
            Type = PlayerActionType.StartBreak,
            Position = position
        };

        public static PlayerAction AttackEntity(uint networkId) => new PlayerAction
        {
            Type = PlayerActionType.Attack,
            Target = networkId
        };

        public static PlayerAction Drop(int slot, bool wholeStack) => new PlayerAction
        {
            Type = PlayerActionType.DropItem,
            Slot = (byte)slot,
            Button = (byte)(wholeStack ? 1 : 0)
        };

        public static PlayerAction Use(int slot) => new PlayerAction
        {
            Type = PlayerActionType.UseItem,
            Slot = (byte)slot
        };

        public static PlayerAction PickClass(int classIndex) => new PlayerAction
        {
            Type = PlayerActionType.ChooseClass,
            Slot = (byte)classIndex
        };

        public static PlayerAction Unlock(int skillIndex) => new PlayerAction
        {
            Type = PlayerActionType.UnlockSkill,
            Slot = (byte)skillIndex
        };

        public static PlayerAction Ability(AbilityCode ability, Vector3 direction) => new PlayerAction
        {
            Type = PlayerActionType.UseAbility,
            Slot = (byte)ability,
            Direction = direction
        };

        public static PlayerAction Spread(IReadOnlyList<int> slots, bool oneEach)
        {
            var bytes = new byte[Math.Min(slots.Count, 64)];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = (byte)slots[i];

            return new PlayerAction { Type = PlayerActionType.InventorySpread, Slots = bytes, Button = (byte)(oneEach ? 1 : 0) };
        }

        public static PlayerAction CollectToCursor(int slot) => new PlayerAction { Type = PlayerActionType.InventoryCollect, Slot = (byte)slot };

        public static PlayerAction SwapWithHotbar(int slot, int hotbarIndex) =>
            new PlayerAction { Type = PlayerActionType.InventorySwap, Slot = (byte)slot, Button = (byte)hotbarIndex };

        public static PlayerAction DropCursor(bool oneItem) => new PlayerAction { Type = PlayerActionType.DropHeld, Button = (byte)(oneItem ? 1 : 0) };

        public static PlayerAction OpenChest(Vector3Int position) => new PlayerAction { Type = PlayerActionType.OpenContainer, Position = position };

        public static PlayerAction CloseChest() => new PlayerAction { Type = PlayerActionType.CloseContainer };

        public static PlayerAction ChestCommand(ContainerLogic.Command command) =>
            new PlayerAction { Type = PlayerActionType.ContainerCommand, Button = (byte)command };

        public static PlayerAction Select(int hotbarSlot) => new PlayerAction
        {
            Type = PlayerActionType.SelectSlot,
            Slot = (byte)hotbarSlot
        };

        public static PlayerAction Place(Vector3Int position, int hotbarSlot) => new PlayerAction
        {
            Type = PlayerActionType.PlaceBlock,
            Position = position,
            Slot = (byte)hotbarSlot
        };
    }
}
