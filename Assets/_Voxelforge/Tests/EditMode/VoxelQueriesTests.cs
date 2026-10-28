using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using Voxelforge.Voxels;

namespace Voxelforge.Tests
{
    /// <summary>Ground lookups for foot IK and grid raycasts for camera collision (Roadmap 2.5).</summary>
    public class VoxelQueriesTests
    {
        VoxelWorld _world;

        [SetUp]
        public void SetUp()
        {
            var blocks = new NativeArray<BlockInfo>(4, Allocator.Persistent);
            blocks[BlockIds.Air] = new BlockInfo { Id = BlockIds.Air, Flags = BlockFlags.Registered | BlockFlags.Transparent };
            blocks[BlockIds.Stone] = new BlockInfo { Id = BlockIds.Stone, Flags = BlockFlags.Registered | BlockFlags.Solid };
            _world = new VoxelWorld(blocks);
            // An air room from -32 to 31 on x and z, with a stone floor along y = 0 and a 1-block step at x >= 4.
            for (int cx = -1; cx <= 0; cx++)
            for (int cz = -1; cz <= 0; cz++)
                _world.CreateChunk(new ChunkCoord(cx, 0, cz));
            for (int x = -32; x < 32; x++)
            for (int z = -32; z < 32; z++)
            {
                _world.SetVoxel(new int3(x, 0, z), new Voxel(BlockIds.Stone));
                if (x >= 4)
                    _world.SetVoxel(new int3(x, 1, z), new Voxel(BlockIds.Stone));
            }
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        [Test]
        public void GroundHeight_OnFlatFloor()
        {
            Assert.IsTrue(VoxelQueries.TryGetGroundHeight(_world, new float3(0.5f, 1.2f, 0.5f), 0.5f, 0.6f, out float height));
            Assert.AreEqual(1f, height);
        }

        [Test]
        public void GroundHeight_FindsTheStepAboveAndTheFloorBelow()
        {
            // A foot just in front of the step, a little below its top, finds the step.
            Assert.IsTrue(VoxelQueries.TryGetGroundHeight(_world, new float3(4.2f, 1.7f, 0.5f), 0.5f, 0.6f, out float onStep));
            Assert.AreEqual(2f, onStep);
            // A foot hanging over the lower side of the step finds the floor.
            Assert.IsTrue(VoxelQueries.TryGetGroundHeight(_world, new float3(3.8f, 1.7f, 0.5f), 0.5f, 0.8f, out float below));
            Assert.AreEqual(1f, below);
        }

        [Test]
        public void GroundHeight_FailsWhenNoGroundInRange()
        {
            Assert.IsFalse(VoxelQueries.TryGetGroundHeight(_world, new float3(0.5f, 5f, 0.5f), 0.5f, 0.6f, out _));
        }

        [Test]
        public void GroundHeight_CountsUngeneratedChunksAsSolid()
        {
            // Nothing is generated below y = 0 in this world, so the "ground" there is the top of the missing chunk.
            Assert.IsTrue(VoxelQueries.TryGetGroundHeight(_world, new float3(40.5f, 0.2f, 0.5f), 0.5f, 0.6f, out float height));
            Assert.AreEqual(0f, height);
        }

        [Test]
        public void Raycast_HitsFloorFromAbove()
        {
            Assert.IsTrue(VoxelQueries.Raycast(_world, new float3(0.5f, 10f, 0.5f), new float3(0f, -1f, 0f), 20f, out VoxelHit hit));
            Assert.AreEqual(new int3(0, 0, 0), hit.Block);
            Assert.AreEqual(new int3(0, 1, 0), hit.Normal);
            Assert.AreEqual(9f, hit.Distance, 1e-4f);
        }

        [Test]
        public void Raycast_HitsStepSideOnEachHorizontalDirection()
        {
            Assert.IsTrue(VoxelQueries.Raycast(_world, new float3(0.5f, 1.5f, 0.5f), new float3(1f, 0f, 0f), 20f, out VoxelHit east));
            Assert.AreEqual(new int3(4, 1, 0), east.Block);
            Assert.AreEqual(new int3(-1, 0, 0), east.Normal);
            Assert.AreEqual(3.5f, east.Distance, 1e-4f);

            // Negative coordinates: walk west from the step's top into the room's wall of unloaded chunks at x = -33.
            Assert.IsTrue(VoxelQueries.Raycast(_world, new float3(-0.5f, 1.5f, -0.5f), new float3(-1f, 0f, 0f), 64f, out VoxelHit west));
            Assert.AreEqual(new int3(-33, 1, -1), west.Block);
            Assert.AreEqual(new int3(1, 0, 0), west.Normal);
        }

        [Test]
        public void Raycast_Diagonal_HitsFloor()
        {
            Assert.IsTrue(VoxelQueries.Raycast(_world, new float3(-3.5f, 4f, -3.5f), new float3(-1f, -1f, -1f), 20f, out VoxelHit hit));
            Assert.AreEqual(0, hit.Block.y);
            Assert.AreEqual(new int3(0, 1, 0), hit.Normal);
            Assert.AreEqual(new int3(-7, 0, -7), hit.Block);
        }

        [Test]
        public void Raycast_MissesBeyondMaxDistance()
        {
            Assert.IsFalse(VoxelQueries.Raycast(_world, new float3(0.5f, 10f, 0.5f), new float3(0f, -1f, 0f), 8f, out _));
        }

        [Test]
        public void Raycast_StartingInsideBlock_HitsAtZero()
        {
            Assert.IsTrue(VoxelQueries.Raycast(_world, new float3(0.5f, 0.5f, 0.5f), new float3(0f, 1f, 0f), 5f, out VoxelHit hit));
            Assert.AreEqual(0f, hit.Distance);
            Assert.AreEqual(int3.zero, hit.Normal);
        }
    }
}
