using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using Voxelforge.Voxels;

namespace Voxelforge.Tests
{
    public class SpawnPointFinderTests
    {
        VoxelWorld _world;

        [SetUp]
        public void SetUp()
        {
            var blocks = new NativeArray<BlockInfo>(4, Allocator.Persistent);
            blocks[BlockIds.Air] = new BlockInfo { Id = BlockIds.Air, Flags = BlockFlags.Registered | BlockFlags.Transparent };
            blocks[BlockIds.Stone] = new BlockInfo { Id = BlockIds.Stone, Flags = BlockFlags.Registered | BlockFlags.Solid };
            _world = new VoxelWorld(blocks);
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        [Test]
        public void FindsSurface_NotTheCaveBelowIt()
        {
            _world.CreateChunk(new ChunkCoord(0, 0, 0));
            for (int y = 0; y <= 20; y++)
            {
                bool cave = y >= 5 && y <= 8;
                if (!cave)
                    _world.SetVoxel(new int3(4, y, 4), new Voxel(BlockIds.Stone));
            }

            Assert.IsTrue(SpawnPointFinder.TryFindSurface(_world, 4, 4, 31, 2, out float3 feet));
            Assert.AreEqual(new float3(4.5f, 21f, 4.5f), feet);
        }

        [Test]
        public void NotReady_WhileColumnIsNotGenerated()
        {
            _world.CreateChunk(new ChunkCoord(0, 0, 0), new Voxel(BlockIds.Stone));

            // The search starts in chunk y = 1, which doesn't exist yet.
            Assert.IsFalse(SpawnPointFinder.TryFindSurface(_world, 4, 4, 40, 2, out _));
        }

        [Test]
        public void SkipsLedges_WithoutEnoughHeadRoom()
        {
            _world.CreateChunk(new ChunkCoord(0, 0, 0));
            _world.SetVoxel(new int3(4, 10, 4), new Voxel(BlockIds.Stone)); // Ground, with one air block above...
            _world.SetVoxel(new int3(4, 12, 4), new Voxel(BlockIds.Stone)); // ...under this thin roof.

            Assert.IsFalse(SpawnPointFinder.TryFindSurface(_world, 4, 4, 11, 2, out _),
                "Starting under the roof, the ground below has only 1 block of head room.");
            Assert.IsTrue(SpawnPointFinder.TryFindSurface(_world, 4, 4, 31, 2, out float3 feet));
            Assert.AreEqual(13f, feet.y, "Should stand on the roof instead.");
        }
    }
}
