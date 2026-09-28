using System.Collections.Generic;
using Networking;
using UnityEngine;
using World.Chunks;

namespace World.Streaming
{
    public sealed class ChunkStreamer : MonoBehaviour
    {
        [Header("References")]
        [SerializeField]
        private WorldManager worldManager;

        [SerializeField]
        private NetworkWorld networkWorld;

        [Header("Streaming")]
        [SerializeField]
        [Min(0)]
        private int viewDistance = 2;

        private Transform _target;

        private ChunkCoord _currentCenter;
        private bool _hasCenter;

        private readonly HashSet<ChunkCoord>
            _desiredChunks =
                new HashSet<ChunkCoord>();

        private void Awake()
        {
            if (worldManager == null)
            {
                worldManager =
                    FindFirstObjectByType<WorldManager>();
            }

            if (networkWorld == null)
            {
                networkWorld =
                    FindFirstObjectByType<NetworkWorld>();
            }
        }

        private void Update()
        {
            if (_target == null)
            {
                TryFindLocalPlayer();

                if (_target == null)
                    return;
            }

            RefreshStreaming();
        }

        private void TryFindLocalPlayer()
        {
            if (networkWorld == null)
                return;

            foreach (
                NetObject netObject
                in networkWorld.Objects)
            {
                if (netObject == null ||
                    !netObject.IsSpawned ||
                    !netObject.IsLocallyOwned)
                {
                    continue;
                }

                _target =
                    netObject.transform;

                _hasCenter = false;

                Debug.Log(
                    $"ChunkStreamer attached to " +
                    $"local player {netObject.NetworkId}.");

                return;
            }
        }

        private void RefreshStreaming()
        {
            if (worldManager == null ||
                _target == null)
            {
                return;
            }

            Vector3 position =
                _target.position;

            ChunkCoord center =
                worldManager.WorldToChunkCoord(
                    Mathf.FloorToInt(
                        position.x),
                    Mathf.FloorToInt(
                        position.z));

            if (_hasCenter &&
                center == _currentCenter)
            {
                return;
            }

            _currentCenter = center;
            _hasCenter = true;

            BuildDesiredSet(center);

            worldManager.UpdateVisibleChunks(
                _desiredChunks);
        }

        private void BuildDesiredSet(
            ChunkCoord center)
        {
            _desiredChunks.Clear();

            for (int x = -viewDistance;
                 x <= viewDistance;
                 x++)
            {
                for (int z = -viewDistance;
                     z <= viewDistance;
                     z++)
                {
                    _desiredChunks.Add(
                        new ChunkCoord(
                            center.X + x,
                            center.Z + z));
                }
            }
        }
    }
}