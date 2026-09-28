namespace Networking
{
    public enum TransportEventType
    {
        Data,
        Error
    }

    public readonly struct TransportEvent
    {
        public TransportEventType Type { get; }
        public ITransportHandle Remote { get; }
        public byte[] Data { get; }
        public string Error { get; }

        private TransportEvent(
            TransportEventType type,
            ITransportHandle remote,
            byte[] data,
            string error)
        {
            Type = type;
            Remote = remote;
            Data = data;
            Error = error;
        }

        public static TransportEvent DataReceived(
            ITransportHandle remote,
            byte[] data)
        {
            return new TransportEvent(
                TransportEventType.Data,
                remote,
                data,
                null);
        }

        public static TransportEvent Failed(string error)
        {
            return new TransportEvent(
                TransportEventType.Error,
                null,
                null,
                error);
        }
    }
}