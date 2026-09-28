using System;
using System.Collections.Generic;

namespace Networking
{
    public sealed class DeliveryTransport :
        INetworkDeliveryTransport
    {
        private const double ResendInterval = 0.2;

        private const int MaximumPendingPackets = 256;
        private const int MaximumBufferedPackets = 256;
        private const int MaximumIncomingEvents = 1024;
        private const int MaximumRawEventsPerUpdate = 2048;

        private readonly INetworkTransport _transport;

        private readonly Dictionary<
            ITransportHandle,
            RemoteState> _remotes =
                new Dictionary<
                    ITransportHandle,
                    RemoteState>();

        private readonly Queue<DeliveryEvent> _incoming =
            new Queue<DeliveryEvent>();

        private readonly List<uint> _sequenceScratch =
            new List<uint>(
                MaximumPendingPackets);

        private double _now;
        private bool _disposed;

        public bool IsRunning =>
            !_disposed &&
            _transport.IsRunning;

        public DeliveryTransport(
            INetworkTransport transport)
        {
            _transport = transport ??
                throw new ArgumentNullException(
                    nameof(transport));
        }

        public void StartServer(
            ushort port)
        {
            ThrowIfDisposed();

            _transport.StartServer(port);
        }

        public ITransportHandle StartClient(
            string address,
            ushort port)
        {
            ThrowIfDisposed();

            return _transport.StartClient(
                address,
                port);
        }

        public void RegisterRemote(
            ITransportHandle remote)
        {
            ThrowIfDisposed();

            if (remote == null)
            {
                throw new ArgumentNullException(
                    nameof(remote));
            }

            if (!_remotes.ContainsKey(remote))
            {
                _remotes.Add(
                    remote,
                    new RemoteState());
            }
        }

        public void RemoveRemote(
            ITransportHandle remote)
        {
            if (_disposed ||
                remote == null)
            {
                return;
            }

            _remotes.Remove(remote);
        }

        public bool Send(
            ITransportHandle destination,
            NetworkMessageType messageType,
            byte[] payload,
            NetworkDelivery delivery)
        {
            if (_disposed ||
                !_transport.IsRunning ||
                destination == null ||
                payload == null ||
                payload.Length >
                    NetworkProtocol.MaximumPayloadSize)
            {
                return false;
            }

            switch (delivery)
            {
                case NetworkDelivery.Unreliable:
                    return SendUnreliable(
                        destination,
                        messageType,
                        payload);

                case NetworkDelivery
                    .UnreliableSequenced:

                    return SendUnreliableSequenced(
                        destination,
                        messageType,
                        payload);

                case NetworkDelivery
                    .ReliableOrdered:

                    return SendReliableOrdered(
                        destination,
                        messageType,
                        payload);

                default:
                    return false;
            }
        }

        public void Update(double now)
        {
            if (_disposed ||
                !_transport.IsRunning)
            {
                return;
            }

            _now = now;

            int processed = 0;

            while (
                processed <
                    MaximumRawEventsPerUpdate &&
                _transport.TryPollEvent(
                    out TransportEvent
                        transportEvent))
            {
                processed++;

                if (transportEvent.Type ==
                    TransportEventType.Error)
                {
                    QueueError(
                        transportEvent.Error);

                    continue;
                }

                ProcessDatagram(
                    transportEvent.Remote,
                    transportEvent.Data);
            }

            DeliverReadyBufferedMessages();
            ResendPendingPackets();
        }

        public bool TryPollEvent(
            out DeliveryEvent deliveryEvent)
        {
            if (_incoming.Count == 0)
            {
                deliveryEvent = default;
                return false;
            }

            deliveryEvent =
                _incoming.Dequeue();

            return true;
        }

        public void Stop()
        {
            if (_disposed)
                return;

            _transport.Stop();

            ClearState();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _transport.Dispose();

            ClearState();
        }

        private bool SendUnreliable(
            ITransportHandle destination,
            NetworkMessageType messageType,
            byte[] payload)
        {
            byte[] datagram =
                NetworkProtocol.EncodeMessage(
                    messageType,
                    NetworkDelivery.Unreliable,
                    0,
                    payload);

            return _transport.Send(
                destination,
                datagram);
        }

        private bool SendUnreliableSequenced(
            ITransportHandle destination,
            NetworkMessageType messageType,
            byte[] payload)
        {
            if (!_remotes.TryGetValue(
                    destination,
                    out RemoteState state))
            {
                return false;
            }

            uint sequence =
                state.NextUnreliableSendSequence;

            byte[] datagram =
                NetworkProtocol.EncodeMessage(
                    messageType,
                    NetworkDelivery
                        .UnreliableSequenced,
                    sequence,
                    payload);

            if (!_transport.Send(
                    destination,
                    datagram))
            {
                return false;
            }

            state.NextUnreliableSendSequence++;

            return true;
        }

        private bool SendReliableOrdered(
            ITransportHandle destination,
            NetworkMessageType messageType,
            byte[] payload)
        {
            if (!_remotes.TryGetValue(
                    destination,
                    out RemoteState state))
            {
                return false;
            }

            if (state.PendingReliable.Count >=
                MaximumPendingPackets)
            {
                return false;
            }

            uint sequence =
                state.NextReliableSendSequence;

            byte[] datagram =
                NetworkProtocol.EncodeMessage(
                    messageType,
                    NetworkDelivery.ReliableOrdered,
                    sequence,
                    payload);

            if (!_transport.Send(
                    destination,
                    datagram))
            {
                return false;
            }

            state.NextReliableSendSequence++;

            state.PendingReliable.Add(
                sequence,
                new PendingPacket(
                    datagram,
                    _now));

            return true;
        }

        private void ProcessDatagram(
            ITransportHandle remote,
            byte[] datagram)
        {
            if (remote == null ||
                !NetworkProtocol.TryDecode(
                    datagram,
                    out NetworkPacket packet))
            {
                return;
            }

            if (packet.IsAcknowledgement)
            {
                if (_remotes.TryGetValue(
                        remote,
                        out RemoteState state))
                {
                    state.PendingReliable.Remove(
                        packet.Sequence);
                }

                return;
            }

            // Handshake messages are unreliable and must
            // be accepted before the server has registered
            // the remote endpoint.
            if (packet.Delivery ==
                NetworkDelivery.Unreliable)
            {
                QueueMessage(
                    remote,
                    packet.MessageType,
                    packet.Payload);

                return;
            }

            if (!_remotes.TryGetValue(
                    remote,
                    out RemoteState remoteState))
            {
                return;
            }

            switch (packet.Delivery)
            {
                case NetworkDelivery
                    .UnreliableSequenced:

                    ProcessUnreliableSequenced(
                        remote,
                        remoteState,
                        packet);
                    break;

                case NetworkDelivery
                    .ReliableOrdered:

                    ProcessReliableOrdered(
                        remote,
                        remoteState,
                        packet);
                    break;
            }
        }

        private void ProcessUnreliableSequenced(
            ITransportHandle remote,
            RemoteState state,
            NetworkPacket packet)
        {
            if (state.HasReceivedUnreliable &&
                !SequenceUtility.IsNewer(
                    packet.Sequence,
                    state
                        .LatestUnreliableReceiveSequence))
            {
                return;
            }

            state.HasReceivedUnreliable = true;

            state.LatestUnreliableReceiveSequence =
                packet.Sequence;

            QueueMessage(
                remote,
                packet.MessageType,
                packet.Payload);
        }

        private void ProcessReliableOrdered(
            ITransportHandle remote,
            RemoteState state,
            NetworkPacket packet)
        {
            uint sequence =
                packet.Sequence;

            uint expected =
                state.NextReliableReceiveSequence;

            if (sequence == expected)
            {
                // Do not ACK a packet that could not be
                // placed in the application event queue.
                if (!QueueMessage(
                        remote,
                        packet.MessageType,
                        packet.Payload))
                {
                    return;
                }

                SendAcknowledgement(
                    remote,
                    sequence);

                state.NextReliableReceiveSequence++;

                DeliverBufferedMessages(
                    remote,
                    state);

                return;
            }

            if (!SequenceUtility.IsNewer(
                    sequence,
                    expected))
            {
                // This message was already delivered.
                // ACK it again because the earlier
                // acknowledgement was probably lost.
                SendAcknowledgement(
                    remote,
                    sequence);

                return;
            }

            uint distance =
                SequenceUtility.ForwardDistance(
                    expected,
                    sequence);

            if (state.BufferedReliable
                .ContainsKey(sequence))
            {
                SendAcknowledgement(
                    remote,
                    sequence);

                return;
            }

            if (distance >
                    MaximumBufferedPackets ||
                state.BufferedReliable.Count >=
                    MaximumBufferedPackets)
            {
                // The message was not retained, so it
                // must not be acknowledged.
                return;
            }

            state.BufferedReliable.Add(
                sequence,
                new BufferedMessage(
                    packet.MessageType,
                    packet.Payload));

            SendAcknowledgement(
                remote,
                sequence);
        }

        private void DeliverBufferedMessages(
            ITransportHandle remote,
            RemoteState state)
        {
            while (
                state.BufferedReliable.TryGetValue(
                    state.NextReliableReceiveSequence,
                    out BufferedMessage message))
            {
                if (!QueueMessage(
                        remote,
                        message.MessageType,
                        message.Payload))
                {
                    return;
                }

                state.BufferedReliable.Remove(
                    state.NextReliableReceiveSequence);

                state.NextReliableReceiveSequence++;
            }
        }

        private void DeliverReadyBufferedMessages()
        {
            foreach (
                KeyValuePair<
                    ITransportHandle,
                    RemoteState> remote
                in _remotes)
            {
                if (_incoming.Count >=
                    MaximumIncomingEvents)
                {
                    return;
                }

                DeliverBufferedMessages(
                    remote.Key,
                    remote.Value);
            }
        }

        private void SendAcknowledgement(
            ITransportHandle remote,
            uint sequence)
        {
            byte[] datagram =
                NetworkProtocol
                    .EncodeAcknowledgement(
                        sequence);

            _transport.Send(
                remote,
                datagram);
        }

        private void ResendPendingPackets()
        {
            foreach (
                KeyValuePair<
                    ITransportHandle,
                    RemoteState> remote
                in _remotes)
            {
                _sequenceScratch.Clear();

                foreach (
                    KeyValuePair<
                        uint,
                        PendingPacket> pending
                    in remote.Value.PendingReliable)
                {
                    if (_now -
                        pending.Value.LastSendTime >=
                        ResendInterval)
                    {
                        _sequenceScratch.Add(
                            pending.Key);
                    }
                }

                for (int index = 0;
                     index < _sequenceScratch.Count;
                     index++)
                {
                    uint sequence =
                        _sequenceScratch[index];

                    if (!remote.Value.PendingReliable
                        .TryGetValue(
                            sequence,
                            out PendingPacket pending))
                    {
                        continue;
                    }

                    if (_transport.Send(
                            remote.Key,
                            pending.Datagram))
                    {
                        pending.LastSendTime = _now;
                    }
                }
            }

            _sequenceScratch.Clear();
        }

        private bool QueueMessage(
            ITransportHandle remote,
            NetworkMessageType messageType,
            byte[] payload)
        {
            if (payload == null ||
                _incoming.Count >=
                    MaximumIncomingEvents)
            {
                return false;
            }

            _incoming.Enqueue(
                DeliveryEvent.MessageReceived(
                    remote,
                    messageType,
                    payload));

            return true;
        }

        private void QueueError(
            string error)
        {
            if (_incoming.Count >=
                MaximumIncomingEvents)
            {
                return;
            }

            _incoming.Enqueue(
                DeliveryEvent.Failed(error));
        }

        private void ClearState()
        {
            _remotes.Clear();
            _incoming.Clear();
            _sequenceScratch.Clear();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(
                    nameof(DeliveryTransport));
            }
        }

        private sealed class RemoteState
        {
            public uint NextUnreliableSendSequence;

            public uint LatestUnreliableReceiveSequence;

            public bool HasReceivedUnreliable;

            public uint NextReliableSendSequence;

            public uint NextReliableReceiveSequence;

            public readonly Dictionary<
                uint,
                PendingPacket> PendingReliable =
                    new Dictionary<
                        uint,
                        PendingPacket>();

            public readonly Dictionary<
                uint,
                BufferedMessage> BufferedReliable =
                    new Dictionary<
                        uint,
                        BufferedMessage>();
        }

        private sealed class PendingPacket
        {
            public byte[] Datagram { get; }

            public double LastSendTime { get; set; }

            public PendingPacket(
                byte[] datagram,
                double lastSendTime)
            {
                Datagram = datagram;
                LastSendTime = lastSendTime;
            }
        }

        private readonly struct BufferedMessage
        {
            public NetworkMessageType MessageType
            {
                get;
            }

            public byte[] Payload
            {
                get;
            }

            public BufferedMessage(
                NetworkMessageType messageType,
                byte[] payload)
            {
                MessageType = messageType;
                Payload = payload;
            }
        }

        private static class SequenceUtility
        {
            public static bool IsNewer(
                uint candidate,
                uint reference)
            {
                return candidate != reference &&
                    unchecked(
                        (int)
                        (candidate - reference)) > 0;
            }

            public static uint ForwardDistance(
                uint from,
                uint to)
            {
                return unchecked(to - from);
            }
        }
    }
}