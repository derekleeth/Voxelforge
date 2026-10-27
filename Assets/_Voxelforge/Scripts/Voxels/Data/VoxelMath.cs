using Unity.Mathematics;
using static Voxelforge.Voxels.VoxelConstants;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// Converts between the three coordinate spaces:
    /// <list type="bullet">
    /// <item><b>World</b>: any block position, can be negative.</item>
    /// <item><b>Chunk</b>: which chunk a block is in (<see cref="ChunkCoord"/>).</item>
    /// <item><b>Local</b>: 0-31 position inside a chunk, plus its flat array index.</item>
    /// </list>
    /// Uses shifts and masks rather than division so negative coordinates round down correctly
    /// (world -1 is chunk -1, local 31). All methods are Burst-safe.
    /// </summary>
    public static class VoxelMath
    {
        public static ChunkCoord WorldToChunk(int3 world) => new ChunkCoord(world >> ChunkShift);

        public static int3 WorldToLocal(int3 world) => world & ChunkMask;

        public static int3 LocalToWorld(ChunkCoord chunk, int3 local) => chunk.WorldOrigin + local;

        /// <summary>The block a world-space point lies inside.</summary>
        public static int3 WorldPositionToBlock(float3 position) => (int3)math.floor(position);

        /// <summary>
        /// Flat array index of a local position. X varies fastest, then Z, then Y, so each
        /// horizontal row is contiguous in memory.
        /// </summary>
        public static int ToIndex(int3 local) => local.x | (local.z << ChunkShift) | (local.y << (ChunkShift * 2));

        public static int ToIndex(int x, int y, int z) => x | (z << ChunkShift) | (y << (ChunkShift * 2));

        public static int3 FromIndex(int index)
        {
            return new int3(
                index & ChunkMask,
                (index >> (ChunkShift * 2)) & ChunkMask,
                (index >> ChunkShift) & ChunkMask);
        }

        public static bool IsInsideChunk(int3 local) => math.all((local >= 0) & (local < ChunkSize));
    }
}
