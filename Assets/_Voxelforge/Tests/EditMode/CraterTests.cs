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
    /// <summary>
    /// Roadmap 4.3: spells blast craters. The crater math (<see cref="AbilityImpact"/>), the editor's batch damage
    /// (<see cref="VoxelWorldEditor.RequestDamageVoxels"/>), merged drops (<see cref="DropBatcher"/>) and how far a blast
    /// shakes the view (<see cref="BlastShake"/>).
    /// </summary>
    public class CraterTests
    {
        static readonly PlayerId P1 = new PlayerId(1);
        const ushort Dirt = BlockIds.Stone + 100; // Test-only blocks, past every real ID.
        const ushort Totem = Dirt + 1;
        const ushort Bedrock = Dirt + 2;

        // The surface of the stone chunk: y = 31 is its top layer, y = 32 is open air.
        static readonly float3 Surface = new float3(16f, 32f, 16f);
        static readonly int3 SurfaceBlock = new int3(16, 31, 16);

        VoxelWorld _world;
        VoxelWorldEditor _editor;
        readonly List<VoxelEdit> _edits = new List<VoxelEdit>();
        readonly List<VoxelDamageRequest> _requests = new List<VoxelDamageRequest>();
        readonly List<Object> _assets = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            var blocks = new NativeArray<BlockInfo>(Bedrock + 1, Allocator.Persistent);
            blocks[BlockIds.Air] = new BlockInfo { Id = BlockIds.Air, Flags = BlockFlags.Registered | BlockFlags.Transparent };
            blocks[BlockIds.Stone] = new BlockInfo { Id = BlockIds.Stone, MaxDurability = 60, Flags = BlockFlags.Registered | BlockFlags.Solid };
            blocks[BlockIds.TreeTrunk] = new BlockInfo
            {
                Id = BlockIds.TreeTrunk, MaxDurability = 30, Flags = BlockFlags.Registered | BlockFlags.Solid | BlockFlags.Transparent,
            };
            blocks[Dirt] = new BlockInfo { Id = Dirt, MaxDurability = 20, Flags = BlockFlags.Registered | BlockFlags.Solid };
            blocks[Totem] = new BlockInfo
            {
                Id = Totem, MaxDurability = 60, Flags = BlockFlags.Registered | BlockFlags.Solid | BlockFlags.BlastProof,
            };
            blocks[Bedrock] = new BlockInfo { Id = Bedrock, MaxDurability = 0, Flags = BlockFlags.Registered | BlockFlags.Solid };
            _world = new VoxelWorld(blocks);
            _world.CreateChunk(new ChunkCoord(0, 0, 0), new Voxel(BlockIds.Stone));
            _world.CreateChunk(new ChunkCoord(0, 1, 0));

            _edits.Clear();
            _requests.Clear();
            _editor = new VoxelWorldEditor(_world, new VoxelDeltaLog(), null);
            _editor.EditApplied += _edits.Add;
        }

        [TearDown]
        public void TearDown()
        {
            _world.Dispose();
            foreach (Object asset in _assets)
                Object.DestroyImmediate(asset);
            _assets.Clear();
        }

        AbilityDefinition Spell(float radius, float falloff = 0.1f, float flatten = 1f)
        {
            var spell = AbilityDefinition.Create(AbilityIds.Pyroblast, "Test Blast", aoeRadius: radius, craterFalloff: falloff,
                craterFlatten: flatten);
            _assets.Add(spell);
            return spell;
        }

        // ---------------------------------------------------------------- The crater math

        [Test]
        public void NoRadius_DamagesOnlyTheBlockItHit_AsBefore()
        {
            AbilityImpact.Resolve(Spell(0f), Surface, SurfaceBlock, 45f, _requests);
            Assert.AreEqual(1, _requests.Count);
            Assert.AreEqual(SurfaceBlock, _requests[0].Block);
            Assert.AreEqual(45f, _requests[0].Amount);
        }

        [Test]
        public void Crater_HasTheHitBlockAtFullDamage_AndNothingBeyondItsRadius()
        {
            const float radius = 3f;
            AbilityImpact.Resolve(Spell(radius), Surface, SurfaceBlock, 200f, _requests);

            Assert.Greater(_requests.Count, 20);
            var seen = new HashSet<int3>();
            foreach (VoxelDamageRequest request in _requests)
            {
                Assert.IsTrue(seen.Add(request.Block), $"{request.Block} is asked for twice.");
                if (math.all(request.Block == SurfaceBlock))
                {
                    Assert.AreEqual(200f, request.Amount);
                    continue;
                }
                float distance = math.distance((float3)request.Block + 0.5f, Surface);
                Assert.LessOrEqual(distance, radius + 1e-4f, $"{request.Block} is outside the crater.");
                Assert.LessOrEqual(request.Amount, 200f);
                Assert.GreaterOrEqual(request.Amount, 200f * 0.1f - 1e-3f, "Never less than the falloff at the edge.");
            }
            Assert.IsTrue(seen.Contains(SurfaceBlock));
        }

        [Test]
        public void Crater_DamageFalls_FurtherFromTheMiddle()
        {
            AbilityImpact.Resolve(Spell(4f), Surface, SurfaceBlock, 240f, _requests);
            float Amount(int3 cell) => _requests.Find(r => math.all(r.Block == cell)).Amount;

            float near = Amount(new int3(16, 31, 15));
            float middle = Amount(new int3(16, 31, 14));
            float far = Amount(new int3(16, 31, 13));
            Assert.Greater(near, middle);
            Assert.Greater(middle, far);
        }

        [Test]
        public void Flatten_MakesAShallowerCrater_OfTheSameWidth()
        {
            AbilityImpact.Resolve(Spell(4f, flatten: 1f), Surface, SurfaceBlock, 240f, _requests);
            int roundDeepest = Deepest(_requests), roundWidest = Widest(_requests);
            _requests.Clear();
            AbilityImpact.Resolve(Spell(4f, flatten: 0.4f), Surface, SurfaceBlock, 240f, _requests);

            Assert.Greater(Deepest(_requests), roundDeepest, "A flattened crater doesn't reach as deep.");
            Assert.AreEqual(roundWidest, Widest(_requests), "It's just as wide.");
        }

        static int Deepest(List<VoxelDamageRequest> requests)
        {
            int lowest = int.MaxValue;
            foreach (VoxelDamageRequest r in requests)
                lowest = math.min(lowest, r.Block.y);
            return lowest;
        }

        static int Widest(List<VoxelDamageRequest> requests)
        {
            int widest = 0;
            foreach (VoxelDamageRequest r in requests)
                widest = math.max(widest, r.Block.x);
            return widest;
        }

        [Test]
        public void HugeRadius_IsCapped()
        {
            AbilityImpact.Resolve(Spell(1000f), Surface, SurfaceBlock, 240f, _requests);
            float cap = AbilityImpact.MaxRadius;
            Assert.LessOrEqual(_requests.Count, (int)math.ceil(4f / 3f * math.PI * cap * cap * cap * 1.2f));
        }

        // ---------------------------------------------------------------- Batch damage in the editor

        [Test]
        public void Batch_BreaksSoftBlocks_CracksHardOnes_AndLogsEveryBreak()
        {
            var dirt = new int3(16, 31, 15);
            _world.SetVoxel(dirt, new Voxel(Dirt));
            _requests.Add(new VoxelDamageRequest { Block = dirt, Amount = 30f });
            _requests.Add(new VoxelDamageRequest { Block = SurfaceBlock, Amount = 30f });

            VoxelBatchResult result = _editor.RequestDamageVoxels(_requests, P1, 0f, area: true);

            Assert.AreEqual(1, result.Broken);
            Assert.AreEqual(1, result.Damaged);
            Assert.AreEqual(0, result.Skipped);
            Assert.IsTrue(_world.GetVoxel(dirt).IsAir, "Dirt (20) breaks at 30.");
            Assert.AreEqual(BlockIds.Stone, _world.GetVoxel(SurfaceBlock).BlockId, "Stone (60) only cracks at 30.");
            Assert.AreEqual(1, _edits.Count);
            Assert.AreEqual(P1, _edits[0].Instigator);
            Assert.IsTrue(_editor.DeltaLog.TryGet(dirt, out _));
        }

        [Test]
        public void Blast_PassesByTotemsAndUnbreakableBlocks_ButMiningStillBreaksATotem()
        {
            var totem = new int3(16, 31, 15);
            var bedrock = new int3(16, 31, 14);
            _world.SetVoxel(totem, new Voxel(Totem));
            _world.SetVoxel(bedrock, new Voxel(Bedrock));
            _requests.Add(new VoxelDamageRequest { Block = totem, Amount = 999f });
            _requests.Add(new VoxelDamageRequest { Block = bedrock, Amount = 999f });
            _requests.Add(new VoxelDamageRequest { Block = new int3(16, 40, 16), Amount = 999f }); // Air.

            VoxelBatchResult result = _editor.RequestDamageVoxels(_requests, P1, 0f, area: true);

            Assert.AreEqual(0, result.Broken);
            Assert.AreEqual(3, result.Skipped);
            Assert.AreEqual(Totem, _world.GetVoxel(totem).BlockId);
            Assert.AreEqual(Bedrock, _world.GetVoxel(bedrock).BlockId);
            Assert.AreEqual(0, _edits.Count);

            Assert.AreEqual(VoxelDamageResult.Broken, _editor.RequestDamageVoxel(totem, 999f, P1, 0f),
                "Its owner can still pick it up and move it.");
        }

        [Test]
        public void Batch_FellsATreeCaughtInTheBlast()
        {
            for (int y = 32; y < 37; y++)
                _world.SetVoxel(new int3(16, y, 16), new Voxel(BlockIds.TreeTrunk));
            _requests.Add(new VoxelDamageRequest { Block = new int3(16, 32, 16), Amount = 100f });

            _editor.RequestDamageVoxels(_requests, P1, 0f, area: true);

            for (int y = 32; y < 37; y++)
                Assert.IsTrue(_world.GetVoxel(new int3(16, y, 16)).IsAir, $"Trunk at y = {y} should have fallen.");
        }

        [Test]
        public void Batch_NeedsAnInstigator()
        {
            _requests.Add(new VoxelDamageRequest { Block = SurfaceBlock, Amount = 999f });
            VoxelBatchResult result = _editor.RequestDamageVoxels(_requests, default, 0f, area: true);
            Assert.AreEqual(1, result.Skipped);
            Assert.AreEqual(BlockIds.Stone, _world.GetVoxel(SurfaceBlock).BlockId);
        }

        [Test]
        public void Pyroblast_CarvesDirtWide_AndStoneOnlyNearTheMiddle()
        {
            // Top 2 layers are dirt, stone beneath, like real terrain.
            for (int x = 8; x < 25; x++)
                for (int z = 8; z < 25; z++)
                    for (int y = 30; y < 32; y++)
                        _world.SetVoxel(new int3(x, y, z), new Voxel(Dirt));

            AbilityImpact.Resolve(Spell(4f), Surface, SurfaceBlock, 240f, _requests);
            _editor.RequestDamageVoxels(_requests, P1, 0f, area: true);

            // 3.6 m out, a block takes about 47: enough for dirt (20), not stone (60). 2.6 m down takes about 100.
            Assert.IsTrue(_world.GetVoxel(new int3(16, 31, 12)).IsAir, "Dirt near the edge is gone.");
            Assert.IsTrue(_world.GetVoxel(new int3(16, 29, 16)).IsAir, "Stone under the middle is gone.");
            Assert.AreEqual(BlockIds.Stone, _world.GetVoxel(new int3(16, 29, 13)).BlockId, "Stone near the edge stands.");
        }

        // ---------------------------------------------------------------- Merged drops

        [Test]
        public void DropBatcher_MergesNearbyBlocksOfOneKind_IntoOneStack()
        {
            var batcher = new DropBatcher(3f);
            for (int i = 0; i < 5; i++)
                batcher.Add(new float3(0.5f + i * 0.5f, 0.5f, 0.5f), Dirt, 1);
            batcher.Add(new float3(1.5f, 0.5f, 0.5f), BlockIds.Stone, 2);
            batcher.Add(new float3(20.5f, 0.5f, 0.5f), Dirt, 1); // Far away: its own stack.

            var drops = new List<PendingDrop>();
            batcher.Flush(drops);

            Assert.AreEqual(3, drops.Count);
            PendingDrop dirt = drops.Find(d => d.BlockId == Dirt && d.Count == 5);
            Assert.AreEqual(5, dirt.Blocks);
            Assert.AreEqual(1.5f, dirt.Position.x, 1e-4f, "It drops from the broken block nearest the middle.");
            Assert.AreEqual(2, drops.Find(d => d.BlockId == BlockIds.Stone).Count);
            Assert.IsTrue(batcher.IsEmpty, "Flushing starts over.");
        }

        [Test]
        public void DropBatcher_OneBlock_IsOneDrop_RightWhereItWas()
        {
            var batcher = new DropBatcher(3f);
            batcher.Add(new float3(4.5f, 31.5f, 4.5f), Dirt, 1);
            var drops = new List<PendingDrop>();
            batcher.Flush(drops);
            Assert.AreEqual(1, drops.Count);
            Assert.AreEqual(new float3(4.5f, 31.5f, 4.5f), drops[0].Position);
            Assert.AreEqual(1, drops[0].Count);
        }

        // ---------------------------------------------------------------- Camera shake

        [Test]
        public void Shake_IsFullInTheCrater_AndFadesToNothing()
        {
            Assert.AreEqual(0.6f, BlastShake.ShakeAt(2f, 4f, 0.6f), 1e-4f);
            float near = BlastShake.ShakeAt(8f, 4f, 0.6f);
            float far = BlastShake.ShakeAt(16f, 4f, 0.6f);
            Assert.Greater(near, far);
            Assert.Greater(far, 0f);
            Assert.AreEqual(0f, BlastShake.ShakeAt(30f, 4f, 0.6f), "A partner across the map isn't shaken.");
        }
    }
}
