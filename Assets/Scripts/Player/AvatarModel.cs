using UnityEngine;
using World.Rendering;

namespace Networking
{
    /// <summary>
    /// Builds the meshes of the voxel character from small coloured voxel
    /// grids (boots, trousers, tunic, belt, face, hair). Pure mesh data, so
    /// it can be tested and previewed outside Unity. Every player gets a
    /// different colour set from their id.
    /// </summary>
    public static class AvatarModel
    {
        /// <summary>Size of one character voxel in metres (a 2 m player).</summary>
        public const float Voxel = 0.075f;

        public const int ThighLength = 6;  // 0.45 m
        public const int ShinLength = 5;   // 0.375 m (incl. boot)
        public const int LegHeight = ThighLength + ShinLength; // 0.825 m
        public const int TorsoHeight = 8;  // 0.6 m
        public const int HeadSize = 6;     // 0.45 m

        public static float HipHeight => LegHeight * Voxel;
        public static float ShoulderHeight => HipHeight + (TorsoHeight - 0.5f) * Voxel;
        public static float NeckHeight => HipHeight + TorsoHeight * Voxel;

        public struct Palette
        {
            public Color32 Skin, Hair, Shirt, ShirtDark, Trousers, Boots, Belt, Buckle, Eyes;
        }

        private static readonly uint[] Skins = { 0xF1C7A3, 0xE0AC85, 0xC68A5E, 0x9C6644, 0x6E4630 };
        private static readonly uint[] Hairs = { 0x3B2A1E, 0x6B4423, 0xC8963C, 0x1E1B1A, 0xA8452A, 0xD9D2C2 };
        private static readonly uint[] Shirts = { 0x3E7CB1, 0xB0413E, 0x4E8A3C, 0xD08A2E, 0x7B4FA0, 0x2E8C8A };
        private static readonly uint[] Trousers = { 0x4A3B2C, 0x2F3A4F, 0x5C5A4E, 0x3D4A33 };

        public static Palette PaletteFor(uint playerId)
        {
            uint h = playerId * 2654435761u + 0x9E37u;

            Color32 shirt = C(Shirts[h % (uint)Shirts.Length]);

            return new Palette
            {
                Skin = C(Skins[(h >> 4) % (uint)Skins.Length]),
                Hair = C(Hairs[(h >> 8) % (uint)Hairs.Length]),
                Shirt = shirt,
                ShirtDark = Scale(shirt, 0.75f),
                Trousers = C(Trousers[(h >> 12) % (uint)Trousers.Length]),
                Boots = C(0x3A2A1E),
                Belt = C(0x5A3A22),
                Buckle = C(0xE0B84A),
                Eyes = C(0x1E1E28)
            };
        }

        // Each part is a small voxel grid; pivots are where the part
        // rotates (hip, shoulder, neck).

        /// <summary>Upper leg; pivot at the hip.</summary>
        public static ChunkMeshData Thigh(Palette p)
        {
            var g = new Grid(3, ThighLength, 3);
            for (int y = 0; y < ThighLength; y++)
            for (int x = 0; x < 3; x++)
            for (int z = 0; z < 3; z++)
                g.Set(x, y, z, p.Trousers);

            return g.Build(new Vector3(1.5f, ThighLength, 1.5f));
        }

        /// <summary>Lower leg with boot; pivot at the knee.</summary>
        public static ChunkMeshData Shin(Palette p)
        {
            var g = new Grid(3, ShinLength, 4);
            for (int y = 0; y < ShinLength; y++)
            for (int x = 0; x < 3; x++)
            for (int z = 0; z < 3; z++)
                g.Set(x, y, z, y < 3 ? p.Boots : p.Trousers);

            // Boot toe sticks out a little at the front.
            for (int x = 0; x < 3; x++)
                g.Set(x, 0, 3, p.Boots);

            return g.Build(new Vector3(1.5f, ShinLength, 1.5f));
        }

        public static ChunkMeshData Torso(Palette p)
        {
            var g = new Grid(6, TorsoHeight, 3);
            for (int y = 0; y < TorsoHeight; y++)
            for (int x = 0; x < 6; x++)
            for (int z = 0; z < 3; z++)
            {
                Color32 c = p.Shirt;
                if (y == 0) c = (x == 2 || x == 3) && z == 2 ? p.Buckle : p.Belt;
                else if (y == TorsoHeight - 1 && z == 2 && (x == 2 || x == 3)) c = p.Skin; // collar opening
                else if (z == 2 && (x == 1 || x == 4) && y > 1) c = p.ShirtDark;          // tunic seams
                g.Set(x, y, z, c);
            }

            return g.Build(new Vector3(3f, 0f, 1.5f));
        }

        public static ChunkMeshData Arm(Palette p)
        {
            var g = new Grid(2, 8, 2);
            for (int y = 0; y < 8; y++)
            for (int x = 0; x < 2; x++)
            for (int z = 0; z < 2; z++)
                g.Set(x, y, z, y < 2 ? p.Skin : y == 2 ? p.ShirtDark : p.Shirt);

            return g.Build(new Vector3(1f, 7.5f, 1f));
        }

        public static ChunkMeshData Head(Palette p)
        {
            const int s = HeadSize;
            var g = new Grid(s, s + 1, s);

            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            for (int z = 0; z < s; z++)
            {
                bool hair =
                    y >= s - 1 ||                         // top
                    z == 0 && y >= 1 ||                   // back
                    (x == 0 || x == s - 1) && y >= s - 3; // sides
                g.Set(x, y, z, hair ? p.Hair : p.Skin);
            }

            // Fringe and a tuft on top.
            for (int x = 0; x < s; x++) if (x != 2 && x != 3) g.Set(x, s - 2, s - 1, p.Hair);
            g.Set(2, s, 2, p.Hair); g.Set(3, s, 3, p.Hair);

            // Face on the front (+z): two eyes and a mouth.
            g.Set(1, 2, s - 1, p.Eyes);
            g.Set(4, 2, s - 1, p.Eyes);
            g.Set(2, 1, s - 1, Scale(p.Skin, 0.8f));
            g.Set(3, 1, s - 1, Scale(p.Skin, 0.8f));

            return g.Build(new Vector3(s * 0.5f, 0f, s * 0.5f));
        }

        private static Color32 C(uint hex) => new Color32((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex, 255);

        private static Color32 Scale(Color32 c, float f) =>
            new Color32((byte)(c.r * f), (byte)(c.g * f), (byte)(c.b * f), 255);

        private sealed class Grid
        {
            private Color32[,,] _cells;
            private bool[,,] _filled;
            private int _w, _h, _d;

            public Grid(int w, int h, int d)
            {
                _w = w; _h = h; _d = d;
                _cells = new Color32[w, h, d];
                _filled = new bool[w, h, d];
            }

            public void Set(int x, int y, int z, Color32 c)
            {
                if (x < 0 || y < 0 || z < 0 || x >= _w || y >= _h || z >= _d)
                    return;

                _cells[x, y, z] = c;
                _filled[x, y, z] = true;
            }

            public void Resize(int w, int h, int d, int ox, int oy, int oz)
            {
                var cells = new Color32[w, h, d];
                var filled = new bool[w, h, d];

                for (int x = 0; x < _w; x++)
                for (int y = 0; y < _h; y++)
                for (int z = 0; z < _d; z++)
                {
                    if (x + ox < w && y + oy < h && z + oz < d)
                    {
                        cells[x + ox, y + oy, z + oz] = _cells[x, y, z];
                        filled[x + ox, y + oy, z + oz] = _filled[x, y, z];
                    }
                }

                _cells = cells; _filled = filled; _w = w; _h = h; _d = d;
            }

            private bool Filled(int x, int y, int z) =>
                x >= 0 && y >= 0 && z >= 0 && x < _w && y < _h && z < _d && _filled[x, y, z];

            /// <param name="pivot">Pivot in voxel units (becomes the mesh origin).</param>
            public ChunkMeshData Build(Vector3 pivot)
            {
                var mesh = new ChunkMeshData();
                int[] dx = { 0, 0, -1, 1, 0, 0 };
                int[] dy = { 1, -1, 0, 0, 0, 0 };
                int[] dz = { 0, 0, 0, 0, 1, -1 };

                for (int x = 0; x < _w; x++)
                for (int y = 0; y < _h; y++)
                for (int z = 0; z < _d; z++)
                {
                    if (!_filled[x, y, z])
                        continue;

                    // Slight per-voxel brightness variation, like the world.
                    uint hash = (uint)(x * 73856093 ^ y * 19349663 ^ z * 83492791);
                    float shade = 0.93f + (hash % 100) / 100f * 0.12f;
                    Color32 c = _cells[x, y, z];
                    c = new Color32((byte)Mathf.Min(255, c.r * shade), (byte)Mathf.Min(255, c.g * shade), (byte)Mathf.Min(255, c.b * shade), 255);

                    for (int f = 0; f < 6; f++)
                    {
                        if (Filled(x + dx[f], y + dy[f], z + dz[f]))
                            continue;

                        AddFace(mesh, f, x, y, z, pivot, c);
                    }
                }

                return mesh;
            }

            private static readonly int[][] Corners =
            {
                new[] { 0,1,0, 0,1,1, 1,1,1, 1,1,0 },
                new[] { 0,0,1, 0,0,0, 1,0,0, 1,0,1 },
                new[] { 0,0,0, 0,0,1, 0,1,1, 0,1,0 },
                new[] { 1,0,1, 1,0,0, 1,1,0, 1,1,1 },
                new[] { 0,0,1, 1,0,1, 1,1,1, 0,1,1 },
                new[] { 1,0,0, 0,0,0, 0,1,0, 1,1,0 }
            };

            private static readonly Vector3[] Normals =
            {
                new Vector3(0, 1, 0), new Vector3(0, -1, 0), new Vector3(-1, 0, 0),
                new Vector3(1, 0, 0), new Vector3(0, 0, 1), new Vector3(0, 0, -1)
            };

            private static void AddFace(ChunkMeshData mesh, int face, int x, int y, int z, Vector3 pivot, Color32 color)
            {
                int start = mesh.Vertices.Count;
                int[] c = Corners[face];

                for (int i = 0; i < 4; i++)
                {
                    mesh.Vertices.Add(new Vector3(
                        (x + c[i * 3] - pivot.x) * Voxel,
                        (y + c[i * 3 + 1] - pivot.y) * Voxel,
                        (z + c[i * 3 + 2] - pivot.z) * Voxel));
                    mesh.Normals.Add(Normals[face]);
                    mesh.Colors.Add(color);
                }

                mesh.Triangles.Add(start); mesh.Triangles.Add(start + 1); mesh.Triangles.Add(start + 2);
                mesh.Triangles.Add(start); mesh.Triangles.Add(start + 2); mesh.Triangles.Add(start + 3);
            }
        }
    }
}
