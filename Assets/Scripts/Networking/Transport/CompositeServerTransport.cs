using System;

namespace Networking
{
    public sealed class CompositeServerTransport
        : INetworkTransport
    {
        private readonly INetworkTransport[] _transports;

        private int _nextPollIndex;
        private bool _started;
        private bool _stopped;

        public CompositeServerTransport(
            params INetworkTransport[] transports)
        {
            if (transports == null ||
                transports.Length == 0)
            {
                throw new ArgumentException(
                    "At least one transport is required.",
                    nameof(transports));
            }

            _transports =
                new INetworkTransport[transports.Length];

            for (int index = 0;
                 index < transports.Length;
                 index++)
            {
                _transports[index] =
                    transports[index] ??
                    throw new ArgumentException(
                        "A transport cannot be null.",
                        nameof(transports));
            }
        }

        public bool IsRunning
        {
            get
            {
                if (!_started || _stopped)
                    return false;

                for (int index = 0;
                     index < _transports.Length;
                     index++)
                {
                    if (_transports[index].IsRunning)
                        return true;
                }

                return false;
            }
        }

        public void StartServer(ushort port)
        {
            if (_started)
            {
                throw new InvalidOperationException(
                    "Transport is already running.");
            }

            if (_stopped)
            {
                throw new ObjectDisposedException(
                    nameof(CompositeServerTransport));
            }

            int startedCount = 0;

            try
            {
                for (;
                     startedCount < _transports.Length;
                     startedCount++)
                {
                    _transports[startedCount]
                        .StartServer(port);
                }

                _started = true;
            }
            catch
            {
                for (int index = startedCount - 1;
                     index >= 0;
                     index--)
                {
                    _transports[index].Stop();
                }

                _stopped = true;
                throw;
            }
        }

        public ITransportHandle StartClient(
            string address,
            ushort port)
        {
            throw new NotSupportedException(
                "CompositeServerTransport can only " +
                "be used by a server.");
        }

        public bool Send(
            ITransportHandle destination,
            byte[] data)
        {
            if (!_started || _stopped)
                return false;

            for (int index = 0;
                 index < _transports.Length;
                 index++)
            {
                if (_transports[index].Send(
                        destination,
                        data))
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryPollEvent(
            out TransportEvent transportEvent)
        {
            if (!_started || _stopped)
            {
                transportEvent = default;
                return false;
            }

            for (int offset = 0;
                 offset < _transports.Length;
                 offset++)
            {
                int index =
                    (_nextPollIndex + offset) %
                    _transports.Length;

                if (!_transports[index].TryPollEvent(
                        out transportEvent))
                {
                    continue;
                }

                _nextPollIndex =
                    (index + 1) % _transports.Length;

                return true;
            }

            transportEvent = default;
            return false;
        }

        public void Stop()
        {
            if (_stopped)
                return;

            _stopped = true;

            for (int index = 0;
                 index < _transports.Length;
                 index++)
            {
                _transports[index].Stop();
            }
        }

        public void Dispose()
        {
            Stop();

            for (int index = 0;
                 index < _transports.Length;
                 index++)
            {
                _transports[index].Dispose();
            }
        }
    }
}