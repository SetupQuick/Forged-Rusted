using System;

namespace Networking
{
    public sealed class ClientNetworkModule
        : INetworkModule
    {
        public int TickOrder => 0;
        public int DisposeOrder => 0;
        private const int MaximumTicksPerFrame = 5;
        private const double MaximumFrameTime = 0.25;

        private readonly GameClient _client;
        private readonly ClientMessageRouter _router;

        private readonly ClientWorldReplication
            _worldReplication;

        private readonly ClientPlayerMovement
            _playerMovement;

        private double _accumulator;
        private bool _disposed;

        public ClientNetworkModule(
            GameClient client,
            ClientMessageRouter router,
            ClientWorldReplication worldReplication,
            ClientPlayerMovement playerMovement)
        {
            _client = client ??
                throw new ArgumentNullException(
                    nameof(client));

            _router = router ??
                throw new ArgumentNullException(
                    nameof(router));

            _worldReplication = worldReplication ??
                throw new ArgumentNullException(
                    nameof(worldReplication));

            _playerMovement = playerMovement ??
                throw new ArgumentNullException(
                    nameof(playerMovement));

            _client.MessageReceived +=
                OnMessageReceived;
        }

        public void Tick(
            double now,
            double deltaTime)
        {
            if (_disposed)
                return;

            // Receive packets first.
            _client.Update(now);

            float frameDelta =
                (float)Math.Min(
                    Math.Max(deltaTime, 0.0),
                    MaximumFrameTime);

            // Remote entities interpolate every render frame.
            _worldReplication.Interpolate(
                frameDelta);

            _accumulator += frameDelta;

            int ticks = 0;

            while (_accumulator >=
                       NetworkTime.TickInterval &&
                   ticks < MaximumTicksPerFrame)
            {
                // One input command = one network simulation tick.
                _playerMovement.Tick();

                _accumulator -=
                    NetworkTime.TickInterval;

                ticks++;
            }

            if (ticks ==
                    MaximumTicksPerFrame &&
                _accumulator >=
                    NetworkTime.TickInterval)
            {
                _accumulator = 0.0;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _client.MessageReceived -=
                OnMessageReceived;

            _playerMovement.Dispose();
            _worldReplication.Dispose();

            _router.Clear();

            _client.Dispose();
        }

        private void OnMessageReceived(
            NetworkMessageType type,
            byte[] payload)
        {
            _router.Dispatch(
                type,
                payload);
        }
    }
}