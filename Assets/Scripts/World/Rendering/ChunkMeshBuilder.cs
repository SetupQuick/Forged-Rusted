using System.Collections.Generic;
using UnityEngine;
using World.Blocks;
using World.Chunks;
using World.Generation;
using World.Generation.Noise;

namespace World.Rendering
{
    /// <summary>
    /// A chunk plus its eight horizontal neighbours, so faces and
    /// ambient occlusion at chunk edges can see across the border.
    /// Immutable view; safe to read on a worker thread.
    /// </summary>
    public readonly struct ChunkNeighborhood
    {
        // Index = (dx + 1) + (dz + 1) * 3, center at 4.
        private readonly ChunkData[] _chunks;

        public ChunkData Center => _chunks[4];

        public ChunkNeighborhood(ChunkData[] chunks)
        {
            _chunks = chunks;
        }

        public ChunkData Get(int dx, int dz)
        {
            return _chunks[(dx + 1) + (dz + 1) * 3];
        }

        /// <summary>Sum of all versions, to notice edits made during meshing.</summary>
        public long CombinedVersion
        {
            get
            {
                long sum = 0;
                for (int i = 0; i < _chunks.Length; i++)
                    sum += _chunks[i]?.Version ?? 0;
                return sum;
            }
        }

        /// <summary>Block at a position relative to the center chunk (x, z may be -1..size).</summary>
        public BlockId GetBlock(int x, int y, int z)
        {
            ChunkData center = _chunks[4];

            if (y < 0 || y >= center.SizeY)
                return BlockId.Air;

            int dx = 0;
            int dz = 0;

            if (x < 0) { dx = -1; x += center.SizeX; }
            else if (x >= center.SizeX) { dx = 1; x -= center.SizeX; }

            if (z < 0) { dz = -1; z += center.SizeZ; }
            else if (z >= center.SizeZ) { dz = 1; z -= center.SizeZ; }

            ChunkData chunk = _chunks[(dx + 1) + (dz + 1) * 3];

            // Missing neighbours count as solid, so no faces are built
            // against unloaded chunks (they are rebuilt when it arrives).
            return chunk == null ? BlockId.Stone : chunk.GetBlock(x, y, z);
        }
    }

    /// <summary>
    /// Plain mesh arrays built on a worker thread and uploaded to a
    /// Unity Mesh on the main thread.
    /// </summary>
    public sealed class ChunkMeshData
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Color32> Colors = new List<Color32>();
        public readonly List<int> Triangles = new List<int>();

        public bool IsEmpty => Vertices.Count == 0;
    }

    public sealed class ChunkMeshResult
    {
        public ChunkCoord Coord;
        public ChunkMeshData Solid;
        public ChunkMeshData Liquid;
        public long SourceVersion;
    }

    /// <summary>
    /// Turns block data into a mesh: one quad per visible block face,
    /// coloured per block and biome, with baked per-vertex ambient
    /// occlusion so corners and crevices read well without textures.
    ///
    /// Pure function of the input chunks. Safe on worker threads.
    /// </summary>
    public static class ChunkMeshBuilder
    {
        private const float WaterSurfaceDrop = 0.12f;

        // AO brightness by number of occluders (0..3).
        private static readonly float[] AoCurve = { 1.0f, 0.84f, 0.71f, 0.58f };

        private enum Face
        {
            Up,
            Down,
            Left,
            Right,
            Forward,
            Back
        }

        private static readonly Vector3Int[] FaceNormals =
        {
            new Vector3Int(0, 1, 0),
            new Vector3Int(0, -1, 0),
            new Vector3Int(-1, 0, 0),
            new Vector3Int(1, 0, 0),
            new Vector3Int(0, 0, 1),
            new Vector3Int(0, 0, -1)
        };

        // Corner positions (0/1 per axis) for each face, in winding order.
        private static readonly Vector3Int[][] FaceCorners =
        {
            new[] { new Vector3Int(0, 1, 0), new Vector3Int(0, 1, 1), new Vector3Int(1, 1, 1), new Vector3Int(1, 1, 0) },
            new[] { new Vector3Int(0, 0, 1), new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0), new Vector3Int(1, 0, 1) },
            new[] { new Vector3Int(0, 0, 0), new Vector3Int(0, 0, 1), new Vector3Int(0, 1, 1), new Vector3Int(0, 1, 0) },
            new[] { new Vector3Int(1, 0, 1), new Vector3Int(1, 0, 0), new Vector3Int(1, 1, 0), new Vector3Int(1, 1, 1) },
            new[] { new Vector3Int(0, 0, 1), new Vector3Int(1, 0, 1), new Vector3Int(1, 1, 1), new Vector3Int(0, 1, 1) },
            new[] { new Vector3Int(1, 0, 0), new Vector3Int(0, 0, 0), new Vector3Int(0, 1, 0), new Vector3Int(1, 1, 0) }
        };

        public static ChunkMeshResult Build(ChunkNeighborhood neighborhood, bool buildLiquid, long seed)
        {
            ChunkData chunk = neighborhood.Center;

            var result = new ChunkMeshResult
            {
                Coord = chunk.Coord,
                Solid = new ChunkMeshData(),
                Liquid = buildLiquid ? new ChunkMeshData() : null,
                SourceVersion = neighborhood.CombinedVersion
            };

            int maxY = chunk.HighestBlockY;
            int originX = chunk.Coord.X * chunk.SizeX;
            int originZ = chunk.Coord.Z * chunk.SizeZ;

            for (int y = 0; y <= maxY; y++)
            for (int z = 0; z < chunk.SizeZ; z++)
            for (int x = 0; x < chunk.SizeX; x++)
            {
                BlockId block = chunk.GetBlock(x, y, z);

                if (block == BlockId.Air)
                    continue;

                if (BlockRegistry.IsLiquid(block))
                {
                    if (buildLiquid)
                        AddLiquidBlock(neighborhood, result.Liquid, block, x, y, z);

                    continue;
                }

                BlockDefinition definition = BlockRegistry.Get(block);
                BiomeDefinition biome = BiomeRegistry.Get((BiomeId)chunk.GetBiome(x, z));

                // Per-block brightness wobble plus a slight colour shift
                // gives the untextured voxels a hand-painted look.
                Vector3 variation = new Vector3(1f, 1f, 1f);
                if (definition.ColorNoise > 0)
                {
                    ulong hash = SeedHash.Hash(seed, originX + x, y, originZ + z, 0x5EED);
                    float brightness = 1f + (float)(SeedHash.ToUnit(hash) * 2 - 1) * definition.ColorNoise;
                    float shift = definition.ColorNoise * 0.35f;

                    variation = new Vector3(
                        brightness * (1f + (((hash >> 8) & 0xFF) / 255f * 2f - 1f) * shift),
                        brightness * (1f + (((hash >> 16) & 0xFF) / 255f * 2f - 1f) * shift),
                        brightness * (1f + (((hash >> 24) & 0xFF) / 255f * 2f - 1f) * shift));
                }

                for (int f = 0; f < 6; f++)
                {
                    Vector3Int n = FaceNormals[f];
                    BlockId neighbour = neighborhood.GetBlock(x + n.x, y + n.y, z + n.z);

                    if (BlockRegistry.IsOpaque(neighbour))
                        continue;

                    Color32 color = FaceColor(definition, biome, (Face)f, variation);
                    AddSolidFace(neighborhood, result.Solid, (Face)f, x, y, z, color);
                }
            }

            return result;
        }

        private static Color32 FaceColor(BlockDefinition definition, BiomeDefinition biome, Face face, Vector3 variation)
        {
            BlockColor baseColor =
                face == Face.Up ? definition.TopColor :
                face == Face.Down ? definition.BottomColor :
                definition.SideColor;

            float r = baseColor.R;
            float g = baseColor.G;
            float b = baseColor.B;

            BlockColor? tint = null;

            if (definition.Tint == BiomeTint.Grass && face != Face.Down)
                tint = biome.GrassTint;
            else if (definition.Tint == BiomeTint.Foliage)
                tint = biome.FoliageTint;

            if (tint.HasValue)
            {
                r = r * tint.Value.R / 255f;
                g = g * tint.Value.G / 255f;
                b = b * tint.Value.B / 255f;
            }

            return new Color32(
                ClampByte(r * variation.x),
                ClampByte(g * variation.y),
                ClampByte(b * variation.z),
                255);
        }

        private static void AddSolidFace(
            ChunkNeighborhood neighborhood,
            ChunkMeshData mesh,
            Face face,
            int x,
            int y,
            int z,
            Color32 color)
        {
            Vector3Int normal = FaceNormals[(int)face];
            Vector3Int[] corners = FaceCorners[(int)face];

            int start = mesh.Vertices.Count;
            int ao0 = 0, ao1 = 0, ao2 = 0, ao3 = 0;

            for (int i = 0; i < 4; i++)
            {
                Vector3Int c = corners[i];
                int ao = VertexOcclusion(neighborhood, x, y, z, normal, c);

                switch (i)
                {
                    case 0: ao0 = ao; break;
                    case 1: ao1 = ao; break;
                    case 2: ao2 = ao; break;
                    default: ao3 = ao; break;
                }

                float shade = AoCurve[ao];

                mesh.Vertices.Add(new Vector3(x + c.x, y + c.y, z + c.z));
                mesh.Normals.Add(normal);
                mesh.Colors.Add(new Color32(
                    (byte)(color.r * shade),
                    (byte)(color.g * shade),
                    (byte)(color.b * shade),
                    255));
            }

            // Flip the quad's diagonal so AO gradients do not look skewed.
            if (ao0 + ao2 > ao1 + ao3)
            {
                mesh.Triangles.Add(start + 1);
                mesh.Triangles.Add(start + 2);
                mesh.Triangles.Add(start + 3);
                mesh.Triangles.Add(start + 1);
                mesh.Triangles.Add(start + 3);
                mesh.Triangles.Add(start + 0);
            }
            else
            {
                mesh.Triangles.Add(start + 0);
                mesh.Triangles.Add(start + 1);
                mesh.Triangles.Add(start + 2);
                mesh.Triangles.Add(start + 0);
                mesh.Triangles.Add(start + 2);
                mesh.Triangles.Add(start + 3);
            }
        }

        /// <summary>
        /// Number of opaque blocks (0..3) touching a face corner from the
        /// outside: the two edge neighbours and the diagonal one.
        /// </summary>
        private static int VertexOcclusion(
            ChunkNeighborhood neighborhood,
            int x,
            int y,
            int z,
            Vector3Int normal,
            Vector3Int corner)
        {
            // The two axes lying in the face plane.
            Vector3Int a, b;

            if (normal.x != 0) { a = new Vector3Int(0, 1, 0); b = new Vector3Int(0, 0, 1); }
            else if (normal.y != 0) { a = new Vector3Int(1, 0, 0); b = new Vector3Int(0, 0, 1); }
            else { a = new Vector3Int(1, 0, 0); b = new Vector3Int(0, 1, 0); }

            int signA = Dot(corner, a) * 2 - 1;
            int signB = Dot(corner, b) * 2 - 1;

            int bx = x + normal.x;
            int by = y + normal.y;
            int bz = z + normal.z;

            bool side1 = IsOccluder(neighborhood, bx + a.x * signA, by + a.y * signA, bz + a.z * signA);
            bool side2 = IsOccluder(neighborhood, bx + b.x * signB, by + b.y * signB, bz + b.z * signB);

            if (side1 && side2)
                return 3;

            bool diagonal = IsOccluder(
                neighborhood,
                bx + a.x * signA + b.x * signB,
                by + a.y * signA + b.y * signB,
                bz + a.z * signA + b.z * signB);

            return (side1 ? 1 : 0) + (side2 ? 1 : 0) + (diagonal ? 1 : 0);
        }

        private static void AddLiquidBlock(
            ChunkNeighborhood neighborhood,
            ChunkMeshData mesh,
            BlockId block,
            int x,
            int y,
            int z)
        {
            BlockDefinition definition = BlockRegistry.Get(block);
            bool liquidAbove = BlockRegistry.IsLiquid(neighborhood.GetBlock(x, y + 1, z));
            float top = liquidAbove ? 1f : 1f - WaterSurfaceDrop;

            for (int f = 0; f < 6; f++)
            {
                Vector3Int n = FaceNormals[f];
                BlockId neighbour = neighborhood.GetBlock(x + n.x, y + n.y, z + n.z);

                // Water only shows where it meets air (or see-through blocks).
                if (BlockRegistry.IsLiquid(neighbour) || BlockRegistry.IsOpaque(neighbour))
                    continue;

                Face face = (Face)f;
                BlockColor c = face == Face.Up ? definition.TopColor : definition.SideColor;
                var color = new Color32(c.R, c.G, c.B, 255);

                Vector3Int[] corners = FaceCorners[f];
                int start = mesh.Vertices.Count;

                for (int i = 0; i < 4; i++)
                {
                    Vector3Int corner = corners[i];
                    float cy = corner.y == 1 ? top : 0f;

                    mesh.Vertices.Add(new Vector3(x + corner.x, y + cy, z + corner.z));
                    mesh.Normals.Add(n);
                    mesh.Colors.Add(color);
                }

                mesh.Triangles.Add(start + 0);
                mesh.Triangles.Add(start + 1);
                mesh.Triangles.Add(start + 2);
                mesh.Triangles.Add(start + 0);
                mesh.Triangles.Add(start + 2);
                mesh.Triangles.Add(start + 3);
            }
        }

        private static bool IsOccluder(ChunkNeighborhood neighborhood, int x, int y, int z)
        {
            return BlockRegistry.IsOpaque(neighborhood.GetBlock(x, y, z));
        }

        private static int Dot(Vector3Int a, Vector3Int b)
        {
            return a.x * b.x + a.y * b.y + a.z * b.z;
        }

        private static byte ClampByte(float value)
        {
            return value <= 0 ? (byte)0 : value >= 255 ? (byte)255 : (byte)value;
        }
    }
}
