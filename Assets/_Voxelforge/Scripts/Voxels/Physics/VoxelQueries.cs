using Unity.Mathematics;

namespace Voxelforge.Voxels
{
    /// <summary>Where a <see cref="VoxelQueries.Raycast"/> hit the block grid.</summary>
    public struct VoxelHit
    {
        /// <summary>The block the ray hit: solid, or a prop such as a torch (<see cref="BlockInfo.IsTargetable"/>).</summary>
        public int3 Block;
        /// <summary>Outward normal of the face the ray entered through. Zero if the ray started inside the block.</summary>
        public int3 Normal;
        /// <summary>Distance along the ray to the hit point.</summary>
        public float Distance;
        public float3 Point;
    }

    /// <summary>
    /// Questions about the block grid that don't change it: where is the ground, what does a ray hit. Plain C#
    /// that reads <see cref="VoxelWorld"/> directly, so it needs no colliders. Blocks count as solid exactly
    /// like they do for <see cref="VoxelBodyMotor.IsSolid"/>, including chunks that aren't generated yet.
    /// </summary>
    public static class VoxelQueries
    {
        /// <summary>
        /// Finds the top of the highest solid block under <paramref name="from"/>, looking from
        /// <paramref name="maxRise"/> above it down to <paramref name="maxDrop"/> below it. Block tops are flat,
        /// so the answer is a whole number.
        /// </summary>
        public static bool TryGetGroundHeight(VoxelWorld world, float3 from, float maxRise, float maxDrop, out float height)
        {
            int x = (int)math.floor(from.x);
            int z = (int)math.floor(from.z);
            int highest = (int)math.floor(from.y + maxRise) - 1;
            int lowest = (int)math.ceil(from.y - maxDrop) - 1;
            for (int y = highest; y >= lowest; y--)
            {
                if (VoxelBodyMotor.IsSolid(new int3(x, y, z), world))
                {
                    height = y + 1f;
                    return true;
                }
            }
            height = 0f;
            return false;
        }

        /// <summary>
        /// Walks a ray through the grid one block at a time (Amanatides &amp; Woo) and stops at the first solid
        /// block or prop (a torch, Roadmap 3.5) within <paramref name="maxDistance"/>. A prop is hit as its whole cell.
        /// <paramref name="direction"/> doesn't need to be normalized.
        /// With <paramref name="opaqueOnly"/>, it stops only at blocks you can see, passing through invisible solid
        /// ones such as tree trunks (cameras use this).
        /// </summary>
        public static bool Raycast(VoxelWorld world, float3 origin, float3 direction, float maxDistance, out VoxelHit hit,
            bool opaqueOnly = false)
        {
            hit = default;
            float length = math.length(direction);
            if (length < 1e-6f)
                return false;
            float3 dir = direction / length;

            int3 block = (int3)math.floor(origin);
            int3 step = (int3)math.sign(dir);
            // Distance along the ray to cross one whole block on each axis, and to reach the next block boundary.
            float3 delta = math.abs(1f / dir);
            float3 nextBoundary = (float3)block + math.max((float3)step, 0f);
            float3 toBoundary = math.select((nextBoundary - origin) / dir, float.PositiveInfinity, step == 0);

            int3 normal = int3.zero;
            float distance = 0f;
            while (distance <= maxDistance)
            {
                bool stops = opaqueOnly
                    ? GreedyMesher.IsOpaque(world.Blocks, world.GetVoxel(block).BlockId)
                    : IsTargetable(block, world);
                if (stops)
                {
                    hit = new VoxelHit { Block = block, Normal = normal, Distance = distance, Point = origin + dir * distance };
                    return true;
                }

                // Step into the neighbour whose boundary is closest.
                int axis = toBoundary.x < toBoundary.y
                    ? (toBoundary.x < toBoundary.z ? 0 : 2)
                    : (toBoundary.y < toBoundary.z ? 1 : 2);
                distance = toBoundary[axis];
                toBoundary[axis] += delta[axis];
                block[axis] += step[axis];
                normal = int3.zero;
                normal[axis] = -step[axis];
            }
            return false;
        }

        /// <summary>Aim rays stop here: anything <see cref="VoxelBodyMotor.IsSolid"/> stops, plus props such as torches.</summary>
        public static bool IsTargetable(int3 block, VoxelWorld world)
        {
            if (VoxelBodyMotor.IsSolid(block, world))
                return true;
            ushort id = world.GetVoxel(block).BlockId;
            return id < world.Blocks.Length && world.Blocks[id].IsProp;
        }
    }
}
