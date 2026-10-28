using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using Voxelforge.Core;
using Voxelforge.Gameplay;
using Voxelforge.Voxels;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 3.3: where a block may be placed, and which block from the bag gets placed.</summary>
    public class BlockPlacementTests
    {
        static readonly PlayerId P1 = new PlayerId(1);
        static readonly float3 Hero = new float3(0.3f, 0.9f, 0.3f); // MotorSettings.Default.HalfExtents

        // Stone fills y 0-31 of chunk (0,0,0); chunk (0,1,0) above it is air. So the ground's top is at y = 32.
        static readonly VoxelHit GroundTop = new VoxelHit { Block = new int3(4, 31, 4), Normal = new int3(0, 1, 0) };

        VoxelWorld _world;
        VoxelWorldEditor _editor;

        [SetUp]
        public void SetUp()
        {
            var blocks = new NativeArray<BlockInfo>(BlockIds.Log + 1, Allocator.Persistent);
            blocks[BlockIds.Air] = new BlockInfo { Id = BlockIds.Air, Flags = BlockFlags.Registered | BlockFlags.Transparent };
            blocks[BlockIds.Stone] = new BlockInfo { Id = BlockIds.Stone, MaxDurability = 60, Flags = BlockFlags.Registered | BlockFlags.Solid };
            blocks[BlockIds.TreeTrunk] = new BlockInfo
            {
                Id = BlockIds.TreeTrunk, MaxDurability = 30, Flags = BlockFlags.Registered | BlockFlags.Solid | BlockFlags.Transparent,
            };
            _world = new VoxelWorld(blocks);
            _world.CreateChunk(new ChunkCoord(0, 0, 0), new Voxel(BlockIds.Stone));
            _world.CreateChunk(new ChunkCoord(0, 1, 0));
            _editor = new VoxelWorldEditor(_world, new VoxelDeltaLog(), null);
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        static List<BodyBounds> Bodies(params float3[] feet)
        {
            var bodies = new List<BodyBounds>();
            foreach (float3 f in feet)
                bodies.Add(BodyBounds.FromFeet(f, Hero));
            return bodies;
        }

        [Test]
        public void OpenCell_AgainstTheAimedFace_IsAllowed()
        {
            Assert.AreEqual(PlacementResult.Ok, VoxelPlacement.Check(_world, GroundTop, Bodies(), out int3 cell));
            Assert.AreEqual(new int3(4, 32, 4), cell, "Snaps to the cell on the far side of the face.");

            var side = new VoxelHit { Block = new int3(4, 31, 4), Normal = new int3(-1, 0, 0) };
            Assert.AreEqual(PlacementResult.Occupied, VoxelPlacement.Check(_world, side, null, out cell));
            Assert.AreEqual(new int3(3, 31, 4), cell, "Underground, the side cell is stone.");
        }

        [Test]
        public void FilledCell_IsOccupied_IncludingAnInvisibleTreeTrunk()
        {
            var below = new VoxelHit { Block = new int3(4, 30, 4), Normal = new int3(0, 1, 0) };
            Assert.AreEqual(PlacementResult.Occupied, VoxelPlacement.Check(_world, below, null, out _));

            Assert.AreEqual(VoxelEditResult.Applied, _editor.RequestModifyVoxel(new int3(4, 32, 4), BlockIds.TreeTrunk, P1));
            Assert.AreEqual(PlacementResult.Occupied, VoxelPlacement.Check(_world, GroundTop, null, out _));
        }

        [Test]
        public void NoFace_OrUnloadedChunk_IsRefused()
        {
            var inside = new VoxelHit { Block = new int3(4, 20, 4), Normal = int3.zero };
            Assert.AreEqual(PlacementResult.NoFace, VoxelPlacement.Check(_world, inside, null, out _));

            var topOfLoaded = new VoxelHit { Block = new int3(4, 63, 4), Normal = new int3(0, 1, 0) };
            Assert.AreEqual(PlacementResult.ChunkNotLoaded, VoxelPlacement.Check(_world, topOfLoaded, null, out _));
        }

        [Test]
        public void ABlock_CantGoInsideAHero()
        {
            Assert.AreEqual(PlacementResult.BlocksCharacter,
                VoxelPlacement.Check(_world, GroundTop, Bodies(new float3(4.5f, 32f, 4.5f)), out _), "Standing in the cell.");
            Assert.AreEqual(PlacementResult.BlocksCharacter,
                VoxelPlacement.Check(_world, GroundTop, Bodies(new float3(5.25f, 32f, 4.5f)), out _), "Half a hero over the edge.");
        }

        [Test]
        public void TheOtherPlayer_BlocksToo()
        {
            List<BodyBounds> bodies = Bodies(new float3(10.5f, 32f, 10.5f), new float3(4.5f, 32.5f, 4.2f));
            Assert.AreEqual(PlacementResult.BlocksCharacter, VoxelPlacement.Check(_world, GroundTop, bodies, out _));
        }

        [Test]
        public void JustTouching_IsAllowed_SoYouCanPillarJump()
        {
            Assert.AreEqual(PlacementResult.Ok,
                VoxelPlacement.Check(_world, GroundTop, Bodies(new float3(4.5f, 33f, 4.5f)), out _), "Feet exactly on the block's top.");
            Assert.AreEqual(PlacementResult.Ok,
                VoxelPlacement.Check(_world, GroundTop, Bodies(new float3(5.3f, 32f, 4.5f)), out _), "Side against the block's side.");
        }

        static List<BlockStack> Bag(params (ushort id, ushort count)[] slots)
        {
            var stacks = new List<BlockStack>();
            foreach ((ushort id, ushort count) in slots)
                stacks.Add(new BlockStack(id, count));
            return stacks;
        }

        [Test]
        public void Selection_CyclesTheNonEmptyStacks_AndWraps()
        {
            List<BlockStack> bag = Bag((BlockIds.Dirt, 4), (BlockIds.Stone, 0), (BlockIds.Log, 2), (BlockIds.Sand, 1));
            Assert.AreEqual(BlockIds.Log, BlockSelection.Next(bag, BlockIds.Dirt), "Skips the empty Stone slot.");
            Assert.AreEqual(BlockIds.Sand, BlockSelection.Next(bag, BlockIds.Log));
            Assert.AreEqual(BlockIds.Dirt, BlockSelection.Next(bag, BlockIds.Sand), "Wraps to the start.");
            Assert.AreEqual(BlockIds.Sand, BlockSelection.Previous(bag, BlockIds.Dirt), "Wraps to the end.");
            Assert.AreEqual(BlockIds.Dirt, BlockSelection.Previous(bag, BlockIds.Log));
        }

        [Test]
        public void Selection_MovesOn_WhenItsStackRunsOut()
        {
            List<BlockStack> bag = Bag((BlockIds.Dirt, 4), (BlockIds.Stone, 0), (BlockIds.Log, 2));
            Assert.AreEqual(BlockIds.Dirt, BlockSelection.Fix(bag, BlockIds.Dirt), "Still has some.");
            Assert.AreEqual(BlockIds.Log, BlockSelection.Fix(bag, BlockIds.Stone), "Ran out: the next one along.");
            Assert.AreEqual(BlockIds.Dirt, BlockSelection.Fix(bag, BlockIds.Air), "Nothing yet: the first one.");
        }

        [Test]
        public void Selection_OfAnEmptyBag_IsNothing()
        {
            Assert.AreEqual(BlockIds.Air, BlockSelection.Fix(Bag(), BlockIds.Air));
            Assert.AreEqual(BlockIds.Air, BlockSelection.Fix(Bag((BlockIds.Dirt, 0)), BlockIds.Dirt));
            Assert.AreEqual(BlockIds.Air, BlockSelection.Next(Bag((BlockIds.Dirt, 0)), BlockIds.Dirt));
            Assert.AreEqual(BlockIds.Air, BlockSelection.Previous(Bag(), BlockIds.Air));
        }

        [Test]
        public void Selection_WithOneBlockType_StaysOnIt()
        {
            List<BlockStack> bag = Bag((BlockIds.Dirt, 3));
            Assert.AreEqual(BlockIds.Dirt, BlockSelection.Next(bag, BlockIds.Dirt));
            Assert.AreEqual(BlockIds.Dirt, BlockSelection.Previous(bag, BlockIds.Dirt));
        }
    }
}
