using System;
using System.Security.Cryptography;

namespace Networking
{
    public sealed class GameClient : IDisposable
    {
        private const double RetryInterval = 0.75;
        private const double ConnectTimeout = 10.0;
        private const double HeartbeatInterval = 2.0;
        private const double ServerTimeout = 15.0;
        private const int MaximumEventsPerUpdate = 256;

        private readonly INetworkDeliveryTransport _transport;
        private readonly NetworkDiagnostics _diagnostics;

        private ITransportHandle _server;

        private ClientConnectionState _state =
            ClientConnectionState.Stopped;

        private ulong _clientNonce;
        private ulong _sessionToken;
        private uint _peerId;

        private double _startedAt;
        private double _lastSendTime;
        private double _lastServerReceiveTime;

        private uint _nextHeartbeatSequence = 1;
        private uint _pendingHeartbeatSequence;
        private double _pendingHeartbeatSentAt;

        private bool _disposed;

        public ClientConnectionState State => _state;

        public uint PeerId => _peerId;

        public bool IsConnected =>
            _state ==
            ClientConnectionState.Connected;

        public event Action<ClientConnectionState>
            StateChanged;

        public event Action<string> Error;

        public event Action<
            NetworkMessageType,
            byte[]> MessageReceived;

        public GameClient(
            INetworkDeliveryTransport transport,
            NetworkDiagnostics diagnostics)
        {
            _transport = transport ??
                throw new ArgumentNullException(
                    nameof(transport));

            _diagnostics = diagnostics ??
                throw new ArgumentNullException(
                    nameof(diagnostics));
        }

        public void Connect(
            string address,
            ushort port,
            double now)
        {
            ThrowIfDisposed();

            if (_state !=
                ClientConnectionState.Stopped)
            {
                throw new InvalidOperationException(
                    "Client has already been started.");
            }

            _server =
                _transport.StartClient(
                    address,
                    port);

            _transport.RegisterRemote(_server);

            _clientNonce =
                CreateRandomUInt64();

            _startedAt = now;
            _lastServerReceiveTime = now;

            SetState(
                ClientConnectionState.SendingHello);

            SendHello(now);
        }

        public void Update(double now)
        {
            if (_disposed ||
                _state ==
                ClientConnectionState.Stopped)
            {
                return;
            }

            _transport.Update(now);

            int processed = 0;

            while (
                processed < MaximumEventsPerUpdate &&
                _transport.TryPollEvent(
                    out DeliveryEvent deliveryEvent))
            {
                processed++;

                if (deliveryEvent.Type ==
                    DeliveryEventType.Error)
                {
                    Error?.Invoke(
                        deliveryEvent.Error);

                    continue;
                }

                if (_server == null ||
                    deliveryEvent.Remote == null ||
                    !_server.Equals(
                        deliveryEvent.Remote))
                {
                    continue;
                }

                // Any packet from the server proves that the
                // connection is still alive, not only heartbeat ACKs.
                _lastServerReceiveTime = now;

                HandleMessage(
                    deliveryEvent.MessageType,
                    deliveryEvent.Payload,
                    now);
            }

            if (_state ==
                    ClientConnectionState.SendingHello ||
                _state ==
                    ClientConnectionState.SendingReady)
            {
                if (now - _startedAt >=
                    ConnectTimeout)
                {
                    TimeOut(
                        "Connection handshake timed out.");

                    return;
                }

                if (now - _lastSendTime >=
                    RetryInterval)
                {
                    if (_state ==
                        ClientConnectionState.SendingHello)
                    {
                        SendHello(now);
                    }
                    else
                    {
                        SendReady(now);
                    }
                }

                return;
            }

            if (_state !=
                ClientConnectionState.Connected)
            {
                return;
            }

            if (now - _lastServerReceiveTime >=
                ServerTimeout)
            {
                TimeOut(
                    "Server connection timed out.");

                return;
            }

            if (now - _lastSendTime >=
                HeartbeatInterval)
            {
                SendHeartbeat(now);
            }
        }

        public void Disconnect()
        {
            if (_disposed)
                return;

            if (_state ==
                    ClientConnectionState.SendingReady ||
                _state ==
                    ClientConnectionState.Connected)
            {
                SendMessage(
                    NetworkMessageType.Disconnect,
                    writer =>
                    {
                        writer.Write(_peerId);
                        writer.Write(_sessionToken);
                    });
            }

            StopConnection(
                ClientConnectionState.Stopped);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            Disconnect();

            _transport.Dispose();

            _disposed = true;
        }

        public bool Send(
            NetworkMessageType type,
            Action<PacketWriter> writePayload,
            NetworkDelivery delivery =
                NetworkDelivery.Unreliable)
        {
            if (_disposed ||
                _state !=
                    ClientConnectionState.Connected)
            {
                return false;
            }

            return SendMessage(
                type,
                writePayload,
                delivery);
        }

        private void HandleMessage(
            NetworkMessageType type,
            byte[] data,
            double now)
        {
            if (data == null)
                return;

            try
            {
                var payload =
                    new PacketReader(data);

                switch (type)
                {
                    case NetworkMessageType.ServerAccept:
                        HandleServerAccept(
                            payload,
                            now);
                        break;

                    case NetworkMessageType.ServerReady:
                        HandleServerReady(
                            payload,
                            now);
                        break;

                    case NetworkMessageType.HeartbeatAck:
                        HandleHeartbeatAck(
                            payload,
                            now);
                        break;

                    case NetworkMessageType.Disconnect:
                        HandleDisconnect(payload);
                        break;

                    default:
                        HandleApplicationMessage(
                            type,
                            data);
                        break;
                }
            }
            catch (InvalidOperationException)
            {
                // Malformed payloads are ignored.
            }
        }

        private void HandleApplicationMessage(
            NetworkMessageType type,
            byte[] data)
        {
            if (!IsConnected)
                return;

            MessageReceived?.Invoke(
                type,
                data);
        }

        private void HandleServerAccept(
            PacketReader payload,
            double now)
        {
            const int expectedSize =
                sizeof(ulong) +
                sizeof(uint) +
                sizeof(ulong);

            if (payload.Remaining != expectedSize ||
                (_state !=
                     ClientConnectionState.SendingHello &&
                 _state !=
                     ClientConnectionState.SendingReady))
            {
                return;
            }

            ulong nonce =
                payload.ReadUInt64();

            uint peerId =
                payload.ReadUInt32();

            ulong token =
                payload.ReadUInt64();

            if (nonce != _clientNonce ||
                peerId == 0 ||
                token == 0)
            {
                return;
            }

            if (_state ==
                    ClientConnectionState.SendingReady &&
                (_peerId != peerId ||
                 _sessionToken != token))
            {
                return;
            }

            _peerId = peerId;
            _sessionToken = token;
            _lastServerReceiveTime = now;

            SetState(
                ClientConnectionState.SendingReady);

            SendReady(now);
        }

        private void HandleServerReady(
            PacketReader payload,
            double now)
        {
            if (payload.Remaining != sizeof(uint) ||
                (_state !=
                     ClientConnectionState.SendingReady &&
                 _state !=
                     ClientConnectionState.Connected))
            {
                return;
            }

            uint peerId =
                payload.ReadUInt32();

            if (peerId != _peerId)
                return;

            _lastServerReceiveTime = now;

            SetState(
                ClientConnectionState.Connected);
        }

        private void HandleDisconnect(
            PacketReader payload)
        {
            const int expectedSize =
                sizeof(uint) +
                sizeof(ulong);

            if (payload.Remaining != expectedSize ||
                (_state !=
                     ClientConnectionState.SendingReady &&
                 _state !=
                     ClientConnectionState.Connected))
            {
                return;
            }

            uint peerId =
                payload.ReadUInt32();

            ulong token =
                payload.ReadUInt64();

            if (peerId != _peerId ||
                token != _sessionToken)
            {
                return;
            }

            StopConnection(
                ClientConnectionState.Stopped);
        }

        private void HandleHeartbeatAck(
            PacketReader payload,
            double now)
        {
            if (payload.Remaining !=
                    sizeof(uint) + sizeof(uint) ||
                _state !=
                    ClientConnectionState.Connected)
            {
                return;
            }

            uint peerId = payload.ReadUInt32();
            uint heartbeatSequence = payload.ReadUInt32();

            if (peerId != _peerId)
                return;

            _lastServerReceiveTime = now;

            if (heartbeatSequence ==
                _pendingHeartbeatSequence)
            {
                _diagnostics.RoundTripTimeMilliseconds =
                    Math.Max(
                        0.0,
                        (now - _pendingHeartbeatSentAt) *
                        1000.0);
            }
        }

        private void SendHello(double now)
        {
            SendMessage(
                NetworkMessageType.ClientHello,
                writer =>
                    writer.Write(_clientNonce));

            _lastSendTime = now;
        }

        private void SendReady(double now)
        {
            SendMessage(
                NetworkMessageType.ClientReady,
                writer =>
                {
                    writer.Write(_peerId);
                    writer.Write(_sessionToken);
                });

            _lastSendTime = now;
        }

        private void SendHeartbeat(double now)
        {
            uint heartbeatSequence =
                _nextHeartbeatSequence++;

            bool sent = SendMessage(
                NetworkMessageType.Heartbeat,
                writer =>
                {
                    writer.Write(_peerId);
                    writer.Write(_sessionToken);
                    writer.Write(heartbeatSequence);
                });

            if (sent)
            {
                _pendingHeartbeatSequence =
                    heartbeatSequence;

                _pendingHeartbeatSentAt = now;
            }

            _lastSendTime = now;
        }

        private bool SendMessage(
            NetworkMessageType type,
            Action<PacketWriter> writePayload,
            NetworkDelivery delivery =
                NetworkDelivery.Unreliable)
        {
            var writer =
                new PacketWriter();

            writePayload?.Invoke(writer);

            byte[] payload =
                writer.ToArray();

            if (!_transport.Send(
                    _server,
                    type,
                    payload,
                    delivery))
            {
                Error?.Invoke(
                    $"Could not queue {type} " +
                    "message for sending.");

                return false;
            }

            return true;
        }

        private void TimeOut(
            string reason)
        {
            Error?.Invoke(reason);

            StopConnection(
                ClientConnectionState.TimedOut);
        }

        private void StopConnection(
            ClientConnectionState finalState)
        {
            _transport.Stop();

            _server = null;
            _clientNonce = 0;
            _sessionToken = 0;
            _peerId = 0;

            _pendingHeartbeatSequence = 0;
            _pendingHeartbeatSentAt = 0.0;

            SetState(finalState);
        }

        private void SetState(
            ClientConnectionState state)
        {
            if (_state == state)
                return;

            _state = state;

            StateChanged?.Invoke(state);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(
                    nameof(GameClient));
            }
        }

        private static ulong CreateRandomUInt64()
        {
            byte[] bytes =
                new byte[sizeof(ulong)];

            ulong value;

            using (
                RandomNumberGenerator random =
                    RandomNumberGenerator.Create())
            {
                do
                {
                    random.GetBytes(bytes);

                    value =
                        BitConverter.ToUInt64(
                            bytes,
                            0);
                }
                while (value == 0);
            }

            return value;
        }
    }
}
