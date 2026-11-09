using System.Collections.Generic;
using UnityEngine;
using World;

namespace Networking
{
    [DefaultExecutionOrder(-900)]
    public sealed class PeerSpawnExample : MonoBehaviour
    {
        [SerializeField]
        private NetObject playerPrefab;

        [SerializeField]
        private Vector3 firstSpawnPosition =
            Vector3.zero;

        [SerializeField]
        private float spacing = 2.5f;

        [SerializeField]
        [Min(0f)]
        private float spawnHeightOffset = 2f;

        private readonly Dictionary<uint, uint>
            _objectByPeer =
                new Dictionary<uint, uint>();

        private NetworkRuntime _runtime;

        /// <summary>For scenes built from code (see GameBootstrap).</summary>
        public NetObject PlayerPrefab
        {
            get => playerPrefab;
            set => playerPrefab = value;
        }
        private WorldManager _worldManager;

        private int _spawnIndex;

        private void Start()
        {
            _runtime =
                NetworkRuntime.Current;

            // The runtime may start later (from the main menu), so
            // subscribe now; PeerConnected only fires on a server anyway.
            if (_runtime == null)
            {
                enabled = false;
                return;
            }

            if (playerPrefab == null)
            {
                Debug.LogError(
                    "Assign a player prefab " +
                    "to PeerSpawnExample.");

                enabled = false;
                return;
            }

            _worldManager =
                FindFirstObjectByType<WorldManager>();

            if (_worldManager == null &&
                _runtime.RunsServer)
            {
                Debug.LogError(
                    "PeerSpawnExample requires " +
                    "a WorldManager in the scene.");

                enabled = false;
                return;
            }

            _runtime.PeerConnected +=
                OnPeerConnected;

            _runtime.PeerDisconnected +=
                OnPeerDisconnected;
        }

        private void OnDestroy()
        {
            if (_runtime == null)
                return;

            _runtime.PeerConnected -=
                OnPeerConnected;

            _runtime.PeerDisconnected -=
                OnPeerDisconnected;
        }

        private void OnPeerConnected(
            Peer peer)
        {
            if (_worldManager == null)
            {
                _worldManager =
                    FindFirstObjectByType<WorldManager>();

                if (_worldManager == null)
                    return;
            }

            // Peer ids restart when a server is started again; forget
            // players from a previous session that no longer exist.
            if (_objectByPeer.TryGetValue(
                    peer.Id,
                    out uint existingId))
            {
                if (_runtime.World.TryGet(existingId, out NetObject existing) &&
                    existing != null)
                {
                    return;
                }

                _objectByPeer.Remove(peer.Id);
                _spawnIndex = 0;
            }

            // Spread players out a little, then let the world pick the
            // nearest dry, flat spot (never inside an ocean or river).
            // This also generates the terrain and colliders there.
            Vector3 preferred =
                new Vector3(
                    firstSpawnPosition.x +
                    (_spawnIndex++ * spacing),
                    0f,
                    firstSpawnPosition.z);

            Vector3 ground =
                _worldManager.FindSpawnPosition(
                    preferred);

            Vector3 position =
                ground +
                Vector3.up *
                spawnHeightOffset;

            NetObject netObject =
                _runtime.World.SpawnAuthoritative(
                    playerPrefab,
                    position,
                    Quaternion.identity,
                    peer.Id);

            _objectByPeer.Add(
                peer.Id,
                netObject.NetworkId);

            Debug.Log(
                $"Spawned peer {peer.Id} " +
                $"at {position}.");
        }

        private void OnPeerDisconnected(
            uint peerId)
        {
            if (!_objectByPeer.TryGetValue(
                    peerId,
                    out uint networkId))
            {
                return;
            }

            _objectByPeer.Remove(peerId);

            _runtime.World.Despawn(
                networkId);
        }
    }
}