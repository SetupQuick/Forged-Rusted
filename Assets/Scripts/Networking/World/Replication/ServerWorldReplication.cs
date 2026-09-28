using System;
using System.Collections.Generic;
using UnityEngine;

namespace Networking
{
    public sealed class ServerWorldReplication :
        IDisposable
    {
        private const uint
            TransformRefreshIntervalTicks = 16;

        private const int
            MaximumSnapshotEntries = 28;

        private readonly GameServer _server;

        private readonly NetworkWorld _world;

        private readonly IInterestManager
            _interestManager;

        private readonly Dictionary<
            uint,
            PeerReplicationState> _peerStates =
                new Dictionary<
                    uint,
                    PeerReplicationState>();

        private readonly List<SnapshotEntry>
            _snapshotEntries =
                new List<SnapshotEntry>(
                    MaximumSnapshotEntries);

        private readonly List<NetObject>
            _visibleObjects =
                new List<NetObject>();

        private readonly HashSet<uint>
            _visibleObjectIds =
                new HashSet<uint>();

        private readonly List<uint>
            _despawnBuffer =
                new List<uint>();

        private bool _disposed;

        public ServerWorldReplication(
            GameServer server,
            NetworkWorld world,
            IInterestManager interestManager)
        {
            _server = server ??
                throw new ArgumentNullException(
                    nameof(server));

            _world = world ??
                throw new ArgumentNullException(
                    nameof(world));

            _interestManager =
                interestManager ??
                throw new ArgumentNullException(
                    nameof(interestManager));

            _server.PeerConnected +=
                OnPeerConnected;

            _server.PeerDisconnected +=
                OnPeerDisconnected;

            _world.ObjectDespawned +=
                OnObjectDespawned;
        }

        public void Tick(
            uint serverTick)
        {
            if (_disposed)
                return;

            /*
             * Refresh dirty/version information only once
             * per server tick, not once per peer.
             */
            RefreshReplicationStates();

            foreach (
                PeerReplicationState state
                in _peerStates.Values)
            {
                UpdateInterest(
                    state);

                SendWorldSnapshot(
                    state,
                    serverTick);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _server.PeerConnected -=
                OnPeerConnected;

            _server.PeerDisconnected -=
                OnPeerDisconnected;

            _world.ObjectDespawned -=
                OnObjectDespawned;

            _peerStates.Clear();
            _snapshotEntries.Clear();
            _visibleObjects.Clear();
            _visibleObjectIds.Clear();
            _despawnBuffer.Clear();
        }

        private void OnPeerConnected(
            Peer peer)
        {
            if (peer == null)
                return;

            _peerStates[
                peer.Id] =
                new PeerReplicationState(
                    peer);
        }

        private void OnPeerDisconnected(
            uint peerId)
        {
            _peerStates.Remove(
                peerId);
        }

        private void OnObjectDespawned(
            uint networkId)
        {
            if (networkId == 0)
                return;

            foreach (
                PeerReplicationState state
                in _peerStates.Values)
            {
                if (!state.ObservedObjects.Contains(
                        networkId))
                {
                    continue;
                }

                state.ForgetObject(
                    networkId);

                _server.Send(
                    state.Peer,
                    NetworkMessageType.ObjectDespawn,
                    writer =>
                        writer.Write(
                            networkId),
                    NetworkDelivery.ReliableOrdered);
            }
        }

        private void UpdateInterest(
            PeerReplicationState state)
        {
            _visibleObjects.Clear();
            _visibleObjectIds.Clear();
            _despawnBuffer.Clear();

            _interestManager.GetVisibleObjects(
                state.Peer,
                _visibleObjects);

            /*
             * Newly visible objects.
             */
            for (int index = 0;
                 index < _visibleObjects.Count;
                 index++)
            {
                NetObject netObject =
                    _visibleObjects[index];

                if (netObject == null ||
                    !netObject.IsSpawned)
                {
                    continue;
                }

                uint networkId =
                    netObject.NetworkId;

                _visibleObjectIds.Add(
                    networkId);

                if (state.ObservedObjects.Contains(
                        networkId))
                {
                    continue;
                }

                state.ObservedObjects.Add(
                    networkId);

                SendSpawn(
                    state.Peer,
                    netObject);
            }

            /*
             * Objects that were visible before but
             * aren't anymore.
             *
             * Don't modify the HashSet while iterating it.
             */
            foreach (
                uint networkId
                in state.ObservedObjects)
            {
                if (_visibleObjectIds.Contains(
                        networkId))
                {
                    continue;
                }

                _despawnBuffer.Add(
                    networkId);
            }

            for (int index = 0;
                 index < _despawnBuffer.Count;
                 index++)
            {
                uint networkId =
                    _despawnBuffer[index];

                state.ForgetObject(
                    networkId);

                _server.Send(
                    state.Peer,
                    NetworkMessageType.ObjectDespawn,
                    writer =>
                        writer.Write(
                            networkId),
                    NetworkDelivery.ReliableOrdered);
            }
        }

        private void RefreshReplicationStates()
        {
            foreach (
                NetObject netObject
                in _world.Objects)
            {
                if (netObject == null ||
                    !netObject.IsSpawned)
                {
                    continue;
                }

                IReadOnlyList<NetBehaviour>
                    behaviours =
                        netObject.Behaviours;

                for (int index = 0;
                     index < behaviours.Count;
                     index++)
                {
                    NetBehaviour behaviour =
                        behaviours[index];

                    if (behaviour == null)
                        continue;

                    behaviour.RefreshReplicationState();
                }
            }
        }

        private void SendWorldSnapshot(
            PeerReplicationState peerState,
            uint serverTick)
        {
            BuildSnapshotEntries(
                peerState,
                serverTick);

            if (_snapshotEntries.Count == 0)
                return;

            bool sent =
                _server.Send(
                    peerState.Peer,
                    NetworkMessageType.WorldSnapshot,
                    writer =>
                    {
                        writer.Write(
                            serverTick);

                        writer.Write(
                            (ushort)
                            _snapshotEntries.Count);

                        for (int index = 0;
                             index <
                                _snapshotEntries.Count;
                             index++)
                        {
                            SnapshotEntry entry =
                                _snapshotEntries[index];

                            WriteStateEntry(
                                writer,
                                entry.NetObject.NetworkId,
                                entry.Behaviour);
                        }
                    },
                    NetworkDelivery.UnreliableSequenced);

            if (!sent)
                return;

            /*
             * This means "accepted by our send pipeline",
             * not "confirmed received by the remote peer".
             *
             * Periodic transform refresh handles packet loss.
             */
            for (int index = 0;
                 index < _snapshotEntries.Count;
                 index++)
            {
                SnapshotEntry entry =
                    _snapshotEntries[index];

                peerState.MarkSent(
                    entry.NetObject,
                    entry.Behaviour,
                    serverTick);
            }
        }

        private void BuildSnapshotEntries(
            PeerReplicationState peerState,
            uint serverTick)
        {
            _snapshotEntries.Clear();

            foreach (
                uint networkId
                in peerState.ObservedObjects)
            {
                if (_snapshotEntries.Count >=
                    MaximumSnapshotEntries)
                {
                    break;
                }

                if (!_world.TryGet(
                        networkId,
                        out NetObject netObject))
                {
                    continue;
                }

                if (netObject == null ||
                    !netObject.IsSpawned)
                {
                    continue;
                }

                /*
                 * CharacterMotor is deliberately NOT added.
                 *
                 * Player movement reconciliation now travels
                 * through PlayerMovementStateMessage.
                 *
                 * Remote player presentation is handled by
                 * NetTransform like any other network object.
                 */
                if (!netObject.TryGetBehaviour(
                        NetBehaviourType.Transform,
                        out NetBehaviour
                            transformBehaviour))
                {
                    continue;
                }

                if (!ShouldSendBehaviour(
                        peerState,
                        netObject,
                        transformBehaviour,
                        serverTick))
                {
                    continue;
                }

                _snapshotEntries.Add(
                    new SnapshotEntry(
                        netObject,
                        transformBehaviour));
            }
        }

        private static bool ShouldSendBehaviour(
            PeerReplicationState peerState,
            NetObject netObject,
            NetBehaviour behaviour,
            uint serverTick)
        {
            if (behaviour == null)
                return false;

            switch (behaviour.ComponentType)
            {
                case NetBehaviourType.Transform:
                    return peerState.ShouldSend(
                        netObject,
                        behaviour,
                        serverTick,
                        TransformRefreshIntervalTicks);

                /*
                 * Future replicated behaviours can add
                 * their own policy here.
                 */
                default:
                    return true;
            }
        }

        private static void WriteStateEntry(
            PacketWriter writer,
            uint networkId,
            NetBehaviour behaviour)
        {
            if (behaviour == null)
            {
                throw new ArgumentNullException(
                    nameof(behaviour));
            }

            var stateWriter =
                new PacketWriter();

            behaviour.WriteState(
                stateWriter);

            byte[] state =
                stateWriter.ToArray();

            writer.Write(
                networkId);

            writer.Write(
                (byte)
                behaviour.ComponentType);

            writer.Write(
                (ushort)
                state.Length);

            writer.Write(
                state);
        }

        private void SendSpawn(
            Peer peer,
            NetObject netObject)
        {
            if (peer == null ||
                netObject == null)
            {
                return;
            }

            _server.Send(
                peer,
                NetworkMessageType.ObjectSpawn,
                writer =>
                    WriteSpawn(
                        writer,
                        netObject),
                NetworkDelivery.ReliableOrdered);
        }

        private static void WriteSpawn(
            PacketWriter writer,
            NetObject netObject)
        {
            Transform objectTransform =
                netObject.transform;

            Vector3 position =
                objectTransform.position;

            Quaternion rotation =
                objectTransform.rotation;

            writer.Write(
                netObject.PrefabId);

            writer.Write(
                netObject.NetworkId);

            writer.Write(
                netObject.OwnerPeerId);

            writer.Write(
                position.x);

            writer.Write(
                position.y);

            writer.Write(
                position.z);

            writer.Write(
                rotation.x);

            writer.Write(
                rotation.y);

            writer.Write(
                rotation.z);

            writer.Write(
                rotation.w);
        }

        private readonly struct SnapshotEntry
        {
            public NetObject NetObject { get; }

            public NetBehaviour Behaviour { get; }

            public SnapshotEntry(
                NetObject netObject,
                NetBehaviour behaviour)
            {
                NetObject =
                    netObject;

                Behaviour =
                    behaviour;
            }
        }
    }
}