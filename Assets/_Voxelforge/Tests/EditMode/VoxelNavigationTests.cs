using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using Voxelforge.Voxels;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 5.1: the voxel nav grid, path searches over it, and creatures repathing when blocks change (D-032).</summary>
    public class VoxelNavigationTests
    {
        const float Dt = 0.02f;
        static readonly Voxel Stone = new Voxel(BlockIds.Stone);

        VoxelWorld _world;
        VoxelNavGrid _grid;
        NavPathService _paths;
        NavAgentProfile _profile;

        [SetUp]
        public void SetUp()
        {
            var blocks = new NativeArray<BlockInfo>(4, Allocator.Persistent);
            blocks[BlockIds.Air] = new BlockInfo { Id = BlockIds.Air, Flags = BlockFlags.Registered | BlockFlags.Transparent };
            blocks[BlockIds.Stone] = new BlockInfo { Id = BlockIds.Stone, Flags = BlockFlags.Registered | BlockFlags.Solid };
            _world = new VoxelWorld(blocks);
            _profile = NavAgentProfile.Default;
        }

        [TearDown]
        public void TearDown()
        {
            _paths?.Dispose();
            _grid?.Dispose();
            _world.Dispose();
            _paths = null;
            _grid = null;
        }

        /// <summary>An air chunk with a stone floor along y = 0 and the nav grid baked. Unloaded chunks around it are walls.</summary>
        void CreateRoom(ChunkCoord coord = default)
        {
            _world.CreateChunk(coord);
            int3 origin = coord.WorldOrigin;
            for (int x = 0; x < VoxelConstants.ChunkSize; x++)
            for (int z = 0; z < VoxelConstants.ChunkSize; z++)
                _world.SetVoxel(origin + new int3(x, 0, z), Stone);
            StartGrid();
        }

        void StartGrid()
        {
            _grid = new VoxelNavGrid(_world);
            _paths = new NavPathService(_grid);
            _grid.Flush();
        }

        void Fill(int3 min, int3 max, Voxel voxel)
        {
            for (int y = min.y; y <= max.y; y++)
            for (int z = min.z; z <= max.z; z++)
            for (int x = min.x; x <= max.x; x++)
                _world.SetVoxel(new int3(x, y, z), voxel);
        }

        NavPathQuery FindPath(int3 from, int3 to)
        {
            NavPathQuery query = _paths.Request(from, to, _profile);
            _paths.CompleteAll();
            return query;
        }

        static float PathLength(int3 from, NavPathQuery query)
        {
            float length = 0f;
            int3 previous = from;
            foreach (int3 waypoint in query.Waypoints)
            {
                length += math.distance((float3)previous, waypoint);
                previous = waypoint;
            }
            return length;
        }

        [Test]
        public void Bake_MatchesMotorSolidity()
        {
            _world.CreateChunk(new ChunkCoord(0, 0, 0));
            var random = new Random(5);
            for (int i = 0; i < 3000; i++)
                _world.SetVoxel(random.NextInt3(0, VoxelConstants.ChunkSize), Stone);
            StartGrid();

            NavGridView view = _grid.View;
            for (int y = -1; y <= VoxelConstants.ChunkSize; y++)
            for (int z = -1; z <= VoxelConstants.ChunkSize; z++)
            for (int x = -1; x <= VoxelConstants.ChunkSize; x++)
            {
                var block = new int3(x, y, z);
                Assert.AreEqual(VoxelBodyMotor.IsSolid(block, _world), view.IsSolid(block), $"Block {block}");
            }
        }

        [Test]
        public void UniformChunks_AreOpenOrSolidWithoutBits()
        {
            _world.CreateChunk(new ChunkCoord(0, 0, 0));
            _world.CreateChunk(new ChunkCoord(0, -1, 0), Stone);
            StartGrid();

            Assert.AreEqual(2, _grid.BakedCount);
            Assert.IsFalse(_grid.View.IsSolid(new int3(5, 5, 5)));
            Assert.IsTrue(_grid.View.IsSolid(new int3(5, -5, 5)));
            Assert.IsTrue(_grid.View.IsStandable(new int3(5, 0, 5), 2));
        }

        [Test]
        public void Edit_InUniformChunk_FlipsOnlyThatBlock()
        {
            _world.CreateChunk(new ChunkCoord(0, 0, 0));
            StartGrid();
            long before = _grid.Stamp;

            _world.SetVoxel(new int3(3, 4, 5), Stone);

            Assert.IsTrue(_grid.View.IsSolid(new int3(3, 4, 5)));
            Assert.IsFalse(_grid.View.IsSolid(new int3(4, 4, 5)));
            Assert.IsFalse(_grid.View.IsSolid(new int3(3, 5, 5)));
            Assert.Greater(_grid.Stamp, before);
        }

        [Test]
        public void Edit_WhileBaking_IsBakedAgain()
        {
            _world.CreateChunk(new ChunkCoord(0, 0, 0));
            _world.SetVoxel(new int3(1, 1, 1), Stone); // Not uniform, so it needs a bake job.
            _grid = new VoxelNavGrid(_world);
            _paths = new NavPathService(_grid);
            _grid.Update(); // The bake has copied the blocks and is running.

            _world.SetVoxel(new int3(8, 8, 8), Stone);
            _grid.Flush();

            Assert.IsTrue(_grid.View.IsSolid(new int3(1, 1, 1)));
            Assert.IsTrue(_grid.View.IsSolid(new int3(8, 8, 8)));
        }

        [Test]
        public void UnloadingAChunk_MakesItSolid()
        {
            _world.CreateChunk(new ChunkCoord(0, 0, 0));
            StartGrid();
            Assert.IsFalse(_grid.View.IsSolid(new int3(5, 5, 5)));

            _world.RemoveChunk(new ChunkCoord(0, 0, 0));

            Assert.IsTrue(_grid.View.IsSolid(new int3(5, 5, 5)));
            Assert.AreEqual(0, _grid.BakedCount);
        }

        [Test]
        public void OpenFloor_PathIsCompleteAndPulledStraight()
        {
            CreateRoom();
            var start = new int3(2, 1, 2);
            var goal = new int3(20, 1, 25);

            NavPathQuery path = FindPath(start, goal);

            Assert.AreEqual(NavPathStatus.Complete, path.Status);
            Assert.AreEqual(1, path.Waypoints.Count, "Open ground is one straight walk.");
            Assert.AreEqual(goal, path.Waypoints[path.Waypoints.Count - 1]);
        }

        [Test]
        public void StartInTheAir_SnapsToTheGround()
        {
            CreateRoom();

            NavPathQuery path = FindPath(new int3(2, 3, 2), new int3(10, 1, 2));

            Assert.AreEqual(NavPathStatus.Complete, path.Status);
            Assert.AreEqual(new int3(10, 1, 2), path.Waypoints[path.Waypoints.Count - 1]);
        }

        [Test]
        public void Wall_PathGoesThroughTheGap()
        {
            CreateRoom();
            Fill(new int3(10, 1, 0), new int3(10, 3, 25), Stone); // Open from z = 26 to 31.
            NavPathQuery path = FindPath(new int3(5, 1, 5), new int3(15, 1, 5));

            Assert.AreEqual(NavPathStatus.Complete, path.Status);
            int furthest = 0;
            foreach (int3 waypoint in path.Waypoints)
                furthest = math.max(furthest, waypoint.z);
            Assert.GreaterOrEqual(furthest, 26, "Should go round the end of the wall.");
        }

        [Test]
        public void BuildingAWallAllTheWayAcross_LeavesOnlyAPartialPath()
        {
            CreateRoom();
            Assert.AreEqual(NavPathStatus.Complete, FindPath(new int3(5, 1, 5), new int3(15, 1, 5)).Status);

            Fill(new int3(10, 1, 0), new int3(10, 2, 31), Stone);
            NavPathQuery path = FindPath(new int3(5, 1, 5), new int3(15, 1, 5));

            Assert.AreEqual(NavPathStatus.Partial, path.Status);
            Assert.AreEqual(9, path.Waypoints[path.Waypoints.Count - 1].x, "Gets as close as it can, against the wall.");
        }

        [Test]
        public void DiggingThroughAWall_GivesAShorterPath()
        {
            CreateRoom();
            Fill(new int3(10, 1, 0), new int3(10, 3, 25), Stone);
            var start = new int3(5, 1, 5);
            float around = PathLength(start, FindPath(start, new int3(15, 1, 5)));

            Fill(new int3(10, 1, 4), new int3(10, 2, 6), Voxel.Air);
            NavPathQuery dug = FindPath(start, new int3(15, 1, 5));

            Assert.AreEqual(NavPathStatus.Complete, dug.Status);
            Assert.Less(PathLength(start, dug), around - 20f);
        }

        [Test]
        public void Ledges_OneBlockIsClimbedTwoIsNot()
        {
            CreateRoom();
            Fill(new int3(10, 1, 0), new int3(31, 1, 31), Stone);
            Assert.AreEqual(NavPathStatus.Complete, FindPath(new int3(5, 1, 5), new int3(15, 2, 5)).Status);

            Fill(new int3(10, 2, 0), new int3(31, 2, 31), Stone);
            Assert.AreEqual(NavPathStatus.Partial, FindPath(new int3(5, 1, 5), new int3(15, 3, 5)).Status);
        }

        [Test]
        public void Ledges_NoHeadRoomToRise_CantClimb()
        {
            CreateRoom();
            Fill(new int3(10, 1, 0), new int3(31, 1, 31), Stone); // A one-block step...
            Fill(new int3(0, 3, 0), new int3(9, 3, 31), Stone); // ...under a ceiling 2 blocks up, so there's no room to rise.

            Assert.AreEqual(NavPathStatus.Partial, FindPath(new int3(5, 1, 5), new int3(15, 2, 5)).Status);
        }

        [Test]
        public void Drops_ThreeBlocksYesFourNo()
        {
            CreateRoom();
            Fill(new int3(0, 1, 0), new int3(9, 3, 31), Stone); // A shelf 3 high.
            Assert.AreEqual(NavPathStatus.Complete, FindPath(new int3(5, 4, 5), new int3(15, 1, 5)).Status);

            Fill(new int3(0, 4, 0), new int3(9, 4, 31), Stone); // Now 4 high.
            Assert.AreEqual(NavPathStatus.Partial, FindPath(new int3(5, 5, 5), new int3(15, 1, 5)).Status);
        }

        [Test]
        public void DiagonalWall_CornersAreNotCut()
        {
            CreateRoom();
            // Blocks touching only at their corners, from one side of the room to the other.
            for (int i = 0; i < VoxelConstants.ChunkSize; i++)
                Fill(new int3(i, 1, i), new int3(i, 2, i), Stone);

            NavPathQuery path = FindPath(new int3(20, 1, 5), new int3(5, 1, 20));

            Assert.AreEqual(NavPathStatus.Partial, path.Status);
        }

        [Test]
        public void UnloadedChunks_BlockTheWay()
        {
            CreateRoom();

            NavPathQuery path = FindPath(new int3(5, 1, 5), new int3(40, 1, 5));

            Assert.AreEqual(NavPathStatus.Partial, path.Status);
            Assert.AreEqual(31, path.Waypoints[path.Waypoints.Count - 1].x);
        }

        [Test]
        public void NegativeCoordinates_Work()
        {
            CreateRoom(new ChunkCoord(-1, 0, -1));

            NavPathQuery path = FindPath(new int3(-28, 1, -28), new int3(-3, 1, -5));

            Assert.AreEqual(NavPathStatus.Complete, path.Status);
            Assert.AreEqual(new int3(-3, 1, -5), path.Waypoints[path.Waypoints.Count - 1]);
        }

        [Test]
        public void Budget_RunsOut_GivesAPartialPathTowardTheGoal()
        {
            CreateRoom();
            Fill(new int3(10, 1, 0), new int3(10, 3, 25), Stone);
            _profile.MaxNodes = 20;

            NavPathQuery path = FindPath(new int3(5, 1, 5), new int3(15, 1, 5));

            Assert.AreEqual(NavPathStatus.Partial, path.Status);
            Assert.LessOrEqual(path.NodesExpanded, 20);
            Assert.Greater(path.Waypoints[path.Waypoints.Count - 1].x, 5);
        }

        [Test]
        public void Boxed_In_HasNoPath()
        {
            CreateRoom();
            Fill(new int3(4, 1, 4), new int3(6, 3, 6), Stone);
            Fill(new int3(5, 1, 5), new int3(5, 2, 5), Voxel.Air); // A 1x2 hole in a solid block.

            Assert.AreEqual(NavPathStatus.NoPath, FindPath(new int3(5, 1, 5), new int3(15, 1, 5)).Status);
        }

        // --- The follower, walking a real motor body ---

        VoxelBody BodyAt(float3 feet, in MotorSettings motor) => new VoxelBody
        {
            Position = feet + new float3(0f, motor.HalfExtents.y + 0.001f, 0f),
            Grounded = true,
        };

        /// <summary>Ticks the follower and motor together, finishing searches between ticks as the game does between frames.</summary>
        void Walk(NavPathFollower follower, ref VoxelBody body, in MotorSettings motor, ref float now, int ticks)
        {
            for (int i = 0; i < ticks && !follower.HasArrived; i++, now += Dt)
            {
                MotorInput input = follower.Tick(body.FeetPosition(motor), now);
                VoxelBodyMotor.Step(ref body, input, motor, _world, Dt);
                _paths.CompleteAll();
            }
        }

        [Test]
        public void Follower_WalksTheBodyAroundAWallToTheGoal()
        {
            CreateRoom();
            Fill(new int3(10, 1, 0), new int3(10, 3, 25), Stone);
            MotorSettings motor = MotorSettings.Default;
            var follower = new NavPathFollower(_paths, NavAgentProfile.For(motor));
            VoxelBody body = BodyAt(new float3(5.5f, 1f, 5.5f), motor);
            follower.SetDestination(new float3(15.5f, 1f, 5.5f));
            float now = 0f;

            Walk(follower, ref body, motor, ref now, 1500);

            Assert.IsTrue(follower.HasArrived);
            Assert.Less(math.distance(body.FeetPosition(motor).xz, new float2(15.5f, 5.5f)), 0.5f);
        }

        [Test]
        public void Follower_ClimbsAStepWithTheMotorsAutoStep()
        {
            CreateRoom();
            Fill(new int3(10, 1, 0), new int3(31, 1, 31), Stone);
            MotorSettings motor = MotorSettings.Default;
            var follower = new NavPathFollower(_paths, NavAgentProfile.For(motor));
            VoxelBody body = BodyAt(new float3(5.5f, 1f, 5.5f), motor);
            follower.SetDestination(new float3(15.5f, 2f, 5.5f));
            float now = 0f;

            Walk(follower, ref body, motor, ref now, 500);

            Assert.IsTrue(follower.HasArrived);
            Assert.AreEqual(2f, body.FeetPosition(motor).y, 0.05f);
        }

        [Test]
        public void Follower_WallBuiltAcrossItsPath_AsksForANewPath()
        {
            CreateRoom();
            MotorSettings motor = MotorSettings.Default;
            var follower = new NavPathFollower(_paths, NavAgentProfile.For(motor));
            VoxelBody body = BodyAt(new float3(5.5f, 1f, 5.5f), motor);
            follower.SetDestination(new float3(25.5f, 1f, 5.5f));
            float now = 0f;
            Walk(follower, ref body, motor, ref now, 10);
            int requests = follower.RequestCount;
            Assert.AreEqual(1, requests);

            Fill(new int3(15, 1, 0), new int3(15, 3, 25), Stone); // Someone builds a wall ahead of it.
            Walk(follower, ref body, motor, ref now, 40);

            Assert.Greater(follower.RequestCount, requests, "The wall changed its chunk, so it should repath.");
            Walk(follower, ref body, motor, ref now, 2000);
            Assert.IsTrue(follower.HasArrived, "And find its way round the wall.");
        }

        [Test]
        public void Follower_DestinationMoves_Repaths()
        {
            CreateRoom();
            MotorSettings motor = MotorSettings.Default;
            var follower = new NavPathFollower(_paths, NavAgentProfile.For(motor));
            VoxelBody body = BodyAt(new float3(5.5f, 1f, 5.5f), motor);
            follower.SetDestination(new float3(25.5f, 1f, 5.5f));
            float now = 0f;
            Walk(follower, ref body, motor, ref now, 10);

            follower.SetDestination(new float3(25.5f, 1f, 25.5f));
            Walk(follower, ref body, motor, ref now, 1000);

            Assert.AreEqual(2, follower.RequestCount);
            Assert.Less(math.distance(body.FeetPosition(motor).xz, new float2(25.5f, 25.5f)), 0.5f);
        }

        [Test]
        public void Follower_Stop_StandsStill()
        {
            CreateRoom();
            var follower = new NavPathFollower(_paths, _profile);
            follower.SetDestination(new float3(25.5f, 1f, 5.5f));
            follower.Tick(new float3(5.5f, 1f, 5.5f), 0f);
            _paths.CompleteAll();

            follower.Stop();

            Assert.AreEqual(float2.zero, follower.Tick(new float3(5.5f, 1f, 5.5f), 1f).Move);
            Assert.IsFalse(follower.HasDestination);
        }

        [Test]
        public void Profile_FromMotor_MatchesTheHeroBody()
        {
            NavAgentProfile profile = NavAgentProfile.For(MotorSettings.Default);

            Assert.AreEqual(2, profile.Height);
            Assert.AreEqual(1, profile.MaxStepUp);
            Assert.AreEqual(0.3f, profile.Radius, 1e-5f);
        }
    }
}
