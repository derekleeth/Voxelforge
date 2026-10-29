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
    /// <summary>Roadmap 3.6: Kian's M.G.R.S. and its Mana Bolts.</summary>
    public class ManaBoltTests
    {
        static readonly PlayerId P1 = new PlayerId(1);
        const ushort Bedrock = BlockIds.Workbench + 1; // A test-only unbreakable block.

        AbilityDefinition _bolt;
        VoxelWorld _world;
        VoxelWorldEditor _editor;
        readonly List<ManaBoltEnd> _ended = new List<ManaBoltEnd>();

        // Chunk (0,0,0) is stone up to y = 31; the chunk above is air, so bolts fly over the ground at y = 34.
        [SetUp]
        public void SetUp()
        {
            var blocks = new NativeArray<BlockInfo>(Bedrock + 1, Allocator.Persistent);
            blocks[BlockIds.Air] = new BlockInfo { Id = BlockIds.Air, Flags = BlockFlags.Registered | BlockFlags.Transparent };
            blocks[Bedrock] = new BlockInfo { Id = Bedrock, MaxDurability = 0, Flags = BlockFlags.Registered | BlockFlags.Solid };
            blocks[BlockIds.Dirt] = new BlockInfo { Id = BlockIds.Dirt, MaxDurability = 20, Flags = BlockFlags.Registered | BlockFlags.Solid };
            blocks[BlockIds.Stone] = new BlockInfo { Id = BlockIds.Stone, MaxDurability = 60, Flags = BlockFlags.Registered | BlockFlags.Solid };
            blocks[BlockIds.Torch] = new BlockInfo
            {
                Id = BlockIds.Torch, MaxDurability = 1,
                Flags = BlockFlags.Registered | BlockFlags.Transparent | BlockFlags.Prop | BlockFlags.NeedsSupport,
            };
            _world = new VoxelWorld(blocks);
            _world.CreateChunk(new ChunkCoord(0, 0, 0), new Voxel(BlockIds.Stone));
            _world.CreateChunk(new ChunkCoord(0, 1, 0));
            _editor = new VoxelWorldEditor(_world, new VoxelDeltaLog(), null);
            _ended.Clear();
            // The Mana Bolt (4.1) with D-019's numbers.
            _bolt = AbilityDefinition.Create(AbilityIds.ManaBolt, "Mana Bolt", manaCost: 8f, cooldown: 0.5f, range: 24f,
                projectileSpeed: 40f, blockDamage: 45f);
        }

        [TearDown]
        public void TearDown()
        {
            _world.Dispose();
            Object.DestroyImmediate(_bolt);
        }

        /// <summary>Steps <paramref name="flight"/> at 50 Hz until a bolt ends, for at most two seconds.</summary>
        ManaBoltEnd FlyUntilEnd(ManaBoltFlight flight)
        {
            for (int i = 0; i < 100 && _ended.Count == 0; i++)
                flight.Step(_world, 0.02f, _ended);
            Assert.AreEqual(1, _ended.Count, "The bolt should have ended.");
            return _ended[0];
        }

        static readonly float3 Muzzle = new float3(2.5f, 34.5f, 2.5f);
        static readonly float3 East = new float3(1f, 0f, 0f);

        [Test]
        public void Bolt_HitsTheFirstBlockOnItsPath_EvenWhenAStepIsLongerThanABlock()
        {
            _editor.RequestModifyVoxel(new int3(12, 34, 2), BlockIds.Stone, P1);
            _editor.RequestModifyVoxel(new int3(13, 34, 2), BlockIds.Dirt, P1);
            var flight = new ManaBoltFlight();
            Assert.IsTrue(flight.TryLaunch(Muzzle, East * 5f, _bolt, out ManaBolt bolt));
            Assert.AreEqual(1f, math.length(bolt.Direction), 1e-5f, "Direction is normalized.");
            Assert.AreEqual(AbilityIds.ManaBolt, bolt.AbilityId);

            // One huge step (60 m/s x 0.1 s = 4 blocks a step): the sweep still finds the block between steps.
            AbilityDefinition fast = AbilityDefinition.Create(AbilityIds.ManaBolt, "Fast Bolt", projectileSpeed: 60f);
            var fastFlight = new ManaBoltFlight();
            fastFlight.TryLaunch(Muzzle, East, fast, out _);
            Object.DestroyImmediate(fast);
            for (int i = 0; i < 10 && _ended.Count == 0; i++)
                fastFlight.Step(_world, 0.1f, _ended);
            Assert.AreEqual(1, _ended.Count);
            Assert.IsTrue(_ended[0].HitBlock);
            Assert.AreEqual(new int3(12, 34, 2), _ended[0].Hit.Block, "It stops at the first block, not the one behind it.");
            Assert.AreEqual(new float3(-1f, 0f, 0f), _ended[0].Normal, "It hit the west face.");
            Assert.AreEqual(45f, _ended[0].Damage);
            Assert.AreEqual(0, fastFlight.Count);
        }

        [Test]
        public void Bolt_FizzlesAtItsRange_AndStopsAtTorches()
        {
            var flight = new ManaBoltFlight();
            flight.TryLaunch(Muzzle, East, _bolt, out _);
            ManaBoltEnd end = FlyUntilEnd(flight);
            Assert.IsFalse(end.HitBlock, "Nothing in the way: it flies its range and fizzles.");
            Assert.AreEqual(Muzzle.x + 24f, end.Point.x, 1e-3f);

            // A torch on the ground in its path stops it, like it stops aiming.
            _ended.Clear();
            _editor.RequestModifyVoxel(new int3(8, 32, 2), BlockIds.Torch, P1);
            flight.TryLaunch(new float3(2.5f, 32.5f, 2.5f), East, _bolt, out _);
            end = FlyUntilEnd(flight);
            Assert.IsTrue(end.HitBlock);
            Assert.AreEqual(new int3(8, 32, 2), end.Hit.Block);
        }

        [Test]
        public void Launch_GivesEachBoltItsOwnId_AndNeedsAnAbilityAndADirection()
        {
            // Cooldowns are the caster's job now (AbilityTests); the flight launches whatever it's handed.
            var flight = new ManaBoltFlight();
            Assert.IsTrue(flight.TryLaunch(Muzzle, East, _bolt, out ManaBolt first));
            Assert.IsTrue(flight.TryLaunch(Muzzle, East, _bolt, out ManaBolt second));
            Assert.AreNotEqual(first.Id, second.Id);
            Assert.AreEqual(2, flight.Count);

            Assert.IsFalse(new ManaBoltFlight().TryLaunch(Muzzle, East, null, out _), "No ability.");
            Assert.IsFalse(new ManaBoltFlight().TryLaunch(Muzzle, float3.zero, _bolt, out _), "No direction.");
        }

        [Test]
        public void Hits_BreakDirtInOneAndStoneInTwo_ButNotBedrock()
        {
            // What PlayerStaff does with a hit, on the host: the damage math (AbilityImpact, 4.2), then the edits.
            var damage = new List<VoxelDamageRequest>();
            VoxelDamageResult Shoot(int3 target, float time)
            {
                _ended.Clear();
                var flight = new ManaBoltFlight();
                float3 from = new float3(2.5f, target.y + 0.5f, target.z + 0.5f);
                flight.TryLaunch(from, East, _bolt, out _);
                ManaBoltEnd end = FlyUntilEnd(flight);
                Assert.AreEqual(target, end.Hit.Block);
                Assert.AreEqual(_bolt.Id, end.AbilityId);
                damage.Clear();
                AbilityImpact.Resolve(_bolt, end, damage);
                Assert.AreEqual(1, damage.Count, "A bolt damages only the block it hits until 4.3's craters.");
                Assert.AreEqual(target, damage[0].Block);
                return _editor.RequestDamageVoxel(damage[0].Block, damage[0].Amount, P1, time);
            }

            var dirt = new int3(10, 34, 2);
            _editor.RequestModifyVoxel(dirt, BlockIds.Dirt, P1);
            Assert.AreEqual(VoxelDamageResult.Broken, Shoot(dirt, 0f));
            Assert.AreEqual(BlockIds.Air, _world.GetVoxel(dirt).BlockId);

            var stone = new int3(10, 35, 2);
            _editor.RequestModifyVoxel(stone, BlockIds.Stone, P1);
            Assert.AreEqual(VoxelDamageResult.Damaged, Shoot(stone, 1f));
            Assert.AreEqual(VoxelDamageResult.Broken, Shoot(stone, 1.5f));

            var bedrock = new int3(10, 36, 2);
            _editor.RequestModifyVoxel(bedrock, Bedrock, P1);
            Assert.AreEqual(VoxelDamageResult.Unbreakable, Shoot(bedrock, 2f));
        }

        [Test]
        public void Mgrs_IsRangedNotATool_AndIsCraftedAtAWorkbench()
        {
            ItemDefinition mgrs = ItemDefinition.Create(ItemIds.Mgrs, "M.G.R.S.", ability: _bolt);
            ItemDefinition pickaxe = ItemDefinition.Create(ItemIds.StonePickaxe, "Stone Pickaxe", new ToolPower(3f, 1f, 2f));
            ItemRegistry registry = ItemRegistry.Create(new[] { mgrs, pickaxe });
            BlockDefinition log = BlockDefinition.Create(BlockIds.Log, "Log");
            BlockDefinition stone = BlockDefinition.Create(BlockIds.Stone, "Stone");
            BlockDefinition ore = BlockDefinition.Create(BlockIds.ManaCrystalOre, "Mana Crystal Ore");
            RecipeDefinition recipe = RecipeDefinition.Create(new ItemAmount(mgrs, 1), true,
                new ItemAmount(log, 3), new ItemAmount(stone, 2), new ItemAmount(ore, 5));
            RecipeBook book = RecipeBook.Create(new[] { recipe });
            try
            {
                Assert.IsTrue(mgrs.IsRanged);
                Assert.IsFalse(mgrs.IsTool, "Mining with it is bare hands; Mine fires it instead.");
                Assert.IsTrue(registry.TryGetAbility(ItemIds.Mgrs, out AbilityDefinition ability));
                Assert.AreEqual(45f, ability.BlockDamage);
                Assert.IsFalse(registry.TryGetAbility(ItemIds.StonePickaxe, out _));
                Assert.IsFalse(registry.TryGetAbility(BlockIds.Stone, out _));

                var bag = new List<ItemStack>
                {
                    new ItemStack(BlockIds.Log, 3), new ItemStack(BlockIds.Stone, 2), new ItemStack(BlockIds.ManaCrystalOre, 5),
                };
                Assert.AreEqual(CraftCheck.NeedsWorkbench, book.Check(0, bag, nearWorkbench: false));
                Assert.AreEqual(CraftCheck.Ok, book.Check(0, bag, nearWorkbench: true));
                var stacks = new ItemStacks(0);
                stacks.Restore(bag, bag.Count);
                Assert.IsTrue(stacks.TryExchange(recipe.InputStacks(), recipe.Output.ToStack(), new List<int>()));
                Assert.AreEqual(1, stacks.CountOf(ItemIds.Mgrs));
                Assert.AreEqual(0, stacks.CountOf(BlockIds.ManaCrystalOre));
                Assert.AreEqual(CraftCheck.MissingInputs, book.Check(0, stacks.Stacks, nearWorkbench: true), "Everything was used.");
            }
            finally
            {
                Object.DestroyImmediate(mgrs);
                Object.DestroyImmediate(pickaxe);
                Object.DestroyImmediate(registry);
                Object.DestroyImmediate(log);
                Object.DestroyImmediate(stone);
                Object.DestroyImmediate(ore);
                Object.DestroyImmediate(recipe);
                Object.DestroyImmediate(book);
            }
        }

        [Test]
        public void Registry_FlagsARangedItemWithNoSpeed()
        {
            AbilityDefinition broken = AbilityDefinition.Create(AbilityIds.ManaBolt, "Broken Bolt", projectileSpeed: 0f);
            ItemDefinition item = ItemDefinition.Create(ItemIds.Mgrs, "Broken Staff", ability: broken);
            ItemRegistry registry = ItemRegistry.Create(new[] { item });
            try
            {
                Assert.IsTrue(registry.Validate(null).Exists(e => e.Contains("speed")));
            }
            finally
            {
                Object.DestroyImmediate(item);
                Object.DestroyImmediate(registry);
                Object.DestroyImmediate(broken);
            }
        }
    }
}
