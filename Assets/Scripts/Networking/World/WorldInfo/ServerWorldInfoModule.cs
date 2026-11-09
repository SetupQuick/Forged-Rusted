using System;

namespace Networking
{
    /// <summary>
    /// Server side of world info sync: sends the world seed to each
    /// client as soon as it connects, before any object spawns (spawns
    /// are sent later, during the replication tick, on the same
    /// reliable-ordered channel).
    /// </summary>
    public sealed class ServerWorldInfoModule
        : INetworkModule
    {
        // Tick does nothing; dispose before ServerNetworkModule (100)
        // disposes the GameServer we are subscribed to.
        public int TickOrder => 100;
        public int DisposeOrder => 90;

        private readonly GameServer _server;
        private readonly Func<WorldInfoMessage?> _getWorldInfo;
        private bool _disposed;

        public ServerWorldInfoModule(
            GameServer server,
            Func<WorldInfoMessage?> getWorldInfo)
        {
            _server = server ??
                throw new ArgumentNullException(
                    nameof(server));

            _getWorldInfo = getWorldInfo ??
                throw new ArgumentNullException(
                    nameof(getWorldInfo));

            _server.PeerConnected +=
                OnPeerConnected;
        }

        public void Tick(
            double now,
            double deltaTime)
        {
        }

        /// <summary>
        /// Sends the current world info to every connected peer, for
        /// when the world is (re)initialised after peers joined.
        /// </summary>
        public void Broadcast()
        {
            if (_disposed)
                return;

            foreach (Peer peer in _server.Peers)
            {
                if (peer.State ==
                    ServerPeerState.Connected)
                {
                    Send(peer);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _server.PeerConnected -=
                OnPeerConnected;
        }

        private void OnPeerConnected(
            Peer peer)
        {
            if (!Send(peer))
            {
                UnityEngine.Debug.LogWarning(
                    $"Peer {peer.Id} connected before the " +
                    "world was ready. It will receive the " +
                    "world seed once the world initialises.");
            }
        }

        private bool Send(
            Peer peer)
        {
            WorldInfoMessage? info =
                _getWorldInfo();

            if (!info.HasValue)
                return false;

            WorldInfoMessage message =
                info.Value;

            return _server.Send(
                peer,
                NetworkMessageType.WorldInfo,
                writer =>
                    message.Write(writer),
                NetworkDelivery.ReliableOrdered);
        }
    }
}
