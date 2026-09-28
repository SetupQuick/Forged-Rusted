namespace Networking
{
    public readonly struct NetworkPacket
    {
        public bool IsAcknowledgement { get; }

        public NetworkDelivery Delivery { get; }

        public NetworkMessageType MessageType { get; }

        public uint Sequence { get; }

        public byte[] Payload { get; }

        private NetworkPacket(
            bool isAcknowledgement,
            NetworkDelivery delivery,
            NetworkMessageType messageType,
            uint sequence,
            byte[] payload)
        {
            IsAcknowledgement = isAcknowledgement;
            Delivery = delivery;
            MessageType = messageType;
            Sequence = sequence;
            Payload = payload;
        }

        public static NetworkPacket Message(
            NetworkDelivery delivery,
            NetworkMessageType messageType,
            uint sequence,
            byte[] payload)
        {
            return new NetworkPacket(
                false,
                delivery,
                messageType,
                sequence,
                payload);
        }

        public static NetworkPacket Acknowledgement(
            uint sequence)
        {
            return new NetworkPacket(
                true,
                default,
                default,
                sequence,
                null);
        }
    }
}