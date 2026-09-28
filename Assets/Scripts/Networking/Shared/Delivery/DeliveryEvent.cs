namespace Networking
{
    public enum DeliveryEventType : byte
    {
        Message = 1,
        Error = 2
    }

    public readonly struct DeliveryEvent
    {
        public DeliveryEventType Type { get; }

        public ITransportHandle Remote { get; }

        public NetworkMessageType MessageType { get; }

        public byte[] Payload { get; }

        public string Error { get; }

        private DeliveryEvent(
            DeliveryEventType type,
            ITransportHandle remote,
            NetworkMessageType messageType,
            byte[] payload,
            string error)
        {
            Type = type;
            Remote = remote;
            MessageType = messageType;
            Payload = payload;
            Error = error;
        }

        public static DeliveryEvent MessageReceived(
            ITransportHandle remote,
            NetworkMessageType messageType,
            byte[] payload)
        {
            return new DeliveryEvent(
                DeliveryEventType.Message,
                remote,
                messageType,
                payload,
                null);
        }

        public static DeliveryEvent Failed(
            string error)
        {
            return new DeliveryEvent(
                DeliveryEventType.Error,
                null,
                default,
                null,
                error);
        }
    }
}