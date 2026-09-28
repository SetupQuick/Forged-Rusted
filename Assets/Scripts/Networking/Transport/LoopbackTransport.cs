using System;
using System.Collections.Generic;

namespace Networking
{
    public sealed class LoopbackTransport : INetworkTransport
    {
        private const int MaximumQueuedPackets = 1024;

        private readonly object _gate = new object();
        private readonly Queue<TransportEvent> _incoming =
            new Queue<TransportEvent>();

        private readonly LoopbackSide _side;
        private readonly ITransportHandle _localHandle;

        private LoopbackTransport _partner;
        private bool _running;
        private bool _stopped;

        private LoopbackTransport(LoopbackSide side)
        {
            _side = side;
            _localHandle = new LoopbackTransportHandle(side);
        }

        public bool IsRunning
        {
            get
            {
                lock (_gate)
                    return _running;
            }
        }

        public static void CreatePair(
            out LoopbackTransport serverTransport,
            out LoopbackTransport clientTransport)
        {
            serverTransport =
                new LoopbackTransport(LoopbackSide.Server);

            clientTransport =
                new LoopbackTransport(LoopbackSide.Client);

            serverTransport._partner = clientTransport;
            clientTransport._partner = serverTransport;
        }

        public void StartServer(ushort port)
        {
            Start(LoopbackSide.Server);
        }

        public ITransportHandle StartClient(
            string address,
            ushort port)
        {
            Start(LoopbackSide.Client);

            return _partner._localHandle;
        }

        public bool Send(
            ITransportHandle destination,
            byte[] data)
        {
            if (data == null ||
                data.Length == 0 ||
                data.Length > NetworkTransportLimits.MaximumDatagramSize ||
                !ReferenceEquals(
                    destination,
                    _partner._localHandle))
            {
                return false;
            }

            lock (_gate)
            {
                if (!_running)
                    return false;
            }

            return _partner.TryEnqueue(
                _localHandle,
                data);
        }

        public bool TryPollEvent(
            out TransportEvent transportEvent)
        {
            lock (_gate)
            {
                if (_incoming.Count == 0)
                {
                    transportEvent = default;
                    return false;
                }

                transportEvent = _incoming.Dequeue();
                return true;
            }
        }

        public void Stop()
        {
            lock (_gate)
            {
                if (_stopped)
                    return;

                _stopped = true;
                _running = false;
                _incoming.Clear();
            }
        }

        public void Dispose()
        {
            Stop();
        }

        private void Start(LoopbackSide requiredSide)
        {
            lock (_gate)
            {
                if (_side != requiredSide)
                {
                    throw new InvalidOperationException(
                        $"This is the {_side} half " +
                        "of a loopback pair.");
                }

                if (_running)
                {
                    throw new InvalidOperationException(
                        "Transport is already running.");
                }

                if (_stopped)
                {
                    throw new ObjectDisposedException(
                        nameof(LoopbackTransport));
                }

                if (_partner == null)
                {
                    throw new InvalidOperationException(
                        "Loopback transport has no partner.");
                }

                _running = true;
            }
        }

        private bool TryEnqueue(
            ITransportHandle source,
            byte[] data)
        {
            lock (_gate)
            {
                if (!_running ||
                    _incoming.Count >= MaximumQueuedPackets)
                {
                    return false;
                }

                var ownedData = new byte[data.Length];

                Buffer.BlockCopy(
                    data,
                    0,
                    ownedData,
                    0,
                    data.Length);

                _incoming.Enqueue(
                    TransportEvent.DataReceived(
                        source,
                        ownedData));

                return true;
            }
        }

        private enum LoopbackSide
        {
            Server,
            Client
        }

        private sealed class LoopbackTransportHandle
            : ITransportHandle
        {
            private readonly LoopbackSide _side;

            public LoopbackTransportHandle(
                LoopbackSide side)
            {
                _side = side;
            }

            public override string ToString()
            {
                return $"Loopback {_side}";
            }
        }
    }
}