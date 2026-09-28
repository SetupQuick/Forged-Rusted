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
        private WorldManager _worldManager;

        private int _spawnIndex;

        private void Start()
        {
            _runtime =
                NetworkRuntime.Current;

            if (_runtime == null ||
                !_runtime.RunsServer)
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

            if (_worldManager == null)
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
            if (_objectByPeer.ContainsKey(
                peer.Id))
            {
                return;
            }

            float spawnX =
                firstSpawnPosition.x +
                (_spawnIndex++ * spacing);

            float spawnZ =
                firstSpawnPosition.z;

            int surfaceY =
                _worldManager.GetSurfaceHeight(
                    Mathf.FloorToInt(spawnX),
                    Mathf.FloorToInt(spawnZ));

            Vector3 position =
                new Vector3(
                    spawnX,
                    surfaceY +
                    spawnHeightOffset,
                    spawnZ);

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
                $"at {position}. " +
                $"Surface Y={surfaceY}.");
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