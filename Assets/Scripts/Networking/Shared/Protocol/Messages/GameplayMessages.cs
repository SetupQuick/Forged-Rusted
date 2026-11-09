using System.Collections.Generic;
using Gameplay;
using Items;
using UnityEngine;
using World.Blocks;

namespace Networking
{
    /// <summary>One changed block: voxel position and new block.</summary>
    public readonly struct BlockChange
    {
        public const int Size = sizeof(int) + sizeof(short) + sizeof(int) + sizeof(byte);

        public readonly Vector3Int Position;
        public readonly BlockId Block;

        public BlockChange(Vector3Int position, BlockId block)
        {
            Position = position;
            Block = block;
        }

        public void Write(PacketWriter writer)
        {
            writer.Write(Position.x);
            writer.Write((short)Position.y);
            writer.Write(Position.z);
            writer.Write((byte)Block);
        }

        public static BlockChange Read(PacketReader reader)
        {
            int x = reader.ReadInt32();
            int y = reader.ReadInt16();
            int z = reader.ReadInt32();
            var block = (BlockId)reader.ReadByte();

            return new BlockChange(new Vector3Int(x, y, z), block);
        }
    }

    /// <summary>
    /// Wire format of the gameplay messages (player actions, block
    /// changes, inventory snapshots). Readers return false on malformed
    /// input instead of throwing.
    /// </summary>
    public static class GameplayMessages
    {
        /// <summary>Block changes per WorldEdits message (fits one datagram).</summary>
        public const int EditsPerMessage = 100;

        // --------------------------------------------------------------
        // PlayerAction (client -> server)
        // --------------------------------------------------------------

        public static void WriteAction(PacketWriter writer, PlayerAction action)
        {
            writer.Write(action.Sequence);
            writer.Write((byte)action.Type);

            switch (action.Type)
            {
                case PlayerActionType.InventoryClick:
                    writer.Write(action.Slot);
                    writer.Write(action.Button);
                    writer.Write(action.Shift);
                    break;

                case PlayerActionType.BreakBlock:
                    WritePosition(writer, action.Position);
                    break;

                case PlayerActionType.PlaceBlock:
                    WritePosition(writer, action.Position);
                    writer.Write(action.Slot);
                    break;
            }
        }

        public static bool TryReadAction(byte[] payload, out PlayerAction action)
        {
            action = default;

            try
            {
                var reader = new PacketReader(payload);

                action.Sequence = reader.ReadUInt32();
                action.Type = (PlayerActionType)reader.ReadByte();

                switch (action.Type)
                {
                    case PlayerActionType.InventoryClick:
                        action.Slot = reader.ReadByte();
                        action.Button = reader.ReadByte();
                        action.Shift = reader.ReadBool();
                        break;

                    case PlayerActionType.InventoryClose:
                        break;

                    case PlayerActionType.BreakBlock:
                        action.Position = ReadPosition(reader);
                        break;

                    case PlayerActionType.PlaceBlock:
                        action.Position = ReadPosition(reader);
                        action.Slot = reader.ReadByte();
                        break;

                    default:
                        return false;
                }

                return reader.Remaining == 0 && action.Sequence != 0;
            }
            catch (System.InvalidOperationException)
            {
                return false;
            }
        }

        // --------------------------------------------------------------
        // InventorySnapshot (server -> owner)
        // --------------------------------------------------------------

        public static void WriteInventory(
            PacketWriter writer,
            uint acknowledgedSequence,
            InventoryContainer container,
            ItemStack held)
        {
            writer.Write(acknowledgedSequence);
            writer.Write((byte)container.Count);
            writer.Write((byte)container.HotbarSize);

            for (int i = 0; i < container.Count; i++)
                WriteStack(writer, container[i]);

            WriteStack(writer, held);
        }

        public static bool TryReadInventory(
            byte[] payload,
            out uint acknowledgedSequence,
            out int hotbarSize,
            out ItemStack[] slots,
            out ItemStack held)
        {
            acknowledgedSequence = 0;
            hotbarSize = 0;
            slots = null;
            held = ItemStack.Empty;

            try
            {
                var reader = new PacketReader(payload);

                acknowledgedSequence = reader.ReadUInt32();
                int count = reader.ReadByte();
                hotbarSize = reader.ReadByte();

                slots = new ItemStack[count];

                for (int i = 0; i < count; i++)
                    slots[i] = ReadStack(reader);

                held = ReadStack(reader);

                return reader.Remaining == 0;
            }
            catch (System.InvalidOperationException)
            {
                return false;
            }
        }

        // --------------------------------------------------------------
        // BlockChanged / WorldEdits (server -> clients)
        // --------------------------------------------------------------

        public static void WriteEdits(PacketWriter writer, IReadOnlyList<BlockChange> changes, int start, int count)
        {
            writer.Write((ushort)count);

            for (int i = 0; i < count; i++)
                changes[start + i].Write(writer);
        }

        public static bool TryReadEdits(byte[] payload, List<BlockChange> output)
        {
            try
            {
                var reader = new PacketReader(payload);
                int count = reader.ReadUInt16();

                if (reader.Remaining != count * BlockChange.Size)
                    return false;

                for (int i = 0; i < count; i++)
                    output.Add(BlockChange.Read(reader));

                return true;
            }
            catch (System.InvalidOperationException)
            {
                return false;
            }
        }

        public static bool TryReadBlockChange(byte[] payload, out BlockChange change)
        {
            change = default;

            try
            {
                var reader = new PacketReader(payload);
                change = BlockChange.Read(reader);
                return reader.Remaining == 0;
            }
            catch (System.InvalidOperationException)
            {
                return false;
            }
        }

        private static void WriteStack(PacketWriter writer, ItemStack stack)
        {
            writer.Write(stack.IsEmpty ? (ushort)0 : ItemDatabase.GetNetworkId(stack.Item));
            writer.Write((ushort)(stack.IsEmpty ? 0 : stack.Count));
        }

        private static ItemStack ReadStack(PacketReader reader)
        {
            ushort id = reader.ReadUInt16();
            int count = reader.ReadUInt16();

            return new ItemStack(ItemDatabase.GetByNetworkId(id), count);
        }

        private static void WritePosition(PacketWriter writer, Vector3Int position)
        {
            writer.Write(position.x);
            writer.Write((short)position.y);
            writer.Write(position.z);
        }

        private static Vector3Int ReadPosition(PacketReader reader)
        {
            int x = reader.ReadInt32();
            int y = reader.ReadInt16();
            int z = reader.ReadInt32();
            return new Vector3Int(x, y, z);
        }
    }
}
