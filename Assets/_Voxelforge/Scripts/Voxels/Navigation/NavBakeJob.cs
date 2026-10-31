using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using static Voxelforge.Voxels.VoxelConstants;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// Turns a copy of one chunk's blocks into its nav bits (Roadmap 5.1): 1,024 rows of 32 bits, 1 = solid, as
    /// <see cref="NavGridView"/> reads them. Runs on a worker thread when a chunk arrives (CLAUDE.md Law #4); single block
    /// edits after that just flip a bit on the main thread.
    /// </summary>
    [BurstCompile]
    public struct NavBakeJob : IJob
    {
        /// <summary>The chunk's <see cref="ChunkVolume"/> blocks, copied, so the chunk can be edited while this runs.</summary>
        [ReadOnly] public NativeArray<Voxel> Voxels;

        [ReadOnly] public NativeArray<BlockInfo> Blocks;

        /// <summary>Output: <see cref="NavGridView.RowsPerChunk"/> rows.</summary>
        [WriteOnly] public NativeArray<uint> Rows;

        public void Execute()
        {
            // The chunk's array runs x fastest, then z, then y, so every 32 blocks in a row make one (y, z) row of bits.
            int index = 0;
            for (int row = 0; row < NavGridView.RowsPerChunk; row++)
            {
                uint bits = 0;
                for (int x = 0; x < ChunkSize; x++, index++)
                {
                    ushort id = Voxels[index].BlockId;
                    if (id < Blocks.Length && Blocks[id].IsSolid)
                        bits |= 1u << x;
                }
                Rows[row] = bits;
            }
        }
    }
}
