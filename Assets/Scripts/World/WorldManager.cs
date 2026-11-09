using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Networking;
using UnityEngine;
using World.Blocks;
using World.Chunks;
using World.Generation;
using World.Rendering;

namespace World
{
    /// <summary>
    /// Owns the block world: chunk data, chunk scene objects, and the
    /// background generation/meshing pipeline.
    ///
    /// Seed flow (server authoritative):
    ///   server / listen server - picks the seed from WorldSettings
    ///                            and publishes it through
    ///                            NetworkRuntime.SetServerWorldInfo;
    ///   client                 - waits for the WorldInfo message and
    ///                            only then starts generating;
    ///   no NetworkRuntime      - offline, uses the settings seed.
    ///
    /// Pipeline per chunk:
    ///   generate data (worker thread) -> publish (main thread)
    ///   -> build mesh once all 8 neighbours exist (worker thread)
    ///   -> upload mesh + collider (main thread, limited per frame).
    ///
    /// Which chunks are wanted is decided by <see cref="Streaming.ChunkStreamer"/>.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public sealed class WorldManager
        : MonoBehaviour
    {
        [SerializeField]
        private WorldSettings settings;

        [Header("Rendering")]
        [Tooltip("Opaque block material. Needs a vertex-colour shader " +
                 "(ForgedRusted/Voxel Lit). Loaded from " +
                 "Resources/World/ChunkMaterial when empty.")]
        [SerializeField]
        private Material chunkMaterial;

        [Tooltip("Transparent water material. Loaded from " +
                 "Resources/World/WaterMaterial when empty.")]
        [SerializeField]
        private Material waterMaterial;

        [Header("Performance")]
        [Tooltip("Finished chunk meshes uploaded per frame. " +
                 "Higher loads faster but can cause hitches.")]
        [SerializeField]
        [Min(1)]
        private int maxMeshUploadsPerFrame = 4;

        [Tooltip("Background generation/meshing jobs queued at once. " +
                 "0 = automatic (2 x CPU cores). The thread pool still " +
                 "runs only as many as there are cores; the extra ones " +
                 "keep it busy between frames.")]
        [SerializeField]
        [Min(0)]
        private int maxConcurrentJobs;

        private readonly Dictionary<ChunkCoord, ChunkData> _chunks =
            new Dictionary<ChunkCoord, ChunkData>();

        private readonly Dictionary<ChunkCoord, ChunkView> _views =
            new Dictionary<ChunkCoord, ChunkView>();

        private readonly HashSet<ChunkCoord> _generating =
            new HashSet<ChunkCoord>();

        private readonly HashSet<ChunkCoord> _meshing =
            new HashSet<ChunkCoord>();

        private readonly HashSet<ChunkCoord> _dirty =
            new HashSet<ChunkCoord>();

        private readonly ConcurrentQueue<JobResult> _completed =
            new ConcurrentQueue<JobResult>();

        // Streaming state, set by ChunkStreamer.
        private readonly List<ChunkCoord> _wantedViews =
            new List<ChunkCoord>();

        private readonly HashSet<ChunkCoord> _wantedViewSet =
            new HashSet<ChunkCoord>();

        private readonly HashSet<ChunkCoord> _keepData =
            new HashSet<ChunkCoord>();

        private readonly List<ChunkCoord> _scratch =
            new List<ChunkCoord>();

        private IWorldGenerator _generator;
        private NetworkRuntime _runtime;
        private bool _subscribedToRuntime;
        private bool _renderingEnabled = true;
        private int _runningJobs;
        private int _epoch;

        public WorldSettings Settings =>
            settings;

        /// <summary>True once a seed is known and chunks can be generated.</summary>
        public bool IsReady =>
            _generator != null;

        public int Seed =>
            _generator?.Config.Seed ?? 0;

        public TerrainConfig Config =>
            _generator?.Config;

        public int LoadedChunkCount =>
            _chunks.Count;

        public int VisibleChunkCount =>
            _views.Count;

        public int PendingJobCount =>
            _runningJobs;

        /// <summary>Raised whenever a world (re)initialises with a seed.</summary>
        public event Action<WorldManager> WorldReady;

        /// <summary>The world in the current scene (there is one per scene).</summary>
        public static WorldManager Active { get; private set; }

        private void Awake()
        {
            Active = this;

            if (settings == null)
            {
                throw new InvalidOperationException(
                    "Assign WorldSettings to WorldManager.");
            }

            if (chunkMaterial == null)
                chunkMaterial = Resources.Load<Material>("World/ChunkMaterial");

            if (waterMaterial == null)
                waterMaterial = Resources.Load<Material>("World/WaterMaterial");

            if (maxConcurrentJobs <= 0)
                maxConcurrentJobs = Mathf.Clamp(SystemInfo.processorCount * 2, 4, 16);
        }

        private void Start()
        {
            TryInitializeFromNetwork();
        }

        private void OnDestroy()
        {
            if (Active == this)
                Active = null;

            UnsubscribeFromRuntime();

            // Results of jobs still running are ignored.
            _epoch++;
        }

        private void Update()
        {
            if (!IsReady)
            {
                TryInitializeFromNetwork();

                if (!IsReady)
                    return;
            }

            // A restarted server runtime forgets the world info.
            if (_runtime != null &&
                _runtime.IsRunning &&
                _runtime.RunsServer &&
                !_runtime.ServerWorldInfo.HasValue)
            {
                PublishServerWorldInfo();
            }

            ProcessCompletedJobs();
            ScheduleJobs();
        }

        // ------------------------------------------------------------
        // Seed / initialisation
        // ------------------------------------------------------------

        /// <summary>
        /// Starts the world if enough is known: as a server it picks the
        /// seed, as a client it waits for the server's seed.
        /// </summary>
        public void TryInitializeFromNetwork()
        {
            NetworkRuntime runtime = NetworkRuntime.Current;

            if (runtime == null)
            {
                if (!IsReady)
                {
                    _renderingEnabled = true;
                    InitializeWorld(settings.Seed);
                }

                return;
            }

            if (_runtime != runtime)
            {
                UnsubscribeFromRuntime();
                _runtime = runtime;
            }

            if (!_subscribedToRuntime)
            {
                _runtime.WorldInfoReceived += OnWorldInfoReceived;
                _subscribedToRuntime = true;
            }

            if (!_runtime.IsRunning)
                return;

            _renderingEnabled = _runtime.RunsClient;

            if (_runtime.RunsServer)
            {
                if (!IsReady)
                    InitializeWorld(ResolveServerSeed());

                PublishServerWorldInfo();
                return;
            }

            if (_runtime.ReceivedWorldInfo.HasValue && !IsReady)
                OnWorldInfoReceived(_runtime.ReceivedWorldInfo.Value);
        }

        /// <summary>
        /// (Re)creates the world for a seed. Existing chunks are dropped.
        /// </summary>
        public void InitializeWorld(int seed)
        {
            if (IsReady && seed == Seed)
                return;

            if (IsReady)
                ResetWorld();

            TerrainConfig config = settings.CreateConfig(seed);
            _generator = new NoiseWorldGenerator(config);

            Debug.Log(
                $"World initialised: seed {seed}, " +
                $"height {config.WorldHeight}, sea level {config.SeaLevel}.");

            WorldReady?.Invoke(this);
        }

        private int ResolveServerSeed()
        {
            if (!settings.UseRandomSeed)
                return settings.Seed;

            return new System.Random().Next(int.MinValue, int.MaxValue);
        }

        private void PublishServerWorldInfo()
        {
            if (_runtime == null || !_runtime.RunsServer || !IsReady)
                return;

            TerrainConfig config = _generator.Config;

            _runtime.SetServerWorldInfo(
                new WorldInfoMessage(
                    config.Seed,
                    TerrainConfig.GeneratorVersion,
                    config.SettingsFingerprint));
        }

        private void OnWorldInfoReceived(WorldInfoMessage info)
        {
            // On a listen server the local client gets the same seed the
            // server already uses; nothing to do.
            if (_runtime != null && _runtime.RunsServer)
                return;

            TerrainConfig local = settings.CreateConfig(info.Seed);

            if (info.GeneratorVersion != TerrainConfig.GeneratorVersion ||
                info.SettingsFingerprint != local.SettingsFingerprint)
            {
                Debug.LogError(
                    "World settings differ from the server's " +
                    $"(server: generator v{info.GeneratorVersion}, " +
                    $"settings {info.SettingsFingerprint:X8}; " +
                    $"local: generator v{TerrainConfig.GeneratorVersion}, " +
                    $"settings {local.SettingsFingerprint:X8}). " +
                    "Terrain will not match the server. Make sure the " +
                    "client and server builds use the same WorldSettings.");
            }

            InitializeWorld(info.Seed);
        }

        private void UnsubscribeFromRuntime()
        {
            if (_runtime != null && _subscribedToRuntime)
                _runtime.WorldInfoReceived -= OnWorldInfoReceived;

            _subscribedToRuntime = false;
        }

        private void ResetWorld()
        {
            _epoch++;

            foreach (ChunkView view in _views.Values)
            {
                if (view != null)
                    Destroy(view.gameObject);
            }

            _views.Clear();
            _chunks.Clear();
            _generating.Clear();
            _meshing.Clear();
            _dirty.Clear();
            _runningJobs = 0;

            while (_completed.TryDequeue(out _))
            {
            }

            _generator = null;
        }

        // ------------------------------------------------------------
        // Streaming API
        // ------------------------------------------------------------

        /// <summary>
        /// Sets which chunks should be visible (and collidable), ordered
        /// by priority - nearest first. Data for one ring around them is
        /// generated so meshes can see their neighbours; everything
        /// further away is unloaded unless a player modified it.
        /// </summary>
        public void SetWantedChunks(IReadOnlyList<ChunkCoord> orderedByPriority)
        {
            _wantedViews.Clear();
            _wantedViewSet.Clear();
            _keepData.Clear();

            for (int i = 0; i < orderedByPriority.Count; i++)
            {
                ChunkCoord coord = orderedByPriority[i];

                if (_wantedViewSet.Add(coord))
                    _wantedViews.Add(coord);

                // Keep two rings of data around visible chunks: one is
                // needed for meshing, the second avoids churn at the edge.
                for (int dz = -2; dz <= 2; dz++)
                for (int dx = -2; dx <= 2; dx++)
                    _keepData.Add(new ChunkCoord(coord.X + dx, coord.Z + dz));
            }

            RemoveUnwantedViews();
            UnloadUnusedData();
        }

        /// <summary>Unordered convenience overload.</summary>
        public void UpdateVisibleChunks(IEnumerable<ChunkCoord> desiredCoords)
        {
            SetWantedChunks(new List<ChunkCoord>(desiredCoords));
        }

        /// <summary>
        /// Generates and meshes a chunk (and its neighbours) right now on
        /// the main thread if it has no collider yet. Used under players
        /// so they never fall through terrain that is still loading.
        /// </summary>
        public void EnsureChunkReadyNow(ChunkCoord coord)
        {
            if (!IsReady)
                return;

            if (_views.TryGetValue(coord, out ChunkView existing) &&
                existing != null &&
                !_dirty.Contains(coord))
            {
                return;
            }

            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
                GetOrCreateChunk(new ChunkCoord(coord.X + dx, coord.Z + dz));

            ChunkNeighborhood neighbourhood = CollectNeighbourhood(coord);
            ChunkMeshResult result = ChunkMeshBuilder.Build(neighbourhood, _renderingEnabled, Seed);

            ApplyMesh(result);
        }

        /// <summary>
        /// Generates every chunk around a world position immediately.
        /// Call before placing a player there.
        /// </summary>
        public void EnsureAreaReadyNow(Vector3 position, int radiusInChunks = 1)
        {
            ChunkCoord center = WorldToChunkCoord(
                Mathf.FloorToInt(position.x),
                Mathf.FloorToInt(position.z));

            for (int dz = -radiusInChunks; dz <= radiusInChunks; dz++)
            for (int dx = -radiusInChunks; dx <= radiusInChunks; dx++)
                EnsureChunkReadyNow(new ChunkCoord(center.X + dx, center.Z + dz));
        }

        // ------------------------------------------------------------
        // Block access
        // ------------------------------------------------------------

        /// <summary>
        /// Height of the topmost non-air block (water counts).
        /// Generates the chunk if needed.
        /// </summary>
        public int GetSurfaceHeight(
            int worldX,
            int worldZ)
        {
            return FindTop(worldX, worldZ, solidOnly: false);
        }

        /// <summary>Height of the topmost block a player can stand on.</summary>
        public int GetGroundHeight(
            int worldX,
            int worldZ)
        {
            return FindTop(worldX, worldZ, solidOnly: true);
        }

        /// <summary>
        /// Finds a dry, flat spot for a player near the preferred position
        /// (never in an ocean or river) and makes sure the terrain around
        /// it exists. Returns the position just above the ground.
        /// </summary>
        public Vector3 FindSpawnPosition(Vector3 preferred, int searchRadius = 1024)
        {
            if (!EnsureReady())
                return preferred;

            int originX = Mathf.FloorToInt(preferred.x);
            int originZ = Mathf.FloorToInt(preferred.z);
            int sea = _generator.Config.SeaLevel;

            const int step = 8;

            int bestX = originX;
            int bestZ = originZ;
            bool found = false;

            // Spiral outwards on a coarse grid using cheap column samples.
            for (int ring = 0; ring * step <= searchRadius && !found; ring++)
            {
                for (int i = -ring; i <= ring && !found; i++)
                for (int j = -ring; j <= ring && !found; j++)
                {
                    if (Math.Abs(i) != ring && Math.Abs(j) != ring)
                        continue;

                    int x = originX + i * step;
                    int z = originZ + j * step;

                    ColumnSample column = _generator.SampleColumn(x, z);

                    if (column.Height <= sea + 1 ||
                        column.Biome == BiomeId.Ocean ||
                        column.Biome == BiomeId.FrozenOcean ||
                        column.IsRiver ||
                        column.Height > sea + 60)
                    {
                        continue;
                    }

                    bestX = x;
                    bestZ = z;
                    found = true;
                }
            }

            int ground = GetGroundHeight(bestX, bestZ);
            var position = new Vector3(bestX + 0.5f, ground + 1, bestZ + 0.5f);

            EnsureAreaReadyNow(position);

            return position;
        }

        public ChunkData GetOrCreateChunk(
            ChunkCoord coord)
        {
            if (_chunks.TryGetValue(coord, out ChunkData existing))
                return existing;

            if (!EnsureReady())
            {
                throw new InvalidOperationException(
                    "The world has no seed yet. On clients, wait for " +
                    "WorldManager.IsReady before accessing blocks.");
            }

            ChunkData chunk = CreateChunkData(coord);
            _generator.GenerateChunk(chunk);
            PublishChunk(chunk);

            return chunk;
        }

        public bool TryGetChunk(
            ChunkCoord coord,
            out ChunkData chunk)
        {
            return _chunks.TryGetValue(coord, out chunk);
        }

        /// <summary>
        /// Block at a position if its chunk is loaded; never generates.
        /// Unloaded chunks report <paramref name="block"/> = Air and
        /// return false. Cheap enough for per-tick physics queries.
        /// </summary>
        public bool TryGetLoadedBlock(
            int worldX,
            int worldY,
            int worldZ,
            out BlockId block)
        {
            block = BlockId.Air;

            if (!IsReady ||
                worldY < 0 ||
                worldY >= Config.WorldHeight)
            {
                return IsReady;
            }

            ChunkCoord coord = WorldToChunkCoord(worldX, worldZ);

            if (!_chunks.TryGetValue(coord, out ChunkData chunk))
                return false;

            WorldToLocalPosition(worldX, worldZ, coord, out int localX, out int localZ);
            block = chunk.GetBlock(localX, worldY, localZ);
            return true;
        }

        public BlockId GetBlock(
            int worldX,
            int worldY,
            int worldZ)
        {
            if (!IsReady ||
                worldY < 0 ||
                worldY >= Config.WorldHeight)
            {
                return BlockId.Air;
            }

            ChunkCoord coord = WorldToChunkCoord(worldX, worldZ);
            ChunkData chunk = GetOrCreateChunk(coord);

            WorldToLocalPosition(worldX, worldZ, coord, out int localX, out int localZ);

            return chunk.GetBlock(localX, worldY, localZ);
        }

        /// <summary>
        /// Changes a block and schedules the affected chunk meshes for
        /// rebuilding. Server-authoritative block editing should call this
        /// on the server and replicate the change to clients.
        /// </summary>
        public void SetBlock(
            int worldX,
            int worldY,
            int worldZ,
            BlockId block)
        {
            if (!IsReady ||
                worldY < 0 ||
                worldY >= Config.WorldHeight)
            {
                return;
            }

            ChunkCoord coord = WorldToChunkCoord(worldX, worldZ);
            ChunkData chunk = GetOrCreateChunk(coord);

            WorldToLocalPosition(worldX, worldZ, coord, out int localX, out int localZ);

            if (chunk.GetBlock(localX, worldY, localZ) == block)
                return;

            chunk.SetBlock(localX, worldY, localZ, block);

            // Rebuild this chunk, and neighbours whose faces or ambient
            // occlusion touch the changed block.
            int size = Config.ChunkSize;
            int minDx = localX == 0 ? -1 : 0;
            int maxDx = localX == size - 1 ? 1 : 0;
            int minDz = localZ == 0 ? -1 : 0;
            int maxDz = localZ == size - 1 ? 1 : 0;

            for (int dz = minDz; dz <= maxDz; dz++)
            for (int dx = minDx; dx <= maxDx; dx++)
            {
                var affected = new ChunkCoord(coord.X + dx, coord.Z + dz);

                if (_views.ContainsKey(affected))
                    _dirty.Add(affected);
            }
        }

        public ChunkCoord WorldToChunkCoord(
            int worldX,
            int worldZ)
        {
            int size = settings.ChunkSize;

            return new ChunkCoord(
                FloorDiv(worldX, size),
                FloorDiv(worldZ, size));
        }

        // ------------------------------------------------------------
        // Pipeline
        // ------------------------------------------------------------

        private bool EnsureReady()
        {
            if (!IsReady)
                TryInitializeFromNetwork();

            return IsReady;
        }

        private ChunkData CreateChunkData(ChunkCoord coord)
        {
            TerrainConfig config = _generator.Config;

            return new ChunkData(
                coord,
                config.ChunkSize,
                config.WorldHeight,
                config.ChunkSize);
        }

        private void PublishChunk(ChunkData chunk)
        {
            ChunkCoord coord = chunk.Coord;
            _chunks[coord] = chunk;

            // Neighbours meshed while this chunk was missing treated it
            // as solid; rebuild them now that the real blocks exist.
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0)
                    continue;

                var neighbour = new ChunkCoord(coord.X + dx, coord.Z + dz);

                if (_views.ContainsKey(neighbour))
                    _dirty.Add(neighbour);
            }
        }

        private void ScheduleJobs()
        {
            if (_wantedViews.Count == 0)
                return;

            // Pass 1, nearest first: mesh chunks whose neighbourhood is
            // complete, or generate the missing data.
            for (int i = 0; i < _wantedViews.Count && _runningJobs < maxConcurrentJobs; i++)
            {
                ChunkCoord coord = _wantedViews[i];

                bool needsMesh =
                    !_views.ContainsKey(coord) ||
                    _dirty.Contains(coord);

                if (!needsMesh || _meshing.Contains(coord))
                    continue;

                if (HasCompleteNeighbourhood(coord))
                {
                    ScheduleMesh(coord);
                    continue;
                }

                for (int dz = -1; dz <= 1 && _runningJobs < maxConcurrentJobs; dz++)
                for (int dx = -1; dx <= 1 && _runningJobs < maxConcurrentJobs; dx++)
                {
                    var needed = new ChunkCoord(coord.X + dx, coord.Z + dz);

                    if (!_chunks.ContainsKey(needed) && !_generating.Contains(needed))
                        ScheduleGeneration(needed);
                }
            }
        }

        private void ScheduleGeneration(ChunkCoord coord)
        {
            _generating.Add(coord);
            _runningJobs++;

            int epoch = _epoch;
            IWorldGenerator generator = _generator;
            ChunkData chunk = CreateChunkData(coord);

            Task.Run(() =>
            {
                var result = new JobResult
                {
                    Kind = JobKind.Generate,
                    Epoch = epoch,
                    Coord = coord
                };

                try
                {
                    generator.GenerateChunk(chunk);
                    result.Chunk = chunk;
                }
                catch (Exception exception)
                {
                    result.Error = exception;
                }

                _completed.Enqueue(result);
            });
        }

        private void ScheduleMesh(ChunkCoord coord)
        {
            _meshing.Add(coord);
            _dirty.Remove(coord);
            _runningJobs++;

            int epoch = _epoch;
            bool buildLiquid = _renderingEnabled;
            long seed = Seed;
            ChunkNeighborhood neighbourhood = CollectNeighbourhood(coord);

            Task.Run(() =>
            {
                var result = new JobResult
                {
                    Kind = JobKind.Mesh,
                    Epoch = epoch,
                    Coord = coord
                };

                try
                {
                    result.Mesh = ChunkMeshBuilder.Build(neighbourhood, buildLiquid, seed);
                }
                catch (Exception exception)
                {
                    result.Error = exception;
                }

                _completed.Enqueue(result);
            });
        }

        private void ProcessCompletedJobs()
        {
            int uploads = 0;

            // Generation results are cheap to publish; mesh uploads are
            // limited per frame. Leave remaining meshes for next frame.
            int count = _completed.Count;

            for (int i = 0; i < count; i++)
            {
                if (!_completed.TryDequeue(out JobResult result))
                    break;

                if (result.Epoch != _epoch)
                    continue;

                if (result.Kind == JobKind.Mesh && uploads >= maxMeshUploadsPerFrame)
                {
                    // Re-queue at the back; still counts as running.
                    _completed.Enqueue(result);
                    continue;
                }

                _runningJobs = Math.Max(0, _runningJobs - 1);

                if (result.Error != null)
                {
                    Debug.LogException(result.Error);
                    _generating.Remove(result.Coord);
                    _meshing.Remove(result.Coord);
                    continue;
                }

                if (result.Kind == JobKind.Generate)
                {
                    _generating.Remove(result.Coord);

                    // A synchronous request may have generated it already,
                    // or it may no longer be needed.
                    if (!_chunks.ContainsKey(result.Coord) && _keepData.Contains(result.Coord))
                        PublishChunk(result.Chunk);
                }
                else
                {
                    _meshing.Remove(result.Coord);

                    if (!_wantedViewSet.Contains(result.Coord))
                        continue;

                    ApplyMesh(result.Mesh);
                    uploads++;
                }
            }
        }

        private void ApplyMesh(ChunkMeshResult result)
        {
            ChunkCoord coord = result.Coord;

            if (!_views.TryGetValue(coord, out ChunkView view) || view == null)
            {
                var chunkObject = new GameObject($"Chunk {coord.X}, {coord.Z}");
                chunkObject.transform.SetParent(transform, false);

                view = chunkObject.AddComponent<ChunkView>();
                view.Initialize(
                    coord,
                    Config.ChunkSize,
                    Config.ChunkSize,
                    chunkMaterial,
                    waterMaterial,
                    _renderingEnabled);

                _views[coord] = view;
            }

            view.ApplyMesh(result);

            // Something changed while the mesh was being built.
            ChunkNeighborhood current = CollectNeighbourhood(coord);
            if (current.CombinedVersion != result.SourceVersion || !HasCompleteNeighbourhood(coord))
                _dirty.Add(coord);
        }

        private bool HasCompleteNeighbourhood(ChunkCoord coord)
        {
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (!_chunks.ContainsKey(new ChunkCoord(coord.X + dx, coord.Z + dz)))
                    return false;
            }

            return true;
        }

        private ChunkNeighborhood CollectNeighbourhood(ChunkCoord coord)
        {
            var chunks = new ChunkData[9];

            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                _chunks.TryGetValue(new ChunkCoord(coord.X + dx, coord.Z + dz), out ChunkData chunk);
                chunks[(dx + 1) + (dz + 1) * 3] = chunk;
            }

            return new ChunkNeighborhood(chunks);
        }

        private void RemoveUnwantedViews()
        {
            _scratch.Clear();

            foreach (ChunkCoord coord in _views.Keys)
            {
                if (!_wantedViewSet.Contains(coord))
                    _scratch.Add(coord);
            }

            for (int i = 0; i < _scratch.Count; i++)
            {
                ChunkCoord coord = _scratch[i];

                if (_views.TryGetValue(coord, out ChunkView view) && view != null)
                    Destroy(view.gameObject);

                _views.Remove(coord);
                _dirty.Remove(coord);
            }
        }

        private void UnloadUnusedData()
        {
            _scratch.Clear();

            foreach (KeyValuePair<ChunkCoord, ChunkData> entry in _chunks)
            {
                // Player-modified chunks stay until world saving exists.
                if (!_keepData.Contains(entry.Key) && !entry.Value.IsModified)
                    _scratch.Add(entry.Key);
            }

            for (int i = 0; i < _scratch.Count; i++)
                _chunks.Remove(_scratch[i]);
        }

        private int FindTop(int worldX, int worldZ, bool solidOnly)
        {
            if (!EnsureReady())
                return 0;

            for (int y = Config.WorldHeight - 1; y >= 0; y--)
            {
                BlockId block = GetBlock(worldX, y, worldZ);

                if (block == BlockId.Air)
                    continue;

                if (!solidOnly || BlockRegistry.IsSolid(block))
                    return y;
            }

            return 0;
        }

        private void WorldToLocalPosition(
            int worldX,
            int worldZ,
            ChunkCoord coord,
            out int localX,
            out int localZ)
        {
            int size = settings.ChunkSize;

            localX = worldX - coord.X * size;
            localZ = worldZ - coord.Z * size;
        }

        private static int FloorDiv(
            int value,
            int divisor)
        {
            int quotient = value / divisor;
            int remainder = value % divisor;

            if (remainder != 0 &&
                ((remainder < 0) != (divisor < 0)))
            {
                quotient--;
            }

            return quotient;
        }

        private enum JobKind
        {
            Generate,
            Mesh
        }

        private sealed class JobResult
        {
            public JobKind Kind;
            public int Epoch;
            public ChunkCoord Coord;
            public ChunkData Chunk;
            public ChunkMeshResult Mesh;
            public Exception Error;
        }
    }
}
