using NUnit.Framework;
using Unity.Mathematics;
using Voxelforge.Voxels;

namespace Voxelforge.Tests
{
    public class VoxelMathTests
    {
        [Test]
        public void ToIndex_FromIndex_RoundTripsEveryLocalPosition()
        {
            for (int index = 0; index < VoxelConstants.ChunkVolume; index++)
            {
                int3 local = VoxelMath.FromIndex(index);
                Assert.IsTrue(VoxelMath.IsInsideChunk(local));
                Assert.AreEqual(index, VoxelMath.ToIndex(local));
            }
        }

        [Test]
        public void ToIndex_XVariesFastest_ThenZ_ThenY()
        {
            Assert.AreEqual(1, VoxelMath.ToIndex(1, 0, 0));
            Assert.AreEqual(VoxelConstants.ChunkSize, VoxelMath.ToIndex(0, 0, 1));
            Assert.AreEqual(VoxelConstants.ChunkArea, VoxelMath.ToIndex(0, 1, 0));
            Assert.AreEqual(VoxelConstants.ChunkVolume - 1, VoxelMath.ToIndex(31, 31, 31));
        }

        [TestCase(0, 0, 0)]
        [TestCase(31, 0, 31)]
        [TestCase(32, 1, 0)]
        [TestCase(-1, -1, 31)]
        [TestCase(-32, -1, 0)]
        [TestCase(-33, -2, 31)]
        [TestCase(1000, 31, 8)]
        public void WorldToChunkAndLocal_HandlesNegativeCoordinates(int world, int expectedChunk, int expectedLocal)
        {
            var position = new int3(world, world, world);

            Assert.AreEqual(new ChunkCoord(expectedChunk, expectedChunk, expectedChunk), VoxelMath.WorldToChunk(position));
            Assert.AreEqual(new int3(expectedLocal), VoxelMath.WorldToLocal(position));
        }

        [Test]
        public void LocalToWorld_InvertsWorldToChunkAndLocal()
        {
            var world = new int3(-45, 70, 3);

            ChunkCoord chunk = VoxelMath.WorldToChunk(world);
            int3 local = VoxelMath.WorldToLocal(world);

            Assert.AreEqual(world, VoxelMath.LocalToWorld(chunk, local));
        }

        [Test]
        public void WorldPositionToBlock_FloorsNegativePositions()
        {
            Assert.AreEqual(new int3(-1, 0, 2), VoxelMath.WorldPositionToBlock(new float3(-0.25f, 0.9f, 2.0f)));
        }

        [Test]
        public void IsInsideChunk_RejectsOutOfRange()
        {
            Assert.IsFalse(VoxelMath.IsInsideChunk(new int3(32, 0, 0)));
            Assert.IsFalse(VoxelMath.IsInsideChunk(new int3(0, -1, 0)));
            Assert.IsTrue(VoxelMath.IsInsideChunk(new int3(31, 31, 31)));
        }

        [Test]
        public void ChunkCoord_WorldOrigin_IsChunkTimesSize()
        {
            Assert.AreEqual(new int3(64, -32, 0), new ChunkCoord(2, -1, 0).WorldOrigin);
        }
    }
}
