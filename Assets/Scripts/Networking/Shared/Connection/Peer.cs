namespace Networking
{
    public enum ServerPeerState
    {
        AwaitingReady,
        Connected
    }

    public sealed class Peer
    {
        public uint Id { get; }
        public ulong ClientNonce { get; }
        public ulong SessionToken { get; }
        public ITransportHandle Handle { get; }

        public ServerPeerState State { get; set; }
        public double LastReceiveTime { get; set; }

        public Peer(
            uint id,
            ulong clientNonce,
            ulong sessionToken,
            ITransportHandle handle,
            double now)
        {
            Id = id;
            ClientNonce = clientNonce;
            SessionToken = sessionToken;
            Handle = handle;
            State = ServerPeerState.AwaitingReady;
            LastReceiveTime = now;
        }
    }
}