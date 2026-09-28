using System.Collections.Generic;
using UnityEngine;
using World.Blocks;
using World.Chunks;

namespace World.Rendering
{
    public static class ChunkMeshBuilder
    {
        public static Mesh BuildMesh(
            ChunkData chunk,
            WorldManager world)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var normals = new List<Vector3>();

            for (int x = 0; x < chunk.SizeX; x++)
            {
                for (int y = 0; y < chunk.SizeY; y++)
                {
                    for (int z = 0; z < chunk.SizeZ; z++)
                    {
                        if (chunk.GetBlock(x, y, z) == BlockId.Air)
                            continue;

                        AddVisibleFaces(
                            chunk,
                            world,
                            x,
                            y,
                            z,
                            vertices,
                            triangles,
                            normals);
                    }
                }
            }

            var mesh = new Mesh
            {
                name = $"Chunk_{chunk.Coord.X}_{chunk.Coord.Z}"
            };

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetNormals(normals);
            mesh.RecalculateBounds();

            return mesh;
        }

        private static void AddVisibleFaces(
            ChunkData chunk,
            WorldManager world,
            int x,
            int y,
            int z,
            List<Vector3> vertices,
            List<int> triangles,
            List<Vector3> normals)
        {
            Vector3 position =
                new Vector3(x, y, z);

            if (IsAir(chunk, world, x, y + 1, z))
                AddFace(position, Face.Up, vertices, triangles, normals);

            if (IsAir(chunk, world, x, y - 1, z))
                AddFace(position, Face.Down, vertices, triangles, normals);

            if (IsAir(chunk, world, x - 1, y, z))
                AddFace(position, Face.Left, vertices, triangles, normals);

            if (IsAir(chunk, world, x + 1, y, z))
                AddFace(position, Face.Right, vertices, triangles, normals);

            if (IsAir(chunk, world, x, y, z + 1))
                AddFace(position, Face.Forward, vertices, triangles, normals);

            if (IsAir(chunk, world, x, y, z - 1))
                AddFace(position, Face.Back, vertices, triangles, normals);
        }

        private static bool IsAir(
            ChunkData chunk,
            WorldManager world,
            int localX,
            int y,
            int localZ)
        {
            if (y < 0 ||
                y >= world.Settings.WorldHeight)
            {
                return true;
            }

            if (chunk.IsInside(
                localX,
                y,
                localZ))
            {
                return
                    chunk.GetBlock(
                        localX,
                        y,
                        localZ) ==
                    BlockId.Air;
            }

            int worldX =
                chunk.Coord.X *
                chunk.SizeX +
                localX;

            int worldZ =
                chunk.Coord.Z *
                chunk.SizeZ +
                localZ;

            return
                world.GetBlock(
                    worldX,
                    y,
                    worldZ) ==
                BlockId.Air;
        }

        private static void AddFace(
            Vector3 blockPosition,
            Face face,
            List<Vector3> vertices,
            List<int> triangles,
            List<Vector3> normals)
        {
            int startIndex =
                vertices.Count;

            GetFaceData(
                face,
                out Vector3 v0,
                out Vector3 v1,
                out Vector3 v2,
                out Vector3 v3,
                out Vector3 normal);

            vertices.Add(blockPosition + v0);
            vertices.Add(blockPosition + v1);
            vertices.Add(blockPosition + v2);
            vertices.Add(blockPosition + v3);

            triangles.Add(startIndex + 0);
            triangles.Add(startIndex + 1);
            triangles.Add(startIndex + 2);

            triangles.Add(startIndex + 0);
            triangles.Add(startIndex + 2);
            triangles.Add(startIndex + 3);

            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);
        }

        private static void GetFaceData(
            Face face,
            out Vector3 v0,
            out Vector3 v1,
            out Vector3 v2,
            out Vector3 v3,
            out Vector3 normal)
        {
            const float min = 0f;
            const float max = 1f;

            switch (face)
            {
                case Face.Up:
                    v0 = new Vector3(min, max, min);
                    v1 = new Vector3(min, max, max);
                    v2 = new Vector3(max, max, max);
                    v3 = new Vector3(max, max, min);
                    normal = Vector3.up;
                    break;

                case Face.Down:
                    v0 = new Vector3(min, min, max);
                    v1 = new Vector3(min, min, min);
                    v2 = new Vector3(max, min, min);
                    v3 = new Vector3(max, min, max);
                    normal = Vector3.down;
                    break;

                case Face.Left:
                    v0 = new Vector3(min, min, min);
                    v1 = new Vector3(min, min, max);
                    v2 = new Vector3(min, max, max);
                    v3 = new Vector3(min, max, min);
                    normal = Vector3.left;
                    break;

                case Face.Right:
                    v0 = new Vector3(max, min, max);
                    v1 = new Vector3(max, min, min);
                    v2 = new Vector3(max, max, min);
                    v3 = new Vector3(max, max, max);
                    normal = Vector3.right;
                    break;

                case Face.Forward:
                    v0 = new Vector3(min, min, max);
                    v1 = new Vector3(max, min, max);
                    v2 = new Vector3(max, max, max);
                    v3 = new Vector3(min, max, max);
                    normal = Vector3.forward;
                    break;

                default:
                    v0 = new Vector3(max, min, min);
                    v1 = new Vector3(min, min, min);
                    v2 = new Vector3(min, max, min);
                    v3 = new Vector3(max, max, min);
                    normal = Vector3.back;
                    break;
            }
        }

        private enum Face
        {
            Up,
            Down,
            Left,
            Right,
            Forward,
            Back
        }
    }
}