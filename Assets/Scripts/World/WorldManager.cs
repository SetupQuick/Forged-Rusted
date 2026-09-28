using System;
using System.Collections.Generic;
using UnityEngine;
using World.Blocks;
using World.Chunks;
using World.Generation;
using World.Rendering;
using System.Collections.Generic;

namespace World
{
    public sealed class WorldManager
        : MonoBehaviour
    {
        [SerializeField]
        private WorldSettings settings;

        [Header("Rendering")]
        [SerializeField]
        private Material chunkMaterial;

        private readonly Dictionary<
            ChunkCoord,
            ChunkData> _chunks =
            new Dictionary<
                ChunkCoord,
                ChunkData>();

        private IWorldGenerator _generator;

        public WorldSettings Settings =>
            settings;

        public int LoadedChunkCount =>
            _chunks.Count;

        private void Awake()
        {
            if (settings == null)
            {
                throw new InvalidOperationException(
                    "Assign WorldSettings to WorldManager.");
            }

            _generator =
                new NoiseWorldGenerator();
        }

        private readonly Dictionary<
    ChunkCoord,
    ChunkView> _views =
        new Dictionary<
            ChunkCoord,
            ChunkView>();

        private void Start()
        {
            const int radius = 1;

            for (int x = -radius;
                 x <= radius;
                 x++)
            {
                for (int z = -radius;
                     z <= radius;
                     z++)
                {
                    CreateChunkView(
                        new ChunkCoord(
                            x,
                            z));
                }
            }
        }

        private void CreateChunkView(
    ChunkCoord coord)
        {
            if (_views.ContainsKey(coord))
                return;

            ChunkData chunk =
                GetOrCreateChunk(coord);

            GameObject chunkObject =
                new GameObject(
                    $"Chunk {coord.X}, {coord.Z}");

            chunkObject.transform.SetParent(
                transform,
                false);

            ChunkView view =
                chunkObject.AddComponent<ChunkView>();

            view.Initialize(
                chunk,
                this,
                chunkMaterial);

            _views.Add(
                coord,
                view);
        }

        private void RemoveChunkView(
    ChunkCoord coord)
        {
            if (!_views.TryGetValue(
                coord,
                out ChunkView view))
            {
                return;
            }

            _views.Remove(coord);

            if (view != null)
            {
                Destroy(
                    view.gameObject);
            }
        }

        public void UpdateVisibleChunks(
    IEnumerable<ChunkCoord> desiredCoords)
        {
            var desired =
                new HashSet<ChunkCoord>(
                    desiredCoords);

            var toRemove =
                new List<ChunkCoord>();

            foreach (
                KeyValuePair<
                    ChunkCoord,
                    ChunkView> entry
                in _views)
            {
                if (!desired.Contains(
                    entry.Key))
                {
                    toRemove.Add(
                        entry.Key);
                }
            }

            for (int i = 0;
                 i < toRemove.Count;
                 i++)
            {
                RemoveChunkView(
                    toRemove[i]);
            }

            foreach (
                ChunkCoord coord
                in desired)
            {
                if (_views.ContainsKey(
                    coord))
                {
                    continue;
                }

                CreateChunkView(coord);
            }
        }

        public int GetSurfaceHeight(
    int worldX,
    int worldZ)
        {
            for (int y = settings.WorldHeight - 1;
                 y >= 0;
                 y--)
            {
                BlockId block =
                    GetBlock(
                        worldX,
                        y,
                        worldZ);

                if (block != BlockId.Air)
                    return y;
            }

            return 0;
        }

        public ChunkData GetOrCreateChunk(
            ChunkCoord coord)
        {
            if (_chunks.TryGetValue(
                coord,
                out ChunkData existing))
            {
                return existing;
            }

            int chunkSize =
                settings.ChunkSize;

            var chunk =
                new ChunkData(
                    coord,
                    chunkSize,
                    settings.WorldHeight,
                    chunkSize);

            _generator.GenerateChunk(
                chunk,
                settings);

            _chunks.Add(
                coord,
                chunk);

            return chunk;
        }

        public bool TryGetChunk(
            ChunkCoord coord,
            out ChunkData chunk)
        {
            return _chunks.TryGetValue(
                coord,
                out chunk);
        }

        public BlockId GetBlock(
            int worldX,
            int worldY,
            int worldZ)
        {
            if (worldY < 0 ||
                worldY >=
                settings.WorldHeight)
            {
                return BlockId.Air;
            }

            ChunkCoord coord =
                WorldToChunkCoord(
                    worldX,
                    worldZ);

            ChunkData chunk =
                GetOrCreateChunk(coord);

            WorldToLocalPosition(
                worldX,
                worldZ,
                coord,
                out int localX,
                out int localZ);

            return chunk.GetBlock(
                localX,
                worldY,
                localZ);
        }

        public void SetBlock(
            int worldX,
            int worldY,
            int worldZ,
            BlockId block)
        {
            if (worldY < 0 ||
                worldY >=
                settings.WorldHeight)
            {
                return;
            }

            ChunkCoord coord =
                WorldToChunkCoord(
                    worldX,
                    worldZ);

            ChunkData chunk =
                GetOrCreateChunk(coord);

            WorldToLocalPosition(
                worldX,
                worldZ,
                coord,
                out int localX,
                out int localZ);

            chunk.SetBlock(
                localX,
                worldY,
                localZ,
                block);
        }

        public ChunkCoord WorldToChunkCoord(
            int worldX,
            int worldZ)
        {
            int size =
                settings.ChunkSize;

            int chunkX =
                FloorDiv(
                    worldX,
                    size);

            int chunkZ =
                FloorDiv(
                    worldZ,
                    size);

            return new ChunkCoord(
                chunkX,
                chunkZ);
        }

        private void WorldToLocalPosition(
            int worldX,
            int worldZ,
            ChunkCoord coord,
            out int localX,
            out int localZ)
        {
            int size =
                settings.ChunkSize;

            localX =
                worldX -
                coord.X *
                size;

            localZ =
                worldZ -
                coord.Z *
                size;
        }

        private static int FloorDiv(
            int value,
            int divisor)
        {
            int quotient =
                value / divisor;

            int remainder =
                value % divisor;

            if (remainder != 0 &&
                ((remainder < 0) !=
                 (divisor < 0)))
            {
                quotient--;
            }

            return quotient;
        }
    }
}