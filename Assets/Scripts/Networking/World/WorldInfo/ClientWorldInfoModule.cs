using System;

namespace Networking
{
    /// <summary>
    /// Client side of world info sync: receives the world seed from
    /// the server and hands it to <see cref="NetworkRuntime"/>, which
    /// raises <see cref="NetworkRuntime.WorldInfoReceived"/>.
    /// </summary>
    public sealed class ClientWorldInfoModule
        : INetworkModule
    {
        // Tick does nothing; dispose before ClientNetworkModule (0)
        // clears the router.
        public int TickOrder => 0;
        public int DisposeOrder => -10;

        private readonly ClientMessageRouter _router;
        private readonly Action<WorldInfoMessage> _onReceived;
        private bool _disposed;

        public ClientWorldInfoModule(
            ClientMessageRouter router,
            Action<WorldInfoMessage> onReceived)
        {
            _router = router ??
                throw new ArgumentNullException(
                    nameof(router));

            _onReceived = onReceived ??
                throw new ArgumentNullException(
                    nameof(onReceived));

            _router.Register(
                NetworkMessageType.WorldInfo,
                OnWorldInfo);
        }

        public void Tick(
            double now,
            double deltaTime)
        {
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _router.Unregister(
                NetworkMessageType.WorldInfo,
                OnWorldInfo);
        }

        private void OnWorldInfo(
            byte[] payload)
        {
            if (payload == null)
                return;

            var reader =
                new PacketReader(payload);

            if (!WorldInfoMessage.TryRead(
                    reader,
                    out WorldInfoMessage message))
            {
                UnityEngine.Debug.LogWarning(
                    "Ignored malformed WorldInfo message.");

                return;
            }

            _onReceived(message);
        }
    }
}
