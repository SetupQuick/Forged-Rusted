using System;

namespace Networking
{
    public sealed class ServerNetworkModule
        : INetworkModule
    {
        public int TickOrder => 100;
        public int DisposeOrder => 100;
        private const int MaximumTicksPerFrame = 5;
        private const double MaximumFrameTime = 0.25;

        private readonly GameServer _server;
        private readonly ServerMessageRouter _router;

        private readonly ServerPlayerMovement
            _playerMovement;

        private readonly ServerWorldReplication
            _worldReplication;

        private readonly NetworkDiagnostics
            _diagnostics;

        private double _accumulator;
        private uint _serverTick;
        private bool _disposed;

        public ServerNetworkModule(
            GameServer server,
            ServerMessageRouter router,
            ServerPlayerMovement playerMovement,
            ServerWorldReplication worldReplication,
            NetworkDiagnostics diagnostics)
        {
            _server = server ??
                throw new ArgumentNullException(
                    nameof(server));

            _router = router ??
                throw new ArgumentNullException(
                    nameof(router));

            _playerMovement = playerMovement ??
                throw new ArgumentNullException(
                    nameof(playerMovement));

            _worldReplication = worldReplication ??
                throw new ArgumentNullException(
                    nameof(worldReplication));

            _diagnostics = diagnostics ??
                throw new ArgumentNullException(
                    nameof(diagnostics));

            _server.MessageReceived +=
                OnMessageReceived;
        }

        public void Tick(
            double now,
            double deltaTime)
        {
            if (_disposed)
                return;

            /*
             * Important:
             * receive packets before simulation.
             */
            _server.Update(now);

            _accumulator +=
                Math.Min(
                    Math.Max(deltaTime, 0.0),
                    MaximumFrameTime);

            int ticks = 0;

            while (_accumulator >=
                       NetworkTime.TickInterval &&
                   ticks < MaximumTicksPerFrame)
            {
                _serverTick++;

                _diagnostics.ServerTick =
                    _serverTick;

                /*
                 * ORDER MATTERS.
                 *
                 * 1. Simulate authoritative movement.
                 * 2. Capture/send resulting world state.
                 */
                _playerMovement.Tick(
                    _serverTick);

                _worldReplication.Tick(
                    _serverTick);

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

            _server.MessageReceived -=
                OnMessageReceived;

            _playerMovement.Dispose();
            _worldReplication.Dispose();

            _router.Clear();

            _server.Dispose();
        }

        private void OnMessageReceived(
            Peer peer,
            NetworkMessageType type,
            byte[] payload)
        {
            _router.Dispatch(
                peer,
                type,
                payload);
        }
    }
}