using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using Voxelforge.Voxels;

namespace Voxelforge.Tests
{
    /// <summary>The box-vs-block movement the host runs for every player (Roadmap 2.1, D-007).</summary>
    public class VoxelBodyMotorTests
    {
        const float Dt = 0.02f;
        VoxelWorld _world;
        MotorSettings _settings;

        [SetUp]
        public void SetUp()
        {
            var blocks = new NativeArray<BlockInfo>(4, Allocator.Persistent);
            blocks[BlockIds.Air] = new BlockInfo { Id = BlockIds.Air, Flags = BlockFlags.Registered | BlockFlags.Transparent };
            blocks[BlockIds.Stone] = new BlockInfo { Id = BlockIds.Stone, Flags = BlockFlags.Registered | BlockFlags.Solid };
            _world = new VoxelWorld(blocks);
            _settings = MotorSettings.Default;
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        /// <summary>An air chunk at (0, 0, 0) with a stone floor along y = 0.</summary>
        void CreateRoomWithFloor()
        {
            _world.CreateChunk(new ChunkCoord(0, 0, 0));
            for (int x = 0; x < VoxelConstants.ChunkSize; x++)
            for (int z = 0; z < VoxelConstants.ChunkSize; z++)
                _world.SetVoxel(new int3(x, 0, z), new Voxel(BlockIds.Stone));
        }

        VoxelBody BodyStandingAt(float3 feet) => new VoxelBody
        {
            Position = feet + new float3(0f, _settings.HalfExtents.y + 0.001f, 0f),
            Grounded = true,
        };

        void Run(ref VoxelBody body, MotorInput input, int ticks)
        {
            for (int i = 0; i < ticks; i++)
                VoxelBodyMotor.Step(ref body, input, _settings, _world, Dt);
        }

        [Test]
        public void Body_FallsAndLandsOnFloor()
        {
            CreateRoomWithFloor();
            var body = new VoxelBody { Position = new float3(16.5f, 12f, 16.5f) };

            Run(ref body, default, 200);

            Assert.IsTrue(body.Grounded);
            Assert.AreEqual(1f, body.FeetPosition(_settings).y, 0.01f);
            Assert.AreEqual(0f, body.Velocity.y, 1e-4f);
        }

        [Test]
        public void Wall_BlocksSidewaysMovement()
        {
            CreateRoomWithFloor();
            for (int y = 1; y <= 3; y++)
            for (int z = 0; z < VoxelConstants.ChunkSize; z++)
                _world.SetVoxel(new int3(20, y, z), new Voxel(BlockIds.Stone));
            VoxelBody body = BodyStandingAt(new float3(16.5f, 1f, 16.5f));

            Run(ref body, new MotorInput { Move = new float2(1f, 0f) }, 100);

            Assert.LessOrEqual(body.Position.x + _settings.HalfExtents.x, 20f);
            Assert.Greater(body.Position.x + _settings.HalfExtents.x, 19.9f, "Should walk right up to the wall.");
            Assert.IsTrue(body.Grounded);
        }

        [Test]
        public void Body_WaitsWhileItsChunkIsNotGenerated()
        {
            var start = new float3(16.5f, 12f, 16.5f);
            var body = new VoxelBody { Position = start };

            Run(ref body, new MotorInput { Move = new float2(1f, 0f) }, 50);

            Assert.IsTrue(body.WaitingForWorld);
            Assert.AreEqual(start, body.Position);
        }

        [Test]
        public void UngeneratedChunkBelow_CountsAsSolid()
        {
            _world.CreateChunk(new ChunkCoord(0, 0, 0)); // All air, nothing generated under it.
            var body = new VoxelBody { Position = new float3(16.5f, 12f, 16.5f) };

            Run(ref body, default, 200);

            Assert.IsTrue(body.Grounded);
            Assert.AreEqual(0f, body.FeetPosition(_settings).y, 0.01f, "Should stop on top of the missing chunk, not fall into it.");
        }

        [Test]
        public void Jump_ReachesConfiguredHeight()
        {
            CreateRoomWithFloor();
            VoxelBody body = BodyStandingAt(new float3(16.5f, 1f, 16.5f));

            VoxelBodyMotor.Step(ref body, new MotorInput { Jump = true }, _settings, _world, Dt);
            float highest = 0f;
            for (int i = 0; i < 100; i++)
            {
                VoxelBodyMotor.Step(ref body, default, _settings, _world, Dt);
                highest = math.max(highest, body.FeetPosition(_settings).y - 1f);
            }

            Assert.AreEqual(_settings.JumpHeight, highest, 0.1f);
            Assert.IsTrue(body.Grounded, "Should land again.");
        }

        [Test]
        public void Ceiling_StopsJump()
        {
            CreateRoomWithFloor();
            for (int x = 0; x < VoxelConstants.ChunkSize; x++)
            for (int z = 0; z < VoxelConstants.ChunkSize; z++)
                _world.SetVoxel(new int3(x, 3, z), new Voxel(BlockIds.Stone)); // Head room of 2 blocks.
            VoxelBody body = BodyStandingAt(new float3(16.5f, 1f, 16.5f));

            VoxelBodyMotor.Step(ref body, new MotorInput { Jump = true }, _settings, _world, Dt);
            float highestHead = 0f;
            for (int i = 0; i < 20; i++)
            {
                VoxelBodyMotor.Step(ref body, default, _settings, _world, Dt);
                highestHead = math.max(highestHead, body.Position.y + _settings.HalfExtents.y);
            }

            Assert.LessOrEqual(highestHead, 3f);
            Assert.LessOrEqual(body.Velocity.y, 0f);
        }

        [Test]
        public void FastFall_DoesNotTunnelThroughThinFloor()
        {
            _world.CreateChunk(new ChunkCoord(0, 0, 0));
            _world.CreateChunk(new ChunkCoord(0, -1, 0));
            _world.SetVoxel(new int3(16, 0, 16), new Voxel(BlockIds.Stone)); // One block to land on.
            var body = new VoxelBody { Position = new float3(16.5f, 30f, 16.5f), Velocity = new float3(0f, -_settings.MaxFallSpeed, 0f) };

            Run(ref body, default, 100);

            Assert.AreEqual(1f, body.FeetPosition(_settings).y, 0.01f);
        }
    }
}
