using System.Collections.Generic;
using Networking;
using UnityEngine;
using World.Chunks;

namespace World.Streaming
{
    /// <summary>
    /// Decides which chunks should exist and tells the WorldManager.
    ///
    /// Streams around:
    ///   - the local player, with <see cref="renderDistance"/> (clients);
    ///   - every player object, with <see cref="serverSimulationDistance"/>
    ///     (servers) - the server simulates movement with colliders, so it
    ///     needs terrain around remote players too, not only the host;
    ///   - <see cref="fallbackTarget"/> or the main camera when there is
    ///     no network runtime (offline testing).
    ///
    /// The chunk directly under each target is built immediately if it
    /// is missing, so players never fall through loading terrain.
    /// </summary>
    public sealed class ChunkStreamer : MonoBehaviour
    {
        [Header("References")]
        [SerializeField]
        private WorldManager worldManager;

        [SerializeField]
        private NetworkWorld networkWorld;

        [Tooltip("Streamed around when no network runtime exists. " +
                 "Uses the main camera when empty.")]
        [SerializeField]
        private Transform fallbackTarget;

        [Header("Streaming")]
        // Renamed from "viewDistance" on purpose so scenes pick up the
        // new default instead of the old value of 2.
        [Tooltip("Radius in chunks around the local player.")]
        [SerializeField]
        [Min(1)]
        private int renderDistance = 8;

        [Tooltip("Radius in chunks the server keeps around every player " +
                 "for collision.")]
        [SerializeField]
        [Min(1)]
        private int serverSimulationDistance = 2;

        [Header("Atmosphere")]
        [Tooltip("Fade distant terrain into fog that ends at the render " +
                 "distance, so chunks appear softly instead of popping in.")]
        [SerializeField]
        private bool distanceFog = true;

        [SerializeField]
        private Color fogColor = new Color(0.70f, 0.80f, 0.92f, 1f);

        [Tooltip("Where fog starts, as a fraction of the render distance.")]
        [SerializeField]
        [Range(0f, 0.95f)]
        private float fogStart = 0.55f;

        private int _appliedFogDistance = -1;

        /// <summary>Radius in chunks around the local player (from the graphics settings).</summary>
        public int RenderDistance
        {
            get => renderDistance;
            set => renderDistance = Mathf.Max(1, value);
        }

        /// <summary>What to stream around while not in a game (menu backdrop camera).</summary>
        public Transform FallbackTarget
        {
            get => fallbackTarget;
            set => fallbackTarget = value;
        }

        private readonly List<Target> _targets = new List<Target>();
        private readonly List<Target> _previousTargets = new List<Target>();

        private readonly Dictionary<ChunkCoord, int> _priority =
            new Dictionary<ChunkCoord, int>();

        private readonly List<ChunkCoord> _ordered = new List<ChunkCoord>();

        private int _previousSeed;
        private bool _hasPrevious;

        private struct Target
        {
            public ChunkCoord Center;
            public int Radius;
        }

        private void Awake()
        {
            if (worldManager == null)
                worldManager = FindFirstObjectByType<WorldManager>();

            if (networkWorld == null)
                networkWorld = FindFirstObjectByType<NetworkWorld>();
        }

        private void Update()
        {
            if (worldManager == null || !worldManager.IsReady)
                return;

            ApplyFog();

            CollectTargets();

            if (_targets.Count == 0)
                return;

            for (int i = 0; i < _targets.Count; i++)
                worldManager.EnsureCollisionAround(_targets[i].Center);

            if (TargetsUnchanged())
                return;

            RebuildWantedChunks();
        }

        /// <summary>Visible radius in metres for the local player.</summary>
        public float RenderDistanceMetres =>
            renderDistance * (worldManager != null && worldManager.Config != null
                ? worldManager.Config.ChunkSize * worldManager.VoxelSize
                : 16f);

        private void ApplyFog()
        {
            NetworkRuntime runtime = NetworkRuntime.Current;

            // Only machines that render (not a dedicated server).
            if (!distanceFog ||
                (runtime != null && runtime.IsRunning && !runtime.RunsClient) ||
                _appliedFogDistance == renderDistance)
            {
                return;
            }

            _appliedFogDistance = renderDistance;

            float end = RenderDistanceMetres * 0.95f;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogStartDistance = end * fogStart;
            RenderSettings.fogEndDistance = end;
        }

        private void CollectTargets()
        {
            _targets.Clear();

            NetworkRuntime runtime = NetworkRuntime.Current;

            if (runtime == null || !runtime.IsRunning)
            {
                Transform target = fallbackTarget;

                if (target == null && Camera.main != null)
                    target = Camera.main.transform;

                // Offline, or in the main menu before a game started.
                if (target != null)
                    AddTarget(target.position, renderDistance);

                return;
            }

            if (networkWorld == null)
                return;

            foreach (NetObject netObject in networkWorld.Objects)
            {
                if (netObject == null || !netObject.IsSpawned)
                    continue;

                if (runtime.RunsClient && netObject.IsLocallyOwned)
                {
                    AddTarget(netObject.transform.position, renderDistance);
                }
                else if (runtime.RunsServer && netObject.OwnerPeerId != 0)
                {
                    AddTarget(netObject.transform.position, serverSimulationDistance);
                }
            }
        }

        private void AddTarget(Vector3 position, int radius)
        {
            _targets.Add(new Target
            {
                Center = worldManager.WorldPositionToChunk(position),
                Radius = radius
            });
        }

        private bool TargetsUnchanged()
        {
            if (!_hasPrevious ||
                _previousSeed != worldManager.Seed ||
                _previousTargets.Count != _targets.Count)
            {
                return false;
            }

            for (int i = 0; i < _targets.Count; i++)
            {
                if (_targets[i].Center != _previousTargets[i].Center ||
                    _targets[i].Radius != _previousTargets[i].Radius)
                {
                    return false;
                }
            }

            return true;
        }

        private void RebuildWantedChunks()
        {
            _previousTargets.Clear();
            _previousTargets.AddRange(_targets);
            _previousSeed = worldManager.Seed;
            _hasPrevious = true;

            _priority.Clear();

            for (int t = 0; t < _targets.Count; t++)
            {
                Target target = _targets[t];
                int r = target.Radius;
                int limit = r * r + r; // round-ish circle

                for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int distance = dx * dx + dz * dz;

                    if (distance > limit)
                        continue;

                    var coord = new ChunkCoord(target.Center.X + dx, target.Center.Z + dz);

                    if (!_priority.TryGetValue(coord, out int existing) || distance < existing)
                        _priority[coord] = distance;
                }
            }

            _ordered.Clear();
            _ordered.AddRange(_priority.Keys);
            _ordered.Sort((a, b) => _priority[a].CompareTo(_priority[b]));

            worldManager.SetWantedChunks(_ordered);
        }
    }
}
