using System.Collections.Generic;
using UnityEngine;
using World.Blocks;
using World.Chunks;

namespace World.Rendering
{
    /// <summary>
    /// Collision-only mesh for a chunk: just the boundary between solid
    /// and non-solid blocks, with neighbouring faces merged into big
    /// rectangles ("greedy meshing"). The visual mesh cannot merge faces
    /// because every voxel has its own colour and ambient occlusion; the
    /// collider has neither, so a flat field becomes a handful of
    /// triangles instead of thousands.
    ///
    /// Fewer triangles make the collider much cheaper to cook
    /// (Physics.BakeMesh), smaller in memory and faster for the
    /// character controller to sweep against. A dedicated server builds
    /// only this mesh - no colours, normals or visual mesh at all.
    ///
    /// Pure function of the input; safe on worker threads.
    /// </summary>
    public static class CollisionMeshBuilder
    {
        [System.ThreadStatic] private static int[] _mask;
        [System.ThreadStatic] private static bool[] _solid;
        [System.ThreadStatic] private static int[] _dims, _position, _origin, _du, _dv;

        /// <summary>Builds into data.Vertices / data.Triangles (other lists stay empty).</summary>
        public static void Build(ChunkNeighborhood neighborhood, ChunkMeshData data, float voxelSize)
        {
            ChunkData chunk = neighborhood.Center;
            int top = chunk.HighestBlockY;
            if (top < 0)
                return;

            if (_dims == null)
            {
                _dims = new int[3];
                _position = new int[3];
                _origin = new int[3];
                _du = new int[3];
                _dv = new int[3];
            }

            int[] dims = _dims;
            dims[0] = chunk.SizeX;
            dims[1] = top + 2;
            dims[2] = chunk.SizeZ;
            int maxSlice = System.Math.Max(dims[0] * dims[1], System.Math.Max(dims[1] * dims[2], dims[0] * dims[2]));

            if (_mask == null || _mask.Length < maxSlice)
                _mask = new int[maxSlice];

            // Solid flags for the chunk plus a one-block border, read once
            // (looking blocks up through the neighbourhood is the slow part).
            _sx = chunk.SizeX + 2;
            _sz = chunk.SizeZ + 2;
            _sy = top + 2;
            int solidCount = _sx * _sz * _sy;
            if (_solid == null || _solid.Length < solidCount)
                _solid = new bool[solidCount];

            for (int y = 0; y < _sy; y++)
            for (int z = -1; z <= chunk.SizeZ; z++)
            for (int x = -1; x <= chunk.SizeX; x++)
                _solid[(x + 1) + _sx * ((z + 1) + _sz * y)] = BlockRegistry.IsSolid(neighborhood.GetBlock(x, y, z));

            int[] mask = _mask;
            int[] position = _position;

            for (int d = 0; d < 3; d++)
            {
                int u = (d + 1) % 3;
                int v = (d + 2) % 3;

                // Planes between cell (plane - 1) and cell (plane) along d.
                for (int plane = 0; plane <= dims[d]; plane++)
                {
                    // Bottom of the world: nothing to collide with below.
                    if (d == 1 && plane == 0)
                        continue;

                    int n = 0;

                    for (position[v] = 0; position[v] < dims[v]; position[v]++)
                    for (position[u] = 0; position[u] < dims[u]; position[u]++, n++)
                    {
                        position[d] = plane - 1;
                        bool a = Solid(position[0], position[1], position[2]);
                        position[d] = plane;
                        bool b = Solid(position[0], position[1], position[2]);

                        // +1: face of cell a looking +d; -1: face of b looking -d.
                        // Faces on the chunk border belong to the chunk that
                        // owns the solid block.
                        int face = 0;
                        if (a && !b && plane > 0)
                            face = 1;
                        else if (!a && b && plane < dims[d])
                            face = -1;

                        mask[n] = face;
                    }

                    // Merge equal neighbouring faces into rectangles.
                    n = 0;
                    for (int j = 0; j < dims[v]; j++)
                    for (int i = 0; i < dims[u];)
                    {
                        int face = mask[n];
                        if (face == 0)
                        {
                            i++;
                            n++;
                            continue;
                        }

                        int width = 1;
                        while (i + width < dims[u] && mask[n + width] == face)
                            width++;

                        int height = 1;
                        bool grow = true;
                        while (j + height < dims[v] && grow)
                        {
                            for (int k = 0; k < width; k++)
                            {
                                if (mask[n + k + height * dims[u]] != face)
                                {
                                    grow = false;
                                    break;
                                }
                            }

                            if (grow)
                                height++;
                        }

                        int[] origin = _origin, du = _du, dv = _dv;
                        origin[d] = plane;
                        origin[u] = i;
                        origin[v] = j;
                        du[0] = du[1] = du[2] = 0;
                        dv[0] = dv[1] = dv[2] = 0;
                        du[u] = width;
                        dv[v] = height;

                        AddQuad(data, origin, du, dv, d, face, voxelSize);

                        for (int l = 0; l < height; l++)
                        for (int k = 0; k < width; k++)
                            mask[n + k + l * dims[u]] = 0;

                        i += width;
                        n += width;
                    }
                }
            }
        }

        [System.ThreadStatic] private static int _sx, _sy, _sz;

        // x, z in -1..size, y below 0 counts as solid (bedrock), above as air.
        private static bool Solid(int x, int y, int z)
        {
            if (y < 0)
                return true;
            if (y >= _sy)
                return false;
            return _solid[(x + 1) + _sx * ((z + 1) + _sz * y)];
        }

        private static void AddQuad(ChunkMeshData data, int[] o, int[] du, int[] dv, int axis, int face, float scale)
        {
            int start = data.Vertices.Count;

            data.Vertices.Add(new Vector3(o[0] * scale, o[1] * scale, o[2] * scale));
            data.Vertices.Add(new Vector3((o[0] + du[0]) * scale, (o[1] + du[1]) * scale, (o[2] + du[2]) * scale));
            data.Vertices.Add(new Vector3((o[0] + du[0] + dv[0]) * scale, (o[1] + du[1] + dv[1]) * scale, (o[2] + du[2] + dv[2]) * scale));
            data.Vertices.Add(new Vector3((o[0] + dv[0]) * scale, (o[1] + dv[1]) * scale, (o[2] + dv[2]) * scale));

            // In Unity the front of triangle (a, b, c) faces along
            // cross(b - a, c - a). For (p0, p1, p2) that is cross(du, dv),
            // which points along +axis because u and v follow the axis
            // cyclically (x -> y -> z). Faces looking -axis are reversed.
            if (face > 0)
            {
                data.Triangles.Add(start); data.Triangles.Add(start + 1); data.Triangles.Add(start + 2);
                data.Triangles.Add(start); data.Triangles.Add(start + 2); data.Triangles.Add(start + 3);
            }
            else
            {
                data.Triangles.Add(start); data.Triangles.Add(start + 2); data.Triangles.Add(start + 1);
                data.Triangles.Add(start); data.Triangles.Add(start + 3); data.Triangles.Add(start + 2);
            }
        }
    }
}
