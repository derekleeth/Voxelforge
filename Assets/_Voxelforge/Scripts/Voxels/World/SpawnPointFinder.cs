using Unity.Mathematics;

namespace Voxelforge.Voxels
{
    /// <summary>Finds where a player can stand in a block column.</summary>
    public static class SpawnPointFinder
    {
        enum ScanResult { Found, NotReady, NoGround, Blocked }

        /// <summary>How many columns east to try when the asked-for one holds a tree trunk.</summary>
        const int MaxShifts = 3;

        /// <summary>
        /// Scans down the column at (<paramref name="x"/>, <paramref name="z"/>) from <paramref name="topY"/> and
        /// returns the top of the first solid block that has <paramref name="clearance"/> blocks of space above it.
        /// Scanning from the sky down means it finds the surface, never a cave below it.
        /// Returns false while any chunk it needs to look at isn't generated yet; try again next tick.
        /// <para>
        /// Only opaque blocks count as ground. If the column holds something solid but see-through, such as an invisible
        /// tree trunk (Roadmap 2.6), the search moves one column east. Trees never touch, so that column is clear.
        /// </para>
        /// </summary>
        public static bool TryFindSurface(VoxelWorld world, int x, int z, int topY, int clearance, out float3 feetPosition)
        {
            for (int shift = 0; shift <= MaxShifts; shift++)
            {
                ScanResult result = Scan(world, x + shift, z, topY, clearance, out feetPosition);
                if (result != ScanResult.Blocked)
                    return result == ScanResult.Found;
            }
            feetPosition = default;
            return false;
        }

        static ScanResult Scan(VoxelWorld world, int x, int z, int topY, int clearance, out float3 feetPosition)
        {
            feetPosition = default;
            int air = 0;
            for (int y = topY; y >= 0; y--)
            {
                var block = new int3(x, y, z);
                if (!world.TryGetChunk(VoxelMath.WorldToChunk(block), out _))
                    return ScanResult.NotReady;

                if (!VoxelBodyMotor.IsSolid(block, world))
                {
                    air++;
                    continue;
                }

                if (!GreedyMesher.IsOpaque(world.Blocks, world.GetVoxel(block).BlockId))
                    return ScanResult.Blocked;

                if (air >= clearance)
                {
                    feetPosition = new float3(x + 0.5f, y + 1f, z + 0.5f);
                    return ScanResult.Found;
                }
                air = 0;
            }
            return ScanResult.NoGround;
        }
    }
}
