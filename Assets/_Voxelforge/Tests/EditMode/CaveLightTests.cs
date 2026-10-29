using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using Voxelforge.Core;
using Voxelforge.Gameplay;
using Voxelforge.Voxels;

namespace Voxelforge.Tests
{
    /// <summary>Caves are darker: how much sky a camera sees through the blocks above it.</summary>
    public class CaveLightTests
    {
        static readonly PlayerId P1 = new PlayerId(1);

        VoxelWorld _world;
        VoxelWorldEditor _editor;

        // Chunk (0,0,0) is air; we build roofs in it. The camera sits at (16, 5, 16).
        [SetUp]
        public void SetUp()
        {
            var blocks = new NativeArray<BlockInfo>(BlockIds.Stone + 1, Allocator.Persistent);
            blocks[BlockIds.Air] = new BlockInfo { Id = BlockIds.Air, Flags = BlockFlags.Registered | BlockFlags.Transparent };
            blocks[BlockIds.Stone] = new BlockInfo { Id = BlockIds.Stone, MaxDurability = 60, Flags = BlockFlags.Registered | BlockFlags.Solid };
            _world = new VoxelWorld(blocks);
            _world.CreateChunk(new ChunkCoord(0, 0, 0));
            _editor = new VoxelWorldEditor(_world, new VoxelDeltaLog(), null);
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        static readonly float3 Eye = new float3(16.5f, 5.5f, 16.5f);

        void Roof(int y, int halfWidth)
        {
            for (int x = 16 - halfWidth; x <= 16 + halfWidth; x++)
            for (int z = 16 - halfWidth; z <= 16 + halfWidth; z++)
                _editor.RequestModifyVoxel(new int3(x, y, z), BlockIds.Stone, P1);
        }

        [Test]
        public void OpenSky_IsFullyExposed()
        {
            Assert.AreEqual(1f, CaveLight.SkyExposure(_world, Eye));
            Assert.AreEqual(1f, CaveLight.SkyExposure(null, Eye), "No world yet: full light.");
        }

        [Test]
        public void AWideRoof_HidesAllTheSky()
        {
            Roof(10, 15);
            Assert.AreEqual(0f, CaveLight.SkyExposure(_world, Eye));
        }

        [Test]
        public void ASmallRoofRightOverhead_HidesOnlyPartOfIt()
        {
            Roof(7, 0); // One block straight up: only the straight-up ray hits it.
            float exposure = CaveLight.SkyExposure(_world, Eye);
            Assert.Greater(exposure, 0.5f);
            Assert.Less(exposure, 1f);
        }

        [Test]
        public void AmbientScale_GoesFromTheCaveLevelToFull()
        {
            Assert.AreEqual(0.55f, CaveLight.AmbientScale(0f, 0.55f), 1e-5f);
            Assert.AreEqual(1f, CaveLight.AmbientScale(1f, 0.55f), 1e-5f);
            Assert.AreEqual(0.775f, CaveLight.AmbientScale(0.5f, 0.55f), 1e-5f);
        }
    }
}
