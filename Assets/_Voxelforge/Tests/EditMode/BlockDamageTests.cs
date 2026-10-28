using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Voxelforge.Core;
using Voxelforge.Voxels;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 3.2: blocks wear down through RequestDamageVoxel, break at their durability, and heal when left alone.</summary>
    public class BlockDamageTests
    {
        static readonly PlayerId P1 = new PlayerId(1);
        static readonly PlayerId P2 = new PlayerId(2);
        static readonly Voxel Stone = new Voxel(BlockIds.Stone);
        const ushort Bedrock = BlockIds.Log + 1; // A test-only unbreakable block.

        VoxelWorld _world;
        VoxelWorldEditor _editor;
        readonly List<VoxelEdit> _edits = new List<VoxelEdit>();

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
            blocks[Bedrock] = new BlockInfo { Id = Bedrock, MaxDurability = 0, Flags = BlockFlags.Registered | BlockFlags.Solid };
            _world = new VoxelWorld(blocks);
            _world.CreateChunk(new ChunkCoord(0, 0, 0), Stone);
            _world.CreateChunk(new ChunkCoord(0, 1, 0));

            _edits.Clear();
            _editor = new VoxelWorldEditor(_world, new VoxelDeltaLog(), null);
            _editor.EditApplied += _edits.Add;
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        [Test]
        public void Damage_AddsUp_UntilDurability_ThenBreaksThroughTheEditor()
        {
            var at = new int3(4, 31, 4);
            Assert.AreEqual(VoxelDamageResult.Damaged, _editor.RequestDamageVoxel(at, 25f, P1, 0f));
            Assert.AreEqual(VoxelDamageResult.Damaged, _editor.RequestDamageVoxel(at, 25f, P1, 0.1f));
            Assert.IsTrue(_editor.Damage.TryGet(at, out BlockDamage damage));
            Assert.AreEqual(50f, damage.Damage, 1e-4f);
            Assert.AreEqual(Stone, _world.GetVoxel(at), "Still standing at 50 of 60.");
            Assert.AreEqual(0, _edits.Count);

            Assert.AreEqual(VoxelDamageResult.Broken, _editor.RequestDamageVoxel(at, 10f, P1, 0.2f));
            Assert.IsTrue(_world.GetVoxel(at).IsAir);
            Assert.AreEqual(1, _edits.Count);
            Assert.AreEqual(P1, _edits[0].Instigator);
            Assert.IsTrue(_editor.DeltaLog.TryGet(at, out _), "Breaking is a logged edit.");
            Assert.AreEqual(0, _editor.Damage.Count, "A broken block has no damage left.");
        }

        [Test]
        public void TwoPlayers_MineTogether_AndTheFinisherGetsTheEdit()
        {
            var at = new int3(4, 31, 4);
            _editor.RequestDamageVoxel(at, 30f, P1, 0f);
            Assert.AreEqual(VoxelDamageResult.Broken, _editor.RequestDamageVoxel(at, 30f, P2, 0f));
            Assert.AreEqual(P2, _edits[0].Instigator);
        }

        [Test]
        public void SmallSteps_LikeFixedUpdate_BreakStoneInOneAndAHalfSeconds()
        {
            var at = new int3(4, 31, 4);
            const float power = 40f, dt = 0.02f;
            int steps = 0;
            while (_editor.RequestDamageVoxel(at, power * dt, P1, steps * dt) == VoxelDamageResult.Damaged)
                steps++;
            Assert.AreEqual(1.5f, (steps + 1) * dt, dt);
        }

        [Test]
        public void IdleBlocks_Heal_HitBlocksDont()
        {
            var idle = new int3(1, 31, 1);
            var busy = new int3(2, 31, 2);
            _editor.RequestDamageVoxel(idle, 10f, P1, 0f);
            _editor.RequestDamageVoxel(busy, 10f, P1, 0f);
            _editor.RequestDamageVoxel(busy, 10f, P1, 2.5f);

            Assert.AreEqual(1, _editor.Damage.HealIdle(3f, 3f));
            Assert.IsFalse(_editor.Damage.TryGet(idle, out _));
            Assert.IsTrue(_editor.Damage.TryGet(busy, out BlockDamage damage));
            Assert.AreEqual(20f, damage.Damage, 1e-4f);
        }

        [Test]
        public void AnyEdit_ClearsThatBlocksDamage()
        {
            var at = new int3(4, 31, 4);
            _editor.RequestDamageVoxel(at, 10f, P1, 0f);
            _editor.RequestModifyVoxel(at, BlockIds.TreeTrunk, P2);
            Assert.IsFalse(_editor.Damage.TryGet(at, out _));
        }

        [Test]
        public void ChoppingATrunk_FellsTheWholeTree()
        {
            for (int y = 32; y < 36; y++)
                _editor.RequestModifyVoxel(new int3(8, y, 8), BlockIds.TreeTrunk, P1);
            _edits.Clear();

            Assert.AreEqual(VoxelDamageResult.Broken, _editor.RequestDamageVoxel(new int3(8, 32, 8), 30f, P1, 0f));
            Assert.AreEqual(4, _edits.Count, "Every trunk block is its own edit, so each one drops a log.");
            for (int y = 32; y < 36; y++)
                Assert.IsTrue(_world.GetVoxel(new int3(8, y, 8)).IsAir);
        }

        [Test]
        public void Refusals()
        {
            Assert.AreEqual(VoxelDamageResult.NoInstigator, _editor.RequestDamageVoxel(new int3(1, 31, 1), 10f, default, 0f));
            Assert.AreEqual(VoxelDamageResult.NotSolid, _editor.RequestDamageVoxel(new int3(1, 40, 1), 10f, P1, 0f));
            Assert.AreEqual(VoxelDamageResult.ChunkNotLoaded, _editor.RequestDamageVoxel(new int3(100, 0, 0), 10f, P1, 0f));
            Assert.AreEqual(VoxelDamageResult.NoDamage, _editor.RequestDamageVoxel(new int3(1, 31, 1), 0f, P1, 0f));
            Assert.AreEqual(VoxelDamageResult.NoDamage, _editor.RequestDamageVoxel(new int3(1, 31, 1), float.NaN, P1, 0f));

            var bedrock = new int3(3, 31, 3);
            _editor.RequestModifyVoxel(bedrock, Bedrock, P1);
            Assert.AreEqual(VoxelDamageResult.Unbreakable, _editor.RequestDamageVoxel(bedrock, 1000f, P1, 0f));
            Assert.AreEqual(Bedrock, _world.GetVoxel(bedrock).BlockId);
            Assert.AreEqual(0, _editor.Damage.Count, "Refused requests leave no damage behind.");
        }

        [Test]
        public void CrackStage_GrowsWithDamage()
        {
            Assert.AreEqual(0, BlockCrackRenderer.StageFor(0f, 60));
            Assert.AreEqual(0, BlockCrackRenderer.StageFor(7f, 60));
            Assert.AreEqual(4, BlockCrackRenderer.StageFor(30f, 60));
            Assert.AreEqual(BlockCrackRenderer.StageCount - 1, BlockCrackRenderer.StageFor(59.9f, 60));
            Assert.AreEqual(BlockCrackRenderer.StageCount - 1, BlockCrackRenderer.StageFor(500f, 60));
            Assert.AreEqual(0, BlockCrackRenderer.StageFor(5f, 0));
        }

        [Test]
        public void ItemMesh_IsAWholeCentredCube_WithTheBlocksTextureLayers()
        {
            var blocks = new NativeArray<BlockInfo>(BlockIds.Log + 1, Allocator.Temp);
            blocks[BlockIds.Log] = new BlockInfo
            {
                Id = BlockIds.Log, Flags = BlockFlags.Registered | BlockFlags.Solid, TopLayer = 3, SideLayer = 5, BottomLayer = 3,
            };

            Mesh mesh = BlockItemMesh.Create(blocks, BlockIds.Log);
            try
            {
                Assert.AreEqual(24, mesh.vertexCount, "6 faces of 4 corners.");
                Bounds bounds = mesh.bounds;
                Assert.AreEqual(Vector3.zero, bounds.center);
                Assert.AreEqual(Vector3.one, bounds.size);
                var layers = new HashSet<float>();
                using (Mesh.MeshDataArray data = Mesh.AcquireReadOnlyMeshData(mesh))
                {
                    foreach (ChunkVertex vertex in data[0].GetVertexData<ChunkVertex>())
                        layers.Add(vertex.TextureLayer);
                }
                CollectionAssert.AreEquivalent(new[] { 3f, 5f }, layers, "Top/bottom and side textures.");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
                blocks.Dispose();
            }
        }
    }
}
