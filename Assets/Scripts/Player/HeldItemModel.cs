using Items;
using UnityEngine;
using World.Blocks;
using World.Generation;
using World.Rendering;

namespace Networking
{
    /// <summary>
    /// The model of an item held in the hand, used by both the first-person
    /// arms and other players' avatars. Items with a <c>heldPrefab</c> use
    /// it (tools later); blocks get a small voxel cube in their colours;
    /// anything else a coloured tile.
    /// </summary>
    public static class HeldItemModel
    {
        /// <summary>Edge length of a held block in metres.</summary>
        public const float BlockSize = 0.22f;

        /// <summary>
        /// Creates the held model under <paramref name="parent"/> (grip at
        /// the parent's origin). Returns null for an empty hand.
        /// </summary>
        public static GameObject Create(ItemData item, Transform parent, Material material, bool castShadows)
        {
            if (item == null)
                return null;

            GameObject model;

            if (item.heldPrefab != null)
            {
                model = Object.Instantiate(item.heldPrefab, parent, false);
            }
            else
            {
                model = new GameObject("Held " + item.DisplayName);
                model.transform.SetParent(parent, false);

                string[] shape = ItemShapes.For(item);
                ChunkMeshData data = item.placesBlock ? BlockCube(item.block)
                    : shape != null ? Shape(shape, item)
                    : Tile(item.Id);

                var mesh = new Mesh { name = model.name };
                mesh.SetVertices(data.Vertices);
                mesh.SetNormals(data.Normals);
                mesh.SetColors(data.Colors);
                mesh.SetTriangles(data.Triangles, 0, true);

                model.AddComponent<MeshFilter>().sharedMesh = mesh;
                model.AddComponent<MeshRenderer>().sharedMaterial = material;
            }

            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
                renderer.shadowCastingMode = castShadows
                    ? UnityEngine.Rendering.ShadowCastingMode.On
                    : UnityEngine.Rendering.ShadowCastingMode.Off;

            return model;
        }

        /// <summary>A block as a small cube of 4x4x4 voxels with per-voxel colour variation.</summary>
        public static ChunkMeshData BlockCube(BlockId block)
        {
            BlockDefinition definition = BlockRegistry.Get(block);
            BiomeDefinition plains = BiomeRegistry.Get(BiomeId.Plains);

            const int n = 4;
            float v = BlockSize / n;
            var mesh = new ChunkMeshData();

            for (int x = 0; x < n; x++)
            for (int y = 0; y < n; y++)
            for (int z = 0; z < n; z++)
            {
                bool surface = x == 0 || y == 0 || z == 0 || x == n - 1 || y == n - 1 || z == n - 1;
                if (!surface)
                    continue;

                uint hash = (uint)(x * 73856093 ^ y * 19349663 ^ z * 83492791 ^ (int)block * 2654435);
                float shade = 1f + ((hash % 100) / 100f - 0.5f) * definition.ColorNoise * 2f;

                for (int f = 0; f < 6; f++)
                {
                    Vector3 normal = Normals[f];
                    int nx = x + (int)normal.x, ny = y + (int)normal.y, nz = z + (int)normal.z;
                    if (nx >= 0 && ny >= 0 && nz >= 0 && nx < n && ny < n && nz < n)
                        continue;

                    BlockColor c = f == 0 ? definition.TopColor : f == 1 ? definition.BottomColor : definition.SideColor;
                    Color32 color = Tint(c, definition, plains, f, shade);

                    AddFace(mesh, f, new Vector3((x - n * 0.5f) * v, (y - n * 0.5f) * v, (z - n * 0.5f) * v), v, color);
                }
            }

            return mesh;
        }

        /// <summary>A flat coloured tile for items without a model.</summary>
        /// <summary>
        /// An ItemShapes pattern as a flat stack of voxels (like the icon,
        /// but 3D). The bottom-left end of the handle sits at the pivot,
        /// which is the grip point of the hand.
        /// </summary>
        public static ChunkMeshData Shape(string[] shape, ItemData item)
        {
            var mesh = new ChunkMeshData();
            int grid = ItemShapes.GridSize;
            float p = BlockSize * 1.6f / grid;     // tools are a bit longer than a block
            float thickness = p;

            // Grip around pixel (1, 10) - near the handle end.
            float gripX = 1.5f * p, gripY = (grid - 10.5f) * p;

            for (int gy = 0; gy < grid; gy++)
            for (int gx = 0; gx < grid; gx++)
            {
                char c = shape[gy][gx];
                if (c == '.')
                    continue;

                Color32 color = ItemShapes.ColorOf(c, item);
                var origin = new Vector3(gx * p - gripX, (grid - 1 - gy) * p - gripY, -thickness * 0.5f);

                for (int f = 0; f < 6; f++)
                    AddBoxFace(mesh, f, origin, new Vector3(p, p, thickness), color);
            }

            return mesh;
        }

        public static ChunkMeshData Tile(string id)
        {
            uint hash = 2166136261;
            foreach (char ch in id)
            {
                hash ^= ch;
                hash *= 16777619;
            }

            Color rgb = Color.HSVToRGB((hash % 360) / 360f, 0.55f, 0.85f);
            var color = new Color32((byte)(rgb.r * 255), (byte)(rgb.g * 255), (byte)(rgb.b * 255), 255);

            var mesh = new ChunkMeshData();
            float s = BlockSize;
            float t = BlockSize * 0.15f;

            for (int f = 0; f < 6; f++)
                AddBoxFace(mesh, f, new Vector3(-s * 0.5f, -s * 0.5f, -t * 0.5f), new Vector3(s, s, t), color);

            return mesh;
        }

        private static Color32 Tint(BlockColor c, BlockDefinition definition, BiomeDefinition biome, int face, float shade)
        {
            float r = c.R, g = c.G, b = c.B;

            BlockColor? tint =
                definition.Tint == BiomeTint.Grass && face != 1 ? biome.GrassTint :
                definition.Tint == BiomeTint.Foliage ? biome.FoliageTint :
                (BlockColor?)null;

            if (tint.HasValue)
            {
                r = r * tint.Value.R / 255f;
                g = g * tint.Value.G / 255f;
                b = b * tint.Value.B / 255f;
            }

            return new Color32(
                (byte)Mathf.Clamp(r * shade, 0f, 255f),
                (byte)Mathf.Clamp(g * shade, 0f, 255f),
                (byte)Mathf.Clamp(b * shade, 0f, 255f),
                255);
        }

        private static readonly Vector3[] Normals =
        {
            new Vector3(0, 1, 0), new Vector3(0, -1, 0), new Vector3(-1, 0, 0),
            new Vector3(1, 0, 0), new Vector3(0, 0, 1), new Vector3(0, 0, -1)
        };

        private static readonly int[][] Corners =
        {
            new[] { 0,1,0, 0,1,1, 1,1,1, 1,1,0 },
            new[] { 0,0,1, 0,0,0, 1,0,0, 1,0,1 },
            new[] { 0,0,0, 0,0,1, 0,1,1, 0,1,0 },
            new[] { 1,0,1, 1,0,0, 1,1,0, 1,1,1 },
            new[] { 0,0,1, 1,0,1, 1,1,1, 0,1,1 },
            new[] { 1,0,0, 0,0,0, 0,1,0, 1,1,0 }
        };

        private static void AddFace(ChunkMeshData mesh, int face, Vector3 origin, float size, Color32 color)
        {
            AddBoxFace(mesh, face, origin, new Vector3(size, size, size), color);
        }

        private static void AddBoxFace(ChunkMeshData mesh, int face, Vector3 origin, Vector3 size, Color32 color)
        {
            int start = mesh.Vertices.Count;
            int[] c = Corners[face];

            for (int i = 0; i < 4; i++)
            {
                mesh.Vertices.Add(new Vector3(
                    origin.x + c[i * 3] * size.x,
                    origin.y + c[i * 3 + 1] * size.y,
                    origin.z + c[i * 3 + 2] * size.z));
                mesh.Normals.Add(Normals[face]);
                mesh.Colors.Add(color);
            }

            mesh.Triangles.Add(start); mesh.Triangles.Add(start + 1); mesh.Triangles.Add(start + 2);
            mesh.Triangles.Add(start); mesh.Triangles.Add(start + 2); mesh.Triangles.Add(start + 3);
        }
    }
}
