using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Networking
{
    public sealed class UdpTransport : INetworkTransport
    {
        private const int MaximumQueuedPackets = 1024;
        private const int ReceiveTimeoutMilliseconds = 25;

        private readonly ConcurrentQueue<TransportEvent> _incoming = new();
        private readonly ConcurrentQueue<OutgoingDatagram> _outgoing = new();

        private UdpClient _socket;
        private Thread _networkThread;

        private volatile bool _running;
        private volatile bool _stopRequested;
        private int _stopped;
        private int _incomingCount;
        private int _outgoingCount;
        private bool _isClient;

        public bool IsRunning => _running;

        public void StartServer(ushort port)
        {
            EnsureNotRunning();

            _socket = new UdpClient(port);
            _socket.Client.ReceiveTimeout = ReceiveTimeoutMilliseconds;
            _isClient = false;
            StartNetworkThread();
        }

        public ITransportHandle StartClient(string address, ushort port)
        {
            EnsureNotRunning();

            if (!IPAddress.TryParse(address, out IPAddress ip))
                throw new ArgumentException("Address must be a numeric IP address.", nameof(address));

            var server = new IPEndPoint(ip, port);

            _socket = new UdpClient(ip.AddressFamily);
            _socket.Connect(server);
            _socket.Client.ReceiveTimeout = ReceiveTimeoutMilliseconds;
            _isClient = true;

            StartNetworkThread();

            return new UdpTransportHandle(server);
        }
        public bool Send(ITransportHandle destination, byte[] data)
        {
            if (!_running ||
                !(destination is UdpTransportHandle udp) ||
                data == null ||
                data.Length == 0 ||
                data.Length > NetworkTransportLimits.MaximumDatagramSize)
            {
                return false;
            }

            if (Interlocked.Increment(ref _outgoingCount) > MaximumQueuedPackets)
            {
                Interlocked.Decrement(ref _outgoingCount);
                return false;
            }

            var ownedData = new byte[data.Length];
            Buffer.BlockCopy(data, 0, ownedData, 0, data.Length);
            _outgoing.Enqueue(new OutgoingDatagram(ownedData, udp.EndPoint));
            return true;
        }

        public bool TryPollEvent(out TransportEvent transportEvent)
        {
            if (_incoming.TryDequeue(out transportEvent))
            {
                Interlocked.Decrement(ref _incomingCount);
                return true;
            }

            return false;
        }

        public void Stop()
        {
            if (Interlocked.Exchange(ref _stopped, 1) != 0)
                return;

            _stopRequested = true;

            if (_networkThread != null &&
                _networkThread.IsAlive &&
                Thread.CurrentThread != _networkThread)
            {
                // Give the network thread one receive-timeout window to flush
                // queued messages such as a graceful Disconnect.
                if (!_networkThread.Join(100))
                {
                    _running = false;
                    CloseSocket();
                    _networkThread.Join(1900);
                }
            }

            _running = false;
            CloseSocket();
            _socket = null;
            _networkThread = null;

            ClearQueues();
        }

        public void Dispose()
        {
            Stop();
        }

        private void EnsureNotRunning()
        {
            if (_running || _socket != null)
                throw new InvalidOperationException("Transport is already running.");

            if (Volatile.Read(ref _stopped) != 0)
                throw new ObjectDisposedException(nameof(UdpTransport));
        }

        private void StartNetworkThread()
        {
            _stopRequested = false;
            _running = true;
            _networkThread = new Thread(NetworkLoop)
            {
                IsBackground = true,
                Name = _isClient ? "UDP Client Transport" : "UDP Server Transport"
            };
            _networkThread.Start();
        }

        private void NetworkLoop()
        {
            while (_running)
            {
                try
                {
                    FlushOutgoing();

                    if (_stopRequested)
                        break;

                    ReceiveOne();
                }
                catch (SocketException exception)
                {
                    if (!_running)
                        break;

                    if (exception.SocketErrorCode == SocketError.TimedOut ||
                        exception.SocketErrorCode == SocketError.WouldBlock)
                    {
                        continue;
                    }

                    QueueIncoming(TransportEvent.Failed(
                        $"UDP socket error: {exception.SocketErrorCode}"));
                }
                catch (ObjectDisposedException)
                {
                    if (_running)
                    {
                        QueueIncoming(TransportEvent.Failed("UDP socket was disposed unexpectedly."));
                        _running = false;
                    }
                }
                catch (Exception exception)
                {
                    QueueIncoming(TransportEvent.Failed(
                        $"UDP transport stopped: {exception.Message}"));
                    _running = false;
                }
            }

            _running = false;
        }

        private void ReceiveOne()
        {
            IPEndPoint remote = null;
            byte[] data = _socket.Receive(ref remote);

            if (data == null ||
                data.Length == 0 ||
                data.Length > NetworkTransportLimits.MaximumDatagramSize)
                return;

            QueueIncoming(TransportEvent.DataReceived(
                new UdpTransportHandle(remote),
                data));
        }

        private void FlushOutgoing()
        {
            while (_running && _outgoing.TryDequeue(out OutgoingDatagram item))
            {
                Interlocked.Decrement(ref _outgoingCount);

                if (_isClient)
                    _socket.Send(item.Data, item.Data.Length);
                else
                    _socket.Send(item.Data, item.Data.Length, item.Target);
            }
        }

        private bool QueueIncoming(TransportEvent transportEvent)
        {
            if (Interlocked.Increment(ref _incomingCount) > MaximumQueuedPackets)
            {
                Interlocked.Decrement(ref _incomingCount);
                return false;
            }

            _incoming.Enqueue(transportEvent);
            return true;
        }

        private void ClearQueues()
        {
            while (_incoming.TryDequeue(out _))
                Interlocked.Decrement(ref _incomingCount);

            while (_outgoing.TryDequeue(out _))
                Interlocked.Decrement(ref _outgoingCount);
        }

        private void CloseSocket()
        {
            try
            {
                _socket?.Close();
                _socket?.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private readonly struct OutgoingDatagram
        {
            public byte[] Data { get; }
            public IPEndPoint Target { get; }

            public OutgoingDatagram(byte[] data, IPEndPoint target)
            {
                Data = data;
                Target = target;
            }
        }
    }
}
