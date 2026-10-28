using Unity.Mathematics;

namespace Voxelforge.Voxels
{
    /// <summary>Finds where a player can stand in a block column.</summary>
    public static class SpawnPointFinder
    {
        /// <summary>
        /// Scans down the column at (<paramref name="x"/>, <paramref name="z"/>) from <paramref name="topY"/> and
        /// returns the top of the first solid block that has <paramref name="clearance"/> blocks of space above it.
        /// Scanning from the sky down means it finds the surface, never a cave below it.
        /// Returns false while any chunk it needs to look at isn't generated yet; try again next tick.
        /// </summary>
        public static bool TryFindSurface(VoxelWorld world, int x, int z, int topY, int clearance, out float3 feetPosition)
        {
            feetPosition = default;
            int air = 0;
            for (int y = topY; y >= 0; y--)
            {
                var block = new int3(x, y, z);
                if (!world.TryGetChunk(VoxelMath.WorldToChunk(block), out _))
                    return false;

                if (!VoxelBodyMotor.IsSolid(block, world))
                {
                    air++;
                    continue;
                }

                if (air >= clearance)
                {
                    feetPosition = new float3(x + 0.5f, y + 1f, z + 0.5f);
                    return true;
                }
                air = 0;
            }
            return false;
        }
    }
}
