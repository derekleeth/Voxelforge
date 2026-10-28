using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Voxelforge.Core;
using Voxelforge.Gameplay;
using Voxelforge.Voxels;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 3.5: torches and workbenches in the world, tool power, and crafting.</summary>
    public class PropsAndToolsTests
    {
        static readonly PlayerId P1 = new PlayerId(1);
        static readonly float3 Hero = new float3(0.3f, 0.9f, 0.3f);

        // Stone fills chunk (0,0,0); the chunk above is air, so the ground's top is at y = 32.
        static readonly VoxelHit GroundTop = new VoxelHit { Block = new int3(4, 31, 4), Normal = new int3(0, 1, 0) };

        VoxelWorld _world;
        VoxelWorldEditor _editor;

        [SetUp]
        public void SetUp()
        {
            var blocks = new NativeArray<BlockInfo>(BlockIds.Workbench + 1, Allocator.Persistent);
            blocks[BlockIds.Air] = new BlockInfo { Id = BlockIds.Air, Flags = BlockFlags.Registered | BlockFlags.Transparent };
            blocks[BlockIds.Dirt] = new BlockInfo
                { Id = BlockIds.Dirt, MaxDurability = 20, Flags = BlockFlags.Registered | BlockFlags.Solid, Harvest = HarvestKind.Any };
            blocks[BlockIds.Stone] = new BlockInfo
                { Id = BlockIds.Stone, MaxDurability = 60, Flags = BlockFlags.Registered | BlockFlags.Solid, Harvest = HarvestKind.Pick };
            blocks[BlockIds.Torch] = new BlockInfo
            {
                Id = BlockIds.Torch, MaxDurability = 1, DropId = BlockIds.Torch, DropCount = 1,
                Flags = BlockFlags.Registered | BlockFlags.Transparent | BlockFlags.Prop | BlockFlags.NeedsSupport,
            };
            blocks[BlockIds.Workbench] = new BlockInfo
            {
                Id = BlockIds.Workbench, MaxDurability = 40, Harvest = HarvestKind.Axe,
                Flags = BlockFlags.Registered | BlockFlags.Solid | BlockFlags.Transparent | BlockFlags.Prop,
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

        // ---- Torches and workbenches in the world ----

        [Test]
        public void Torch_NeedsSupport_ButMayGoWhereAHeroStands()
        {
            Assert.AreEqual(PlacementResult.Ok,
                VoxelPlacement.Check(_world, GroundTop, BlockIds.Torch, Bodies(new float3(4.5f, 32f, 4.5f)), out int3 cell),
                "You can walk through a torch, so it may go at your feet.");
            Assert.AreEqual(PlacementResult.BlocksCharacter,
                VoxelPlacement.Check(_world, GroundTop, BlockIds.Workbench, Bodies(new float3(4.5f, 32f, 4.5f)), out _),
                "A workbench is solid.");

            // Against the side of a single floating block: no floor below the cell, but the block beside it holds it.
            _editor.RequestModifyVoxel(new int3(10, 40, 10), BlockIds.Stone, P1);
            var side = new VoxelHit { Block = new int3(10, 40, 10), Normal = new int3(1, 0, 0) };
            Assert.AreEqual(PlacementResult.Ok, VoxelPlacement.Check(_world, side, BlockIds.Torch, null, out cell));
            Assert.AreEqual(new int3(11, 40, 10), cell);

            // Under the floating block: only a ceiling, and torches don't hang from ceilings.
            var under = new VoxelHit { Block = new int3(10, 40, 10), Normal = new int3(0, -1, 0) };
            Assert.AreEqual(PlacementResult.NoSupport, VoxelPlacement.Check(_world, under, BlockIds.Torch, null, out _));
        }

        [Test]
        public void Torch_Breaks_WhenItsSupportIsMined()
        {
            var floor = new int3(4, 31, 4);
            var torch = new int3(4, 32, 4);
            var raised = new List<VoxelEdit>();
            _editor.EditApplied += raised.Add;
            Assert.AreEqual(VoxelEditResult.Applied, _editor.RequestModifyVoxel(torch, BlockIds.Torch, P1));

            Assert.AreEqual(VoxelEditResult.Applied, _editor.RequestModifyVoxel(floor, BlockIds.Air, P1));

            Assert.AreEqual(BlockIds.Air, _world.GetVoxel(torch).BlockId, "The torch fell with its floor.");
            Assert.AreEqual(3, raised.Count, "Placed, floor removed, torch broken: each one raised, so the torch drops.");
            Assert.AreEqual(BlockIds.Torch, raised[2].Before.BlockId);
            Assert.AreEqual(P1, raised[2].Instigator);
        }

        [Test]
        public void Torch_OnAWall_SurvivesItsFloorGoing()
        {
            _editor.RequestModifyVoxel(new int3(5, 32, 4), BlockIds.Stone, P1); // A wall east of the torch.
            _editor.RequestModifyVoxel(new int3(4, 32, 4), BlockIds.Torch, P1);

            _editor.RequestModifyVoxel(new int3(4, 31, 4), BlockIds.Air, P1);

            Assert.AreEqual(BlockIds.Torch, _world.GetVoxel(new int3(4, 32, 4)).BlockId);
        }

        [Test]
        public void AimRays_StopAtTorches_SoTheyCanBeMined()
        {
            var torch = new int3(4, 32, 4);
            _editor.RequestModifyVoxel(torch, BlockIds.Torch, P1);

            Assert.IsTrue(VoxelQueries.Raycast(_world, new float3(4.5f, 34.5f, 4.5f), new float3(0f, -1f, 0f), 5f, out VoxelHit hit));
            Assert.AreEqual(torch, hit.Block);
            Assert.IsTrue(VoxelQueries.Raycast(_world, new float3(4.5f, 34.5f, 4.5f), new float3(0f, -1f, 0f), 5f, out hit,
                opaqueOnly: true));
            Assert.AreEqual(new int3(4, 31, 4), hit.Block, "Cameras look past torches.");

            Assert.AreEqual(VoxelDamageResult.Broken, _editor.RequestDamageVoxel(torch, 1f, P1, 0f));
            Assert.AreEqual(BlockIds.Air, _world.GetVoxel(torch).BlockId);
        }

        // ---- Tools ----

        [Test]
        public void ToolPower_MultipliesByHarvestKind()
        {
            ItemDefinition destiny = ItemDefinition.Create(ItemIds.PickOfDestiny, "The Pick of Destiny", new ToolPower(5f, 5f, 3f));
            ItemDefinition pickaxe = ItemDefinition.Create(ItemIds.StonePickaxe, "Stone Pickaxe", new ToolPower(3f, 1f, 2f));
            ItemRegistry items = ItemRegistry.Create(new[] { destiny, pickaxe });
            try
            {
                Assert.AreEqual(5f, items.ToolPowerOf(ItemIds.PickOfDestiny).For(HarvestKind.Pick));
                Assert.AreEqual(5f, items.ToolPowerOf(ItemIds.PickOfDestiny).For(HarvestKind.Axe), "An axe on the other side.");
                Assert.AreEqual(1f, items.ToolPowerOf(ItemIds.StonePickaxe).For(HarvestKind.Axe), "A pickaxe chops like hands.");
                Assert.AreEqual(1f, items.ToolPowerOf(BlockIds.Dirt).For(HarvestKind.Pick), "Holding a block is bare hands.");
                Assert.AreEqual(1f, items.ToolPowerOf(BlockIds.Air).For(HarvestKind.Any));

                // Stone (60) with The Pick of Destiny at 40 x 5 per second, in fixed steps: 0.3 s.
                float power = 40f * items.ToolPowerOf(ItemIds.PickOfDestiny).For(_world.Blocks[BlockIds.Stone].Harvest);
                int steps = 0;
                while (_editor.RequestDamageVoxel(new int3(1, 31, 1), power * 0.02f, P1, steps * 0.02f) != VoxelDamageResult.Broken)
                    steps++;
                Assert.AreEqual(15, steps + 1);
            }
            finally
            {
                Object.DestroyImmediate(destiny);
                Object.DestroyImmediate(pickaxe);
                Object.DestroyImmediate(items);
            }
        }

        // ---- Crafting ----

        [Test]
        public void Exchange_IsAllOrNothing()
        {
            var bag = new ItemStacks();
            bag.Add(BlockIds.Log, 3, out _);
            bag.Add(BlockIds.Stone, 2, out _);
            var changed = new List<int>();
            var inputs = new[] { new ItemStack(BlockIds.Log, 2), new ItemStack(BlockIds.Stone, 3) };

            Assert.IsFalse(bag.TryExchange(inputs, new ItemStack(ItemIds.StonePickaxe, 1), changed), "One Stone short.");
            Assert.AreEqual(3, bag.CountOf(BlockIds.Log), "Nothing was taken.");
            Assert.AreEqual(0, changed.Count);

            bag.Add(BlockIds.Stone, 1, out _);
            Assert.IsTrue(bag.TryExchange(inputs, new ItemStack(ItemIds.StonePickaxe, 1), changed));
            Assert.AreEqual(1, bag.CountOf(BlockIds.Log));
            Assert.AreEqual(0, bag.CountOf(BlockIds.Stone));
            Assert.AreEqual(1, bag.CountOf(ItemIds.StonePickaxe));
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2 }, changed, "Both inputs and the new pickaxe slot.");
        }

        [Test]
        public void Exchange_RefusesAnOutputThatWontFit()
        {
            var bag = new ItemStacks();
            bag.Add(BlockIds.Log, 1, out _);
            bag.Add(BlockIds.Torch, ItemStacks.MaxPerItem - 2, out _);
            var changed = new List<int>();

            Assert.IsFalse(bag.TryExchange(new[] { new ItemStack(BlockIds.Log, 1) }, new ItemStack(BlockIds.Torch, 4), changed));
            Assert.AreEqual(1, bag.CountOf(BlockIds.Log));
        }

        [Test]
        public void Recipes_CheckInputsAndTheWorkbench()
        {
            BlockDefinition log = BlockDefinition.Create(BlockIds.Log, "Log");
            BlockDefinition stone = BlockDefinition.Create(BlockIds.Stone, "Stone");
            BlockDefinition bench = BlockDefinition.Create(BlockIds.Workbench, "Workbench");
            ItemDefinition pickaxe = ItemDefinition.Create(ItemIds.StonePickaxe, "Stone Pickaxe");
            RecipeDefinition makeBench = RecipeDefinition.Create(new ItemAmount(bench, 1), false, new ItemAmount(log, 4));
            RecipeDefinition makePickaxe = RecipeDefinition.Create(new ItemAmount(pickaxe, 1), true,
                new ItemAmount(log, 2), new ItemAmount(stone, 3));
            RecipeBook book = RecipeBook.Create(new[] { makeBench, makePickaxe });
            try
            {
                CollectionAssert.IsEmpty(book.Validate());
                var bag = new List<ItemStack> { new ItemStack(BlockIds.Log, 4), new ItemStack(BlockIds.Stone, 3) };

                Assert.AreEqual(CraftCheck.Ok, book.Check(0, bag, nearWorkbench: false), "A workbench is made anywhere.");
                Assert.AreEqual(CraftCheck.NeedsWorkbench, book.Check(1, bag, nearWorkbench: false));
                Assert.AreEqual(CraftCheck.Ok, book.Check(1, bag, nearWorkbench: true));
                bag[1] = new ItemStack(BlockIds.Stone, 2);
                Assert.AreEqual(CraftCheck.MissingInputs, book.Check(1, bag, nearWorkbench: true));
                Assert.AreEqual(CraftCheck.UnknownRecipe, book.Check(7, bag, nearWorkbench: true));
            }
            finally
            {
                Object.DestroyImmediate(log);
                Object.DestroyImmediate(stone);
                Object.DestroyImmediate(bench);
                Object.DestroyImmediate(pickaxe);
                Object.DestroyImmediate(makeBench);
                Object.DestroyImmediate(makePickaxe);
                Object.DestroyImmediate(book);
            }
        }

        [Test]
        public void NearBlock_FindsAWorkbenchInReach()
        {
            _editor.RequestModifyVoxel(new int3(8, 32, 4), BlockIds.Workbench, P1);

            Assert.IsTrue(RecipeBook.IsNearBlock(_world, new float3(4.5f, 32.9f, 4.5f), BlockIds.Workbench, 4));
            Assert.IsFalse(RecipeBook.IsNearBlock(_world, new float3(4.5f, 32.9f, 4.5f), BlockIds.Workbench, 3));
            Assert.IsFalse(RecipeBook.IsNearBlock(null, float3.zero, BlockIds.Workbench, 4));
        }
    }
}
