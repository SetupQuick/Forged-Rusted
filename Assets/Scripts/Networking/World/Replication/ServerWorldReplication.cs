using System;
using System.Collections.Generic;
using UnityEngine;

namespace Networking
{
    /// <summary>
    /// Sends spawns, despawns and transform updates of network objects to
    /// each peer, limited to what that peer can see (interest manager).
    ///
    /// Bandwidth: a changed transform is sent every tick to peers within
    /// <see cref="NearDistance"/>, every 2nd tick up to
    /// <see cref="MidDistance"/>, every 4th beyond (smoothing on the
    /// client hides it at that range). When more updates are due than fit
    /// in one packet, the ones that waited longest go first, so no object
    /// is ever starved.
    /// </summary>
    public sealed class ServerWorldReplication :
        IDisposable
    {
        private const uint
            TransformRefreshIntervalTicks = 16;

        /// <summary>Metres within which changed transforms are sent every tick.</summary>
        public const float NearDistance = 48f;

        /// <summary>Metres within which they are sent every 2nd tick (every 4th beyond).</summary>
        public const float MidDistance = 112f;

        private readonly List<(SnapshotEntry Entry, uint Waited)> _candidates =
            new List<(SnapshotEntry, uint)>();

        private const int
            MaximumSnapshotEntries = 28;

        private const int
            MaximumSnapshotsPerTick = 2;

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

            (_interestManager as DistanceInterestManager)?.Forget(peerId);
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

            // One packet holds MaximumSnapshotEntries; crowded places use
            // up to MaximumSnapshotsPerTick packets so nearby players still
            // update every tick. Anything beyond waits for the next tick
            // (longest-waiting first).
            for (int first = 0;
                 first < _snapshotEntries.Count;
                 first += MaximumSnapshotEntries)
            {
                int count = Math.Min(MaximumSnapshotEntries, _snapshotEntries.Count - first);
                int start = first;

                bool sent =
                    _server.Send(
                        peerState.Peer,
                        NetworkMessageType.WorldSnapshot,
                        writer =>
                        {
                            writer.Write(
                                serverTick);

                            writer.Write(
                                (ushort)count);

                            for (int index = start;
                                 index < start + count;
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
                for (int index = start;
                     index < start + count;
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
        }

        private void BuildSnapshotEntries(
            PeerReplicationState peerState,
            uint serverTick)
        {
            _snapshotEntries.Clear();
            _candidates.Clear();

            // Where this peer's player is, for distance-based send rates.
            NetObject anchor =
                (_interestManager as DistanceInterestManager)?.GetAnchor(peerState.Peer.Id);

            foreach (
                uint networkId
                in peerState.ObservedObjects)
            {
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

                uint minimumInterval = SendInterval(anchor, netObject);

                if (!ShouldSendBehaviour(
                        peerState,
                        netObject,
                        transformBehaviour,
                        serverTick,
                        minimumInterval))
                {
                    continue;
                }

                _candidates.Add((
                    new SnapshotEntry(netObject, transformBehaviour),
                    peerState.TicksSinceSent(netObject, transformBehaviour, serverTick)));
            }

            // Longest-waiting first, so nothing starves when more updates
            // are due than fit in one snapshot.
            int capacity = MaximumSnapshotEntries * MaximumSnapshotsPerTick;

            if (_candidates.Count > capacity)
                _candidates.Sort(LongestWaitingFirst);

            int count = Math.Min(_candidates.Count, capacity);
            for (int index = 0; index < count; index++)
                _snapshotEntries.Add(_candidates[index].Entry);
        }

        private static readonly Comparison<(SnapshotEntry Entry, uint Waited)> LongestWaitingFirst =
            (a, b) => b.Waited.CompareTo(a.Waited);

        /// <summary>Minimum ticks between updates of a changed object, by distance.</summary>
        private static uint SendInterval(NetObject anchor, NetObject netObject)
        {
            if (anchor == null || anchor == netObject)
                return 1;

            Vector3 offset = netObject.transform.position - anchor.transform.position;
            float distance = offset.x * offset.x + offset.z * offset.z;

            if (distance <= NearDistance * NearDistance)
                return 1;

            return distance <= MidDistance * MidDistance ? 2u : 4u;
        }

        private static bool ShouldSendBehaviour(
            PeerReplicationState peerState,
            NetObject netObject,
            NetBehaviour behaviour,
            uint serverTick,
            uint minimumIntervalTicks)
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
                        TransformRefreshIntervalTicks,
                        minimumIntervalTicks);

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