using System;
using System.Collections.Generic;

namespace Networking
{
    public sealed class PeerReplicationState
    {
        private readonly Dictionary<
            BehaviourReplicationKey,
            BehaviourSendState> _behaviourStates =
                new Dictionary<
                    BehaviourReplicationKey,
                    BehaviourSendState>();

        public Peer Peer { get; }

        public HashSet<uint> ObservedObjects
        {
            get;
        } = new HashSet<uint>();

        public PeerReplicationState(
            Peer peer)
        {
            Peer = peer ??
                throw new ArgumentNullException(
                    nameof(peer));
        }

        public bool ShouldSend(
            NetObject netObject,
            NetBehaviour behaviour,
            uint serverTick,
            uint refreshIntervalTicks)
        {
            var key =
                new BehaviourReplicationKey(
                    netObject.NetworkId,
                    behaviour.ComponentType);

            if (!_behaviourStates.TryGetValue(
                    key,
                    out BehaviourSendState sentState))
            {
                return true;
            }

            if (sentState.Version !=
                behaviour.StateVersion)
            {
                return true;
            }

            uint elapsed =
                unchecked(
                    serverTick -
                    sentState.LastSentTick);

            return elapsed >=
                   refreshIntervalTicks;
        }

        public void MarkSent(
            NetObject netObject,
            NetBehaviour behaviour,
            uint serverTick)
        {
            var key =
                new BehaviourReplicationKey(
                    netObject.NetworkId,
                    behaviour.ComponentType);

            _behaviourStates[key] =
                new BehaviourSendState(
                    behaviour.StateVersion,
                    serverTick);
        }

        public void ForgetObject(
            uint networkId)
        {
            ObservedObjects.Remove(
                networkId);

            var keysToRemove =
                new List<
                    BehaviourReplicationKey>();

            foreach (
                BehaviourReplicationKey key
                in _behaviourStates.Keys)
            {
                if (key.NetworkId ==
                    networkId)
                {
                    keysToRemove.Add(
                        key);
                }
            }

            for (int index = 0;
                 index < keysToRemove.Count;
                 index++)
            {
                _behaviourStates.Remove(
                    keysToRemove[index]);
            }
        }
    }
}