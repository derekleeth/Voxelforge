using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Voxelforge.Voxels;

namespace Voxelforge.Tests
{
    /// <summary>Proves chunk data can be read by a Burst job on a worker thread, ready for meshing (1.3).</summary>
    public class ChunkJobTests
    {
        [BurstCompile]
        struct CountSolidBlocksJob : IJob
        {
            [ReadOnly] public NativeArray<Voxel>.ReadOnly Voxels;
            [ReadOnly] public NativeArray<BlockInfo> Blocks;
            public NativeReference<int> SolidCount;

            public void Execute()
            {
                int count = 0;
                for (int i = 0; i < Voxels.Length; i++)
                {
                    if (Blocks[Voxels[i].BlockId].IsSolid)
                        count++;
                }
                SolidCount.Value = count;
            }
        }

        [Test]
        public void BurstJob_CountsSolidBlocksInChunk()
        {
            var blocks = new NativeArray<BlockInfo>(4, Allocator.TempJob);
            blocks[BlockIds.Air] = new BlockInfo { Id = BlockIds.Air, Flags = BlockFlags.Registered | BlockFlags.Transparent };
            blocks[BlockIds.Stone] = new BlockInfo { Id = BlockIds.Stone, Flags = BlockFlags.Registered | BlockFlags.Solid };
            var solidCount = new NativeReference<int>(Allocator.TempJob);

            using var chunk = new VoxelChunk(new ChunkCoord(0, 0, 0));
            for (int x = 0; x < VoxelConstants.ChunkSize; x++)
            for (int z = 0; z < VoxelConstants.ChunkSize; z++)
                chunk.SetVoxel(new int3(x, 0, z), new Voxel(BlockIds.Stone)); // Fill the bottom layer.

            try
            {
                new CountSolidBlocksJob
                {
                    Voxels = chunk.GetReadOnlyVoxels(),
                    Blocks = blocks,
                    SolidCount = solidCount,
                }.Schedule().Complete();

                Assert.AreEqual(VoxelConstants.ChunkArea, solidCount.Value);
            }
            finally
            {
                blocks.Dispose();
                solidCount.Dispose();
            }
        }
    }
}
