using System;
using UnityEngine;
using World.Blocks;

namespace Gameplay
{
    public struct VoxelHit
    {
        /// <summary>The voxel that was hit.</summary>
        public Vector3Int Voxel;

        /// <summary>Face normal; Voxel + Normal is where a block would be placed.</summary>
        public Vector3Int Normal;

        public BlockId Block;

        /// <summary>Distance from the origin in metres.</summary>
        public float Distance;
    }

    /// <summary>
    /// Steps through the voxel grid along a ray (Amanatides and Woo) and
    /// returns the first targetable voxel. Uses block data, not physics,
    /// so it also hits grass and flowers and works without colliders.
    /// </summary>
    public static class VoxelRaycast
    {
        /// <param name="origin">Ray start in metres.</param>
        /// <param name="direction">Ray direction (any length).</param>
        /// <param name="maxDistance">Maximum distance in metres.</param>
        /// <param name="voxelSize">Metres per voxel.</param>
        /// <param name="getBlock">Block lookup in voxel coordinates.</param>
        public static bool Cast(
            Vector3 origin,
            Vector3 direction,
            float maxDistance,
            float voxelSize,
            Func<int, int, int, BlockId> getBlock,
            out VoxelHit hit)
        {
            hit = default;

            float length = direction.magnitude;
            if (length < 1e-6f)
                return false;

            Vector3 d = direction / length;
            Vector3 p = origin / voxelSize;
            float max = maxDistance / voxelSize;

            int x = Mathf.FloorToInt(p.x);
            int y = Mathf.FloorToInt(p.y);
            int z = Mathf.FloorToInt(p.z);

            int stepX = d.x > 0 ? 1 : -1;
            int stepY = d.y > 0 ? 1 : -1;
            int stepZ = d.z > 0 ? 1 : -1;

            float tDeltaX = Math.Abs(d.x) < 1e-6f ? float.MaxValue : Math.Abs(1f / d.x);
            float tDeltaY = Math.Abs(d.y) < 1e-6f ? float.MaxValue : Math.Abs(1f / d.y);
            float tDeltaZ = Math.Abs(d.z) < 1e-6f ? float.MaxValue : Math.Abs(1f / d.z);

            float tMaxX = Boundary(p.x, d.x, x) ;
            float tMaxY = Boundary(p.y, d.y, y);
            float tMaxZ = Boundary(p.z, d.z, z);

            var normal = new Vector3Int(0, 0, 0);
            float t = 0f;

            for (int i = 0; i < 1024 && t <= max; i++)
            {
                BlockId block = getBlock(x, y, z);

                if (BlockEditRules.IsTargetable(block))
                {
                    hit = new VoxelHit
                    {
                        Voxel = new Vector3Int(x, y, z),
                        Normal = normal,
                        Block = block,
                        Distance = t * voxelSize
                    };

                    return true;
                }

                if (tMaxX < tMaxY && tMaxX < tMaxZ)
                {
                    x += stepX;
                    t = tMaxX;
                    tMaxX += tDeltaX;
                    normal = new Vector3Int(-stepX, 0, 0);
                }
                else if (tMaxY < tMaxZ)
                {
                    y += stepY;
                    t = tMaxY;
                    tMaxY += tDeltaY;
                    normal = new Vector3Int(0, -stepY, 0);
                }
                else
                {
                    z += stepZ;
                    t = tMaxZ;
                    tMaxZ += tDeltaZ;
                    normal = new Vector3Int(0, 0, -stepZ);
                }
            }

            return false;
        }

        private static float Boundary(float position, float direction, int cell)
        {
            if (Math.Abs(direction) < 1e-6f)
                return float.MaxValue;

            float next = direction > 0 ? cell + 1 : cell;
            return (next - position) / direction;
        }
    }
}
