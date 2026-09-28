using System;

namespace Networking
{
    public static class NetworkProtocol
    {
        public const uint Magic = 0x46525354;

        // Incremented because the wire format has changed.
        public const ushort Version = 5;

        // The lowest two bits store NetworkDelivery.
        private const byte DeliveryMask = 0b00000011;

        // The highest bit distinguishes acknowledgements.
        private const byte AcknowledgementFlag = 0b10000000;

        private const byte AllowedFlags =
            DeliveryMask |
            AcknowledgementFlag;

        // Magic        = 4
        // Version      = 2
        // Flags        = 1
        // Message type = 1
        // Sequence     = 4
        //
        // Total        = 12 bytes
        public const int HeaderSize =
            sizeof(uint) +
            sizeof(ushort) +
            sizeof(byte) +
            sizeof(byte) +
            sizeof(uint);

        public const int MaximumPayloadSize =
            NetworkTransportLimits.MaximumDatagramSize -
            HeaderSize;

        public static byte[] EncodeMessage(
            NetworkMessageType messageType,
            NetworkDelivery delivery,
            uint sequence,
            byte[] payload)
        {
            if (!Enum.IsDefined(
                    typeof(NetworkMessageType),
                    messageType))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(messageType));
            }

            if (!Enum.IsDefined(
                    typeof(NetworkDelivery),
                    delivery))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(delivery));
            }

            payload ??= Array.Empty<byte>();

            if (payload.Length > MaximumPayloadSize)
            {
                throw new InvalidOperationException(
                    $"Payload exceeds " +
                    $"{MaximumPayloadSize} bytes.");
            }

            // Plain unreliable packets do not need a sequence.
            if (delivery == NetworkDelivery.Unreliable)
                sequence = 0;

            var writer =
                new PacketWriter(
                    HeaderSize +
                    payload.Length);

            writer.Write(Magic);
            writer.Write(Version);
            writer.Write((byte)delivery);
            writer.Write((byte)messageType);
            writer.Write(sequence);
            writer.Write(payload);

            return writer.ToArray();
        }

        public static byte[] EncodeAcknowledgement(
            uint sequence)
        {
            var writer =
                new PacketWriter(HeaderSize);

            writer.Write(Magic);
            writer.Write(Version);
            writer.Write(AcknowledgementFlag);

            // Acknowledgements have no message type.
            writer.Write((byte)0);

            writer.Write(sequence);

            return writer.ToArray();
        }

        public static bool TryDecode(
            byte[] datagram,
            out NetworkPacket packet)
        {
            packet = default;

            if (datagram == null ||
                datagram.Length < HeaderSize ||
                datagram.Length >
                    NetworkTransportLimits.MaximumDatagramSize)
            {
                return false;
            }

            try
            {
                var reader =
                    new PacketReader(datagram);

                uint magic =
                    reader.ReadUInt32();

                ushort version =
                    reader.ReadUInt16();

                byte flags =
                    reader.ReadByte();

                byte messageTypeValue =
                    reader.ReadByte();

                uint sequence =
                    reader.ReadUInt32();

                if (magic != Magic ||
                    version != Version)
                {
                    return false;
                }

                // Reject flags that this protocol version
                // does not understand.
                if ((flags & ~AllowedFlags) != 0)
                    return false;

                bool isAcknowledgement =
                    (flags &
                     AcknowledgementFlag) != 0;

                if (isAcknowledgement)
                {
                    // ACK packets must contain only:
                    // header + acknowledged sequence.
                    if (flags !=
                            AcknowledgementFlag ||
                        messageTypeValue != 0 ||
                        reader.Remaining != 0)
                    {
                        return false;
                    }

                    packet =
                        NetworkPacket
                            .Acknowledgement(
                                sequence);

                    return true;
                }

                NetworkDelivery delivery =
                    (NetworkDelivery)
                    (flags & DeliveryMask);

                if (!Enum.IsDefined(
                        typeof(NetworkDelivery),
                        delivery))
                {
                    return false;
                }

                var messageType =
                    (NetworkMessageType)
                    messageTypeValue;

                if (!Enum.IsDefined(
                        typeof(NetworkMessageType),
                        messageType))
                {
                    return false;
                }

                if (delivery ==
                        NetworkDelivery.Unreliable &&
                    sequence != 0)
                {
                    return false;
                }

                // UDP already preserves datagram boundaries,
                // so the remaining bytes are the payload.
                byte[] payload =
                    reader.ReadBytes(
                        reader.Remaining);

                packet =
                    NetworkPacket.Message(
                        delivery,
                        messageType,
                        sequence,
                        payload);

                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }
}
