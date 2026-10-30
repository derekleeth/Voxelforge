using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using Voxelforge.Gameplay;
using Voxelforge.Voxels;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 3.2 and 4.5: block drops and loot bags fall, land on top of blocks and slide to a stop.</summary>
    public class DropPhysicsTests
    {
        const float Dt = 1f / 50f;
        const float Half = 0.18f;

        VoxelWorld _world;

        [SetUp]
        public void SetUp()
        {
            var blocks = new NativeArray<BlockInfo>(BlockIds.Stone + 1, Allocator.Persistent);
            blocks[BlockIds.Air] = new BlockInfo { Id = BlockIds.Air, Flags = BlockFlags.Registered | BlockFlags.Transparent };
            blocks[BlockIds.Stone] = new BlockInfo { Id = BlockIds.Stone, MaxDurability = 60, Flags = BlockFlags.Registered | BlockFlags.Solid };
            _world = new VoxelWorld(blocks);
            // Stone fills y 0-31 of chunk (0,0,0); the chunk above is air. The ground's top is y = 32.
            _world.CreateChunk(new ChunkCoord(0, 0, 0), new Voxel(BlockIds.Stone));
            _world.CreateChunk(new ChunkCoord(0, 1, 0));
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        [Test]
        public void ThrownSack_LandsOnTheGround_AndComesToRest()
        {
            var position = new float3(8.5f, 34f, 8.5f);
            var velocity = new float3(2.5f, 3f, 0f); // A drop: forward and up.
            bool resting = false;
            for (int step = 0; step < 300 && !resting; step++)
                resting = DropPhysics.Step(_world, ref position, ref velocity, Half, 25f, Dt);

            Assert.IsTrue(resting, "Settles within 6 seconds.");
            Assert.AreEqual(32f + Half, position.y, 1e-4f, "Sits exactly on top of the stone.");
            Assert.Greater(position.x, 8.5f, "It was thrown forward.");
            Assert.Less(position.x, 12f, "It slid to a stop instead of skating away.");
        }

        [Test]
        public void SackInsideABlock_PopsUpOnTop()
        {
            var position = new float3(4.5f, 31.5f, 4.5f); // Buried in the top stone layer, as if a block was placed on it.
            float3 velocity = float3.zero;
            DropPhysics.Step(_world, ref position, ref velocity, Half, 25f, Dt);
            Assert.GreaterOrEqual(position.y, 32f, "Pushed out of the block.");
        }
    }
}
