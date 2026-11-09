using System;
using System.Collections.Generic;
using UnityEngine;

namespace Networking
{
    /// <summary>
    /// Each player only hears about objects near them.
    ///
    /// Without this every player's position goes to every other player
    /// every tick, so traffic grows with players squared. Here a peer
    /// "sees" objects within <see cref="EnterRadius"/> metres (on the
    /// ground plane) of its own player, and keeps seeing them until they
    /// are further than <see cref="LeaveRadius"/> - the gap stops objects
    /// at the edge from spawning and despawning every tick. Objects the
    /// peer owns are always visible.
    ///
    /// The radius is larger than the longest view distance (Ultra:
    /// 192 m, fully fogged before that), so nobody pops in where you
    /// could see them.
    ///
    /// A peer whose player is not spawned yet sees everything (the old
    /// behaviour) so joining works exactly as before.
    /// </summary>
    public sealed class DistanceInterestManager : IInterestManager
    {
        private readonly NetworkWorld _world;
        private readonly Dictionary<uint, HashSet<uint>> _visibleByPeer = new Dictionary<uint, HashSet<uint>>();
        private readonly Dictionary<uint, NetObject> _anchors = new Dictionary<uint, NetObject>();

        public float EnterRadius { get; }
        public float LeaveRadius { get; }

        public DistanceInterestManager(NetworkWorld world, float enterRadius = 210f, float leaveRadius = 240f)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            EnterRadius = Mathf.Max(1f, enterRadius);
            LeaveRadius = Mathf.Max(EnterRadius, leaveRadius);
        }

        /// <summary>The player object a peer controls (null if not spawned yet).</summary>
        public NetObject GetAnchor(uint peerId)
        {
            if (_anchors.TryGetValue(peerId, out NetObject anchor) &&
                anchor != null && anchor.IsSpawned && anchor.OwnerPeerId == peerId)
            {
                return anchor;
            }

            _anchors.Remove(peerId);

            foreach (NetObject netObject in _world.Objects)
            {
                if (netObject != null && netObject.IsSpawned && netObject.OwnerPeerId == peerId)
                {
                    _anchors[peerId] = netObject;
                    return netObject;
                }
            }

            return null;
        }

        public void GetVisibleObjects(Peer peer, List<NetObject> results)
        {
            if (results == null)
                throw new ArgumentNullException(nameof(results));

            results.Clear();

            if (peer == null)
                return;

            NetObject anchor = GetAnchor(peer.Id);

            if (!_visibleByPeer.TryGetValue(peer.Id, out HashSet<uint> visible))
            {
                visible = new HashSet<uint>();
                _visibleByPeer[peer.Id] = visible;
            }

            Vector3 center = anchor != null ? anchor.transform.position : Vector3.zero;
            float enter = EnterRadius * EnterRadius;
            float leave = LeaveRadius * LeaveRadius;

            foreach (NetObject netObject in _world.Objects)
            {
                if (netObject == null || !netObject.IsSpawned)
                    continue;

                bool show;

                if (anchor == null || netObject.OwnerPeerId == peer.Id)
                {
                    show = true;
                }
                else
                {
                    Vector3 offset = netObject.transform.position - center;
                    float distance = offset.x * offset.x + offset.z * offset.z;
                    show = distance <= (visible.Contains(netObject.NetworkId) ? leave : enter);
                }

                if (show)
                {
                    visible.Add(netObject.NetworkId);
                    results.Add(netObject);
                }
                else
                {
                    visible.Remove(netObject.NetworkId);
                }
            }

            // Forget objects that no longer exist.
            if (visible.Count > results.Count)
            {
                visible.Clear();
                foreach (NetObject netObject in results)
                    visible.Add(netObject.NetworkId);
            }
        }

        /// <summary>Call when a peer leaves.</summary>
        public void Forget(uint peerId)
        {
            _visibleByPeer.Remove(peerId);
            _anchors.Remove(peerId);
        }
    }
}
