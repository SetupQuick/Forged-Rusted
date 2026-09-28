using System;

namespace Networking
{
    public readonly struct PlayerInputBatchMessage
    {
        public const int MaximumCommands = 3;

        private const int HeaderSize =
            sizeof(uint) + sizeof(byte);

        public uint NetworkId { get; }

        public PlayerInputMessage[] Commands { get; }

        public PlayerInputBatchMessage(
            uint networkId,
            PlayerInputMessage[] commands)
        {
            if (networkId == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(networkId));
            }

            if (commands == null ||
                commands.Length == 0 ||
                commands.Length > MaximumCommands)
            {
                throw new ArgumentException(
                    $"An input batch must contain 1-{MaximumCommands} commands.",
                    nameof(commands));
            }

            for (int index = 0;
                 index < commands.Length;
                 index++)
            {
                if (commands[index].NetworkId != networkId ||
                    commands[index].InputSequence == 0)
                {
                    throw new ArgumentException(
                        "Every command must belong to the batch object " +
                        "and have a non-zero sequence.",
                        nameof(commands));
                }
            }

            NetworkId = networkId;
            Commands = commands;
        }

        public void Write(PacketWriter writer)
        {
            if (writer == null)
            {
                throw new ArgumentNullException(
                    nameof(writer));
            }

            writer.Write(NetworkId);
            writer.Write((byte)Commands.Length);

            for (int index = 0;
                 index < Commands.Length;
                 index++)
            {
                Commands[index].WriteCommand(writer);
            }
        }

        public static bool TryRead(
            byte[] data,
            out PlayerInputBatchMessage batch)
        {
            batch = default;

            if (data == null ||
                data.Length <
                    HeaderSize +
                    PlayerInputMessage.CommandSize)
            {
                return false;
            }

            try
            {
                var reader = new PacketReader(data);

                uint networkId = reader.ReadUInt32();
                byte commandCount = reader.ReadByte();

                if (networkId == 0 ||
                    commandCount == 0 ||
                    commandCount > MaximumCommands ||
                    reader.Remaining !=
                        commandCount *
                        PlayerInputMessage.CommandSize)
                {
                    return false;
                }

                var commands =
                    new PlayerInputMessage[commandCount];

                for (int index = 0;
                     index < commands.Length;
                     index++)
                {
                    if (!PlayerInputMessage.TryReadCommand(
                            reader,
                            networkId,
                            out commands[index]))
                    {
                        return false;
                    }
                }

                if (reader.Remaining != 0)
                    return false;

                batch = new PlayerInputBatchMessage(
                    networkId,
                    commands);

                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}
