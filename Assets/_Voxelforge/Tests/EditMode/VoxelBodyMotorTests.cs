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
        public void SpeedScale_ZeroWalksAndSprintScalesSpeed()
        {
            CreateRoomWithFloor();
            var walker = BodyStandingAt(new float3(4.5f, 1f, 4.5f));
            var sprinter = walker;
            Run(ref walker, new MotorInput { Move = new float2(1f, 0f) }, 5);
            Run(ref sprinter, new MotorInput { Move = new float2(1f, 0f), SpeedScale = 1.6f }, 5);

            Assert.AreEqual(_settings.WalkSpeed, walker.Velocity.x, 1e-4f);
            Assert.AreEqual(_settings.WalkSpeed * 1.6f, sprinter.Velocity.x, 1e-4f);
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
    
        /// <summary>A stone column from y = 1 up to <paramref name="topY"/> across the room at x = 20.</summary>
        void BuildWallAtX20(int topY)
        {
            for (int y = 1; y <= topY; y++)
            for (int z = 0; z < VoxelConstants.ChunkSize; z++)
                _world.SetVoxel(new int3(20, y, z), new Voxel(BlockIds.Stone));
        }

        [Test]
        public void OneBlockStep_IsClimbedWithoutJumping()
        {
            CreateRoomWithFloor();
            for (int x = 20; x < VoxelConstants.ChunkSize; x++)
            for (int z = 0; z < VoxelConstants.ChunkSize; z++)
                _world.SetVoxel(new int3(x, 1, z), new Voxel(BlockIds.Stone));
            VoxelBody body = BodyStandingAt(new float3(16.5f, 1f, 16.5f));

            float totalStep = 0f;
            for (int i = 0; i < 100; i++)
            {
                VoxelBodyMotor.Step(ref body, new MotorInput { Move = new float2(1f, 0f) }, _settings, _world, Dt);
                totalStep += body.StepOffset;
            }

            Assert.Greater(body.Position.x, 22f, "Should keep walking after the step.");
            Assert.AreEqual(2f, body.FeetPosition(_settings).y, 0.01f);
            Assert.IsTrue(body.Grounded);
            Assert.AreEqual(1f, totalStep, 0.01f, "StepOffset should report the one-block climb.");
        }

        [Test]
        public void TwoBlockWall_IsNotClimbed()
        {
            CreateRoomWithFloor();
            BuildWallAtX20(2);
            VoxelBody body = BodyStandingAt(new float3(16.5f, 1f, 16.5f));

            Run(ref body, new MotorInput { Move = new float2(1f, 0f) }, 100);

            Assert.LessOrEqual(body.Position.x + _settings.HalfExtents.x, 20f);
            Assert.AreEqual(1f, body.FeetPosition(_settings).y, 0.01f);
        }

        [Test]
        public void Step_NeedsHeadRoom()
        {
            CreateRoomWithFloor();
            BuildWallAtX20(1);
            // A ceiling over the step leaves only one block of space on top of it.
            for (int x = 20; x < VoxelConstants.ChunkSize; x++)
            for (int z = 0; z < VoxelConstants.ChunkSize; z++)
                _world.SetVoxel(new int3(x, 3, z), new Voxel(BlockIds.Stone));
            VoxelBody body = BodyStandingAt(new float3(16.5f, 1f, 16.5f));

            Run(ref body, new MotorInput { Move = new float2(1f, 0f) }, 100);

            Assert.LessOrEqual(body.Position.x + _settings.HalfExtents.x, 20f);
            Assert.AreEqual(1f, body.FeetPosition(_settings).y, 0.01f);
        }

        [Test]
        public void AirborneBody_DoesNotStep()
        {
            CreateRoomWithFloor();
            BuildWallAtX20(3);
            // Falling past a ledge whose top (y = 4) is less than a block above the feet.
            var body = new VoxelBody { Position = new float3(19.65f, 3.5f + _settings.HalfExtents.y, 16.5f), Velocity = new float3(0f, -2f, 0f) };

            VoxelBodyMotor.Step(ref body, new MotorInput { Move = new float2(1f, 0f) }, _settings, _world, Dt);

            Assert.LessOrEqual(body.Position.x + _settings.HalfExtents.x, 20f);
            Assert.AreEqual(0f, body.StepOffset);
        }

        [Test]
        public void WalkingOffOneBlockLedge_StaysGrounded()
        {
            CreateRoomWithFloor();
            for (int x = 0; x < 20; x++)
            for (int z = 0; z < VoxelConstants.ChunkSize; z++)
                _world.SetVoxel(new int3(x, 1, z), new Voxel(BlockIds.Stone));
            VoxelBody body = BodyStandingAt(new float3(18.5f, 2f, 16.5f));

            for (int i = 0; i < 50; i++)
            {
                VoxelBodyMotor.Step(ref body, new MotorInput { Move = new float2(1f, 0f) }, _settings, _world, Dt);
                Assert.IsTrue(body.Grounded, $"Should never be airborne going down a step (tick {i}).");
            }
            Assert.AreEqual(1f, body.FeetPosition(_settings).y, 0.01f);
        }

        [Test]
        public void WalkingOffTwoBlockLedge_Falls()
        {
            CreateRoomWithFloor();
            for (int x = 0; x < 20; x++)
            for (int y = 1; y <= 2; y++)
            for (int z = 0; z < VoxelConstants.ChunkSize; z++)
                _world.SetVoxel(new int3(x, y, z), new Voxel(BlockIds.Stone));
            VoxelBody body = BodyStandingAt(new float3(19.5f, 3f, 16.5f));

            bool fell = false;
            for (int i = 0; i < 50; i++)
            {
                VoxelBodyMotor.Step(ref body, new MotorInput { Move = new float2(1f, 0f) }, _settings, _world, Dt);
                fell |= !body.Grounded;
            }

            Assert.IsTrue(fell);
            Assert.AreEqual(1f, body.FeetPosition(_settings).y, 0.01f);
        }

        [Test]
        public void Jump_StillWorksNextToAStep()
        {
            CreateRoomWithFloor();
            BuildWallAtX20(1);
            VoxelBody body = BodyStandingAt(new float3(19.5f, 1f, 16.5f));

            VoxelBodyMotor.Step(ref body, new MotorInput { Jump = true }, _settings, _world, Dt);

            Assert.Greater(body.Velocity.y, 0f);
            Assert.IsFalse(body.Grounded);
            Assert.AreEqual(0f, body.StepOffset);
        }
    }
}
