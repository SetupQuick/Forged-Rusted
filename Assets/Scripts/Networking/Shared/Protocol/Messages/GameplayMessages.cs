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
                case PlayerActionType.StartBreak:
                    WritePosition(writer, action.Position);
                    break;

                case PlayerActionType.PlaceBlock:
                    WritePosition(writer, action.Position);
                    writer.Write(action.Slot);
                    break;

                case PlayerActionType.SelectSlot:
                case PlayerActionType.UseItem:
                case PlayerActionType.ChooseClass:
                case PlayerActionType.UnlockSkill:
                    writer.Write(action.Slot);
                    break;

                case PlayerActionType.UseAbility:
                    writer.Write(action.Slot);
                    writer.Write(action.Direction.x);
                    writer.Write(action.Direction.y);
                    writer.Write(action.Direction.z);
                    break;

                case PlayerActionType.Attack:
                    writer.Write(action.Target);
                    break;

                case PlayerActionType.InventorySpread:
                    writer.Write(action.Button);
                    writer.Write((byte)(action.Slots?.Length ?? 0));
                    if (action.Slots != null)
                        foreach (byte slot in action.Slots)
                            writer.Write(slot);
                    break;

                case PlayerActionType.InventoryCollect:
                    writer.Write(action.Slot);
                    break;

                case PlayerActionType.InventorySwap:
                    writer.Write(action.Slot);
                    writer.Write(action.Button);
                    break;

                case PlayerActionType.DropHeld:
                case PlayerActionType.ContainerCommand:
                    writer.Write(action.Button);
                    break;

                case PlayerActionType.OpenContainer:
                    WritePosition(writer, action.Position);
                    break;

                case PlayerActionType.CloseContainer:
                    break;

                case PlayerActionType.DropItem:
                    writer.Write(action.Slot);
                    writer.Write(action.Button);
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
                    case PlayerActionType.StartBreak:
                        action.Position = ReadPosition(reader);
                        break;

                    case PlayerActionType.PlaceBlock:
                        action.Position = ReadPosition(reader);
                        action.Slot = reader.ReadByte();
                        break;

                    case PlayerActionType.SelectSlot:
                    case PlayerActionType.UseItem:
                    case PlayerActionType.ChooseClass:
                    case PlayerActionType.UnlockSkill:
                        action.Slot = reader.ReadByte();
                        break;

                    case PlayerActionType.UseAbility:
                        action.Slot = reader.ReadByte();
                        action.Direction = new Vector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat());
                        if (float.IsNaN(action.Direction.x) || float.IsNaN(action.Direction.y) || float.IsNaN(action.Direction.z))
                            return false;
                        break;

                    case PlayerActionType.Attack:
                        action.Target = reader.ReadUInt32();
                        break;

                    case PlayerActionType.InventorySpread:
                    {
                        action.Button = reader.ReadByte();
                        int count = reader.ReadByte();
                        if (count > 64)
                            return false;
                        action.Slots = new byte[count];
                        for (int i = 0; i < count; i++)
                            action.Slots[i] = reader.ReadByte();
                        break;
                    }

                    case PlayerActionType.InventoryCollect:
                        action.Slot = reader.ReadByte();
                        break;

                    case PlayerActionType.InventorySwap:
                        action.Slot = reader.ReadByte();
                        action.Button = reader.ReadByte();
                        break;

                    case PlayerActionType.DropHeld:
                    case PlayerActionType.ContainerCommand:
                        action.Button = reader.ReadByte();
                        break;

                    case PlayerActionType.OpenContainer:
                        action.Position = ReadPosition(reader);
                        break;

                    case PlayerActionType.CloseContainer:
                        break;

                    case PlayerActionType.DropItem:
                        action.Slot = reader.ReadByte();
                        action.Button = reader.ReadByte();
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
            ItemStack held,
            InventoryContainer chest = null,
            Vector3Int chestPosition = default)
        {
            writer.Write(acknowledgedSequence);
            writer.Write((byte)container.Count);
            writer.Write((byte)container.HotbarSize);

            for (int i = 0; i < container.Count; i++)
                WriteStack(writer, container[i]);

            WriteStack(writer, held);

            // The open chest, at the same acknowledged sequence, so the
            // client can reconcile its predicted chest clicks too.
            writer.Write(chest != null);
            if (chest == null)
                return;

            WritePosition(writer, chestPosition);
            writer.Write((byte)chest.Count);
            for (int i = 0; i < chest.Count; i++)
                WriteStack(writer, chest[i]);
        }

        public static bool TryReadInventory(
            byte[] payload,
            out uint acknowledgedSequence,
            out int hotbarSize,
            out ItemStack[] slots,
            out ItemStack held)
        {
            return TryReadInventory(payload, out acknowledgedSequence, out hotbarSize, out slots, out held, out _, out _);
        }

        /// <param name="chestSlots">The open chest's contents, or null when no chest is open.</param>
        public static bool TryReadInventory(
            byte[] payload,
            out uint acknowledgedSequence,
            out int hotbarSize,
            out ItemStack[] slots,
            out ItemStack held,
            out ItemStack[] chestSlots,
            out Vector3Int chestPosition)
        {
            acknowledgedSequence = 0;
            hotbarSize = 0;
            slots = null;
            held = ItemStack.Empty;
            chestSlots = null;
            chestPosition = default;

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

                if (reader.ReadBool())
                {
                    chestPosition = ReadPosition(reader);
                    int chestCount = reader.ReadByte();
                    chestSlots = new ItemStack[chestCount];
                    for (int i = 0; i < chestCount; i++)
                        chestSlots[i] = ReadStack(reader);
                }

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

        // --------------------------------------------------------------
        // Equipment (server -> clients)
        // --------------------------------------------------------------

        public static void WriteEquipment(PacketWriter writer, uint peerId, ItemData held)
        {
            writer.Write(peerId);
            writer.Write(ItemDatabase.GetNetworkId(held));
        }

        public static bool TryReadEquipment(byte[] payload, out uint peerId, out ItemData held)
        {
            peerId = 0;
            held = null;

            try
            {
                var reader = new PacketReader(payload);
                peerId = reader.ReadUInt32();
                held = ItemDatabase.GetByNetworkId(reader.ReadUInt16());
                return reader.Remaining == 0;
            }
            catch (System.InvalidOperationException)
            {
                return false;
            }
        }

        // --------------------------------------------------------------
        // Time of day (server -> clients)
        // --------------------------------------------------------------

        public static void WriteTimeSync(PacketWriter writer, double worldSeconds, float dayLengthSeconds, float startTimeOfDay)
        {
            writer.Write(worldSeconds);
            writer.Write(dayLengthSeconds);
            writer.Write(startTimeOfDay);
        }

        public static bool TryReadTimeSync(byte[] payload, out double worldSeconds, out float dayLengthSeconds, out float startTimeOfDay)
        {
            worldSeconds = 0;
            dayLengthSeconds = 0;
            startTimeOfDay = 0;

            try
            {
                var reader = new PacketReader(payload);
                worldSeconds = reader.ReadDouble();
                dayLengthSeconds = reader.ReadFloat();
                startTimeOfDay = reader.ReadFloat();

                return reader.Remaining == 0 &&
                       !double.IsNaN(worldSeconds) && !double.IsInfinity(worldSeconds) && worldSeconds >= 0 &&
                       dayLengthSeconds >= 10f && dayLengthSeconds < 1e6f &&
                       startTimeOfDay >= 0f && startTimeOfDay <= 1f;
            }
            catch (System.InvalidOperationException)
            {
                return false;
            }
        }

        // --------------------------------------------------------------
        // Player status (server -> owner): health and more, when it changes
        // --------------------------------------------------------------

        public static void WriteStatus(PacketWriter writer, PlayerStatus status)
        {
            writer.Write(status.Health);
            writer.Write(status.MaxHealth);
            writer.Write(status.Dead);
            writer.Write(status.RespawnSeconds);
            writer.Write(status.Mana);
            writer.Write(status.MaxMana);
            writer.Write(status.Level);
            writer.Write(status.Experience);
            writer.Write(status.ExperienceToNext);
            writer.Write(status.SkillPoints);
            writer.Write(status.ClassId ?? string.Empty);
            writer.Write(status.UnlockedSkills ?? string.Empty);
            writer.Write(status.Invisible);
            writer.Write(status.QuestState ?? string.Empty);
        }

        public static bool TryReadStatus(byte[] payload, out PlayerStatus status)
        {
            status = default;

            try
            {
                var reader = new PacketReader(payload);
                status.Health = reader.ReadFloat();
                status.MaxHealth = reader.ReadFloat();
                status.Dead = reader.ReadBool();
                status.RespawnSeconds = reader.ReadFloat();
                status.Mana = reader.ReadFloat();
                status.MaxMana = reader.ReadFloat();
                status.Level = reader.ReadInt32();
                status.Experience = reader.ReadInt32();
                status.ExperienceToNext = reader.ReadInt32();
                status.SkillPoints = reader.ReadInt32();
                status.ClassId = reader.ReadString();
                status.UnlockedSkills = reader.ReadString();
                status.Invisible = reader.ReadBool();
                status.QuestState = reader.ReadString();
                return reader.Remaining == 0;
            }
            catch (System.InvalidOperationException)
            {
                return false;
            }
        }

        // --------------------------------------------------------------
        // Player profile (client -> server)
        // --------------------------------------------------------------

        public const int MaxPlayerIdLength = 64;
        public const int MaxPlayerNameLength = 24;

        public static void WriteProfile(PacketWriter writer, string playerId, string name)
        {
            writer.Write(Clip(playerId, MaxPlayerIdLength));
            writer.Write(Clip(name, MaxPlayerNameLength));
        }

        public static bool TryReadProfile(byte[] payload, out string playerId, out string name)
        {
            playerId = null;
            name = null;

            try
            {
                var reader = new PacketReader(payload);
                playerId = reader.ReadString();
                name = reader.ReadString();

                return reader.Remaining == 0 &&
                       !string.IsNullOrWhiteSpace(playerId) &&
                       playerId.Length <= MaxPlayerIdLength &&
                       name != null && name.Length <= MaxPlayerNameLength;
            }
            catch (System.InvalidOperationException)
            {
                return false;
            }
        }

        private static string Clip(string value, int length)
        {
            value = value ?? string.Empty;
            return value.Length > length ? value.Substring(0, length) : value;
        }

        // --------------------------------------------------------------
        // Container contents (server -> players viewing a chest)
        // --------------------------------------------------------------

        public static void WriteContainer(PacketWriter writer, bool open, Vector3Int position, ItemStack[] slots)
        {
            writer.Write(open);
            WritePosition(writer, position);
            writer.Write((byte)(slots?.Length ?? 0));
            if (slots != null)
                foreach (ItemStack stack in slots)
                    WriteStack(writer, stack);
        }

        public static bool TryReadContainer(byte[] payload, out bool open, out Vector3Int position, out ItemStack[] slots)
        {
            open = false;
            position = default;
            slots = null;

            try
            {
                var reader = new PacketReader(payload);
                open = reader.ReadBool();
                position = ReadPosition(reader);
                int count = reader.ReadByte();
                slots = new ItemStack[count];
                for (int i = 0; i < count; i++)
                    slots[i] = ReadStack(reader);
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
