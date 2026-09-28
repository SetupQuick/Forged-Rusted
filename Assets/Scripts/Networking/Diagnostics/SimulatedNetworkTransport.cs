using System;
using System.Collections.Generic;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Networking
{
    public sealed class SimulatedNetworkTransport :
        INetworkTransport
    {
        private const int MaximumDelayedPackets = 4096;

        private readonly INetworkTransport _transport;
        private readonly NetworkSimulationSettings _settings;
        private readonly NetworkDiagnostics _diagnostics;
        private readonly List<DelayedDatagram> _outgoing =
            new List<DelayedDatagram>();
        private readonly Random _random = new Random();

        private bool _disposed;

        public bool IsRunning =>
            !_disposed && _transport.IsRunning;

        public SimulatedNetworkTransport(
            INetworkTransport transport,
            NetworkSimulationSettings settings,
            NetworkDiagnostics diagnostics)
        {
            _transport = transport ??
                throw new ArgumentNullException(
                    nameof(transport));

            _settings = settings ??
                throw new ArgumentNullException(
                    nameof(settings));

            _diagnostics = diagnostics ??
                throw new ArgumentNullException(
                    nameof(diagnostics));
        }

        public void StartServer(ushort port)
        {
            ThrowIfDisposed();
            _transport.StartServer(port);
        }

        public ITransportHandle StartClient(
            string address,
            ushort port)
        {
            ThrowIfDisposed();
            return _transport.StartClient(address, port);
        }

        public bool Send(
            ITransportHandle destination,
            byte[] data)
        {
            if (_disposed ||
                !_transport.IsRunning ||
                destination == null ||
                data == null ||
                data.Length == 0)
            {
                return false;
            }

            FlushOutgoing(GetTime());

            if (!_settings.Enabled)
                return SendImmediately(destination, data);

            if (Roll(_settings.PacketLossPercent))
            {
                _diagnostics.SimulatedPacketsDropped++;

                // A simulated network drop is still a successful
                // handoff from the delivery layer's point of view.
                return true;
            }

            if (_outgoing.Count >= MaximumDelayedPackets)
                return false;

            double delayMilliseconds =
                Math.Max(
                    0.0,
                    _settings.LatencyMilliseconds +
                    RandomRange(
                        -_settings.JitterMilliseconds,
                        _settings.JitterMilliseconds));

            if (Roll(_settings.ReorderingPercent))
            {
                delayMilliseconds +=
                    _settings.ReorderingDelayMilliseconds;

                _diagnostics.SimulatedPacketsReordered++;
            }

            if (delayMilliseconds <= 0.0)
                return SendImmediately(destination, data);

            var ownedData = new byte[data.Length];

            Buffer.BlockCopy(
                data,
                0,
                ownedData,
                0,
                data.Length);

            _outgoing.Add(
                new DelayedDatagram(
                    destination,
                    ownedData,
                    GetTime() +
                    (delayMilliseconds / 1000.0)));

            return true;
        }

        public bool TryPollEvent(
            out TransportEvent transportEvent)
        {
            FlushOutgoing(GetTime());

            if (!_transport.TryPollEvent(
                    out transportEvent))
            {
                return false;
            }

            if (transportEvent.Type ==
                    TransportEventType.Data &&
                transportEvent.Data != null)
            {
                _diagnostics.PacketsReceived++;
                _diagnostics.BytesReceived +=
                    (ulong)transportEvent.Data.Length;
            }

            return true;
        }

        public void Stop()
        {
            if (_disposed)
                return;

            _outgoing.Clear();
            _transport.Stop();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _outgoing.Clear();
            _transport.Dispose();
        }

        private void FlushOutgoing(double now)
        {
            int index = 0;

            while (index < _outgoing.Count)
            {
                DelayedDatagram item = _outgoing[index];

                if (item.ReleaseTime > now)
                {
                    index++;
                    continue;
                }

                SendImmediately(
                    item.Destination,
                    item.Data);

                _outgoing.RemoveAt(index);
            }
        }

        private bool SendImmediately(
            ITransportHandle destination,
            byte[] data)
        {
            if (!_transport.Send(destination, data))
                return false;

            _diagnostics.PacketsSent++;
            _diagnostics.BytesSent += (ulong)data.Length;
            return true;
        }

        private bool Roll(float percent)
        {
            return percent > 0f &&
                   _random.NextDouble() < percent / 100.0;
        }

        private float RandomRange(float minimum, float maximum)
        {
            return minimum +
                   ((float)_random.NextDouble() *
                    (maximum - minimum));
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(
                    nameof(SimulatedNetworkTransport));
            }
        }

        private static double GetTime()
        {
            return (double)Stopwatch.GetTimestamp() /
                   Stopwatch.Frequency;
        }

        private readonly struct DelayedDatagram
        {
            public ITransportHandle Destination { get; }
            public byte[] Data { get; }
            public double ReleaseTime { get; }

            public DelayedDatagram(
                ITransportHandle destination,
                byte[] data,
                double releaseTime)
            {
                Destination = destination;
                Data = data;
                ReleaseTime = releaseTime;
            }
        }
    }
}
