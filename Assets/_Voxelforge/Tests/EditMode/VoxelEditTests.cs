using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using Voxelforge.Core;
using Voxelforge.Voxels;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 3.1: every block change goes through RequestModifyVoxel, is logged, and survives regeneration.</summary>
    public class VoxelEditTests
    {
        static readonly PlayerId P1 = new PlayerId(1);
        static readonly PlayerId P2 = new PlayerId(2);
        static readonly Voxel Stone = new Voxel(BlockIds.Stone);
        static readonly Voxel Trunk = new Voxel(BlockIds.TreeTrunk);

        VoxelWorld _world;
        VoxelWorldEditor _editor;
        readonly List<VoxelEdit> _edits = new List<VoxelEdit>();

        [SetUp]
        public void SetUp()
        {
            var blocks = new NativeArray<BlockInfo>(BlockIds.TreeTrunk + 1, Allocator.Persistent);
            blocks[BlockIds.Air] = new BlockInfo { Id = BlockIds.Air, Flags = BlockFlags.Registered | BlockFlags.Transparent };
            blocks[BlockIds.Stone] = new BlockInfo { Id = BlockIds.Stone, Flags = BlockFlags.Registered | BlockFlags.Solid };
            blocks[BlockIds.TreeTrunk] = new BlockInfo
            {
                Id = BlockIds.TreeTrunk, Flags = BlockFlags.Registered | BlockFlags.Solid | BlockFlags.Transparent,
            };
            _world = new VoxelWorld(blocks);
            // Two layers of chunks around the origin: stone below y = 32, air above.
            for (int cx = -1; cx <= 0; cx++)
            for (int cz = -1; cz <= 0; cz++)
            {
                _world.CreateChunk(new ChunkCoord(cx, 0, cz), Stone);
                _world.CreateChunk(new ChunkCoord(cx, 1, cz));
            }
            DrainDirty();

            _edits.Clear();
            _editor = new VoxelWorldEditor(_world, new VoxelDeltaLog(), null);
            _editor.EditApplied += _edits.Add;
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        void DrainDirty()
        {
            while (_world.TryTakeDirty(out _)) { }
        }

        HashSet<ChunkCoord> TakeDirty()
        {
            var dirty = new HashSet<ChunkCoord>();
            while (_world.TryTakeDirty(out ChunkCoord coord))
                dirty.Add(coord);
            return dirty;
        }

        [Test]
        public void Dig_SetsBlock_LogsIt_AndRaisesEvent()
        {
            var at = new int3(5, 31, 5);
            Assert.AreEqual(VoxelEditResult.Applied, _editor.RequestModifyVoxel(at, BlockIds.Air, P1));

            Assert.IsTrue(_world.GetVoxel(at).IsAir);
            Assert.IsTrue(_editor.DeltaLog.TryGet(at, out VoxelDelta delta));
            Assert.AreEqual(Voxel.Air, delta.Current);
            Assert.AreEqual(Stone, delta.Generated);
            Assert.AreEqual(P1, delta.Instigator);
            Assert.AreEqual(1, _editor.DeltaLog.BlockCount);
            Assert.AreEqual(1, _editor.DeltaLog.ChunkCount);

            Assert.AreEqual(1, _edits.Count);
            Assert.AreEqual(Stone, _edits[0].Before);
            Assert.AreEqual(Voxel.Air, _edits[0].After);
            Assert.AreEqual(P1, _edits[0].Instigator);
        }

        [Test]
        public void SameBlock_IsUnchanged_AndNotLogged()
        {
            Assert.AreEqual(VoxelEditResult.Unchanged, _editor.RequestModifyVoxel(new int3(5, 10, 5), BlockIds.Stone, P1));
            Assert.AreEqual(0, _editor.DeltaLog.BlockCount);
            Assert.AreEqual(0, _edits.Count);
            Assert.AreEqual(0, _world.DirtyCount);
        }

        [Test]
        public void UnloadedChunk_IsRejected()
        {
            Assert.AreEqual(VoxelEditResult.ChunkNotLoaded, _editor.RequestModifyVoxel(new int3(100, 10, 5), BlockIds.Air, P1));
            Assert.AreEqual(VoxelEditResult.ChunkNotLoaded, _editor.RequestModifyVoxel(new int3(5, -1, 5), BlockIds.Air, P1));
            Assert.AreEqual(0, _editor.DeltaLog.BlockCount);
        }

        [Test]
        public void UnknownBlock_IsRejected()
        {
            Assert.AreEqual(VoxelEditResult.UnknownBlock, _editor.RequestModifyVoxel(new int3(5, 40, 5), BlockIds.Grass, P1),
                "Grass isn't registered in this test world.");
            Assert.AreEqual(VoxelEditResult.UnknownBlock, _editor.RequestModifyVoxel(new int3(5, 40, 5), 999, P1));
            Assert.IsTrue(_world.GetVoxel(new int3(5, 40, 5)).IsAir);
        }

        [Test]
        public void NoInstigator_IsRejected()
        {
            Assert.AreEqual(VoxelEditResult.NoInstigator, _editor.RequestModifyVoxel(new int3(5, 31, 5), BlockIds.Air, PlayerId.None));
            Assert.AreEqual(Stone, _world.GetVoxel(new int3(5, 31, 5)));
        }

        [Test]
        public void PuttingTheGeneratedBlockBack_RemovesTheDelta()
        {
            var at = new int3(5, 31, 5);
            _editor.RequestModifyVoxel(at, BlockIds.Air, P1);
            Assert.AreEqual(VoxelEditResult.Applied, _editor.RequestModifyVoxel(at, BlockIds.Stone, P2));

            Assert.AreEqual(0, _editor.DeltaLog.BlockCount);
            Assert.AreEqual(0, _editor.DeltaLog.ChunkCount);
            Assert.IsFalse(_editor.DeltaLog.HasDeltas(VoxelMath.WorldToChunk(at)));
        }

        [Test]
        public void EditingTwice_KeepsOneEntry_WithTheOriginalGeneratedBlock()
        {
            var at = new int3(5, 40, 5);
            _editor.RequestModifyVoxel(at, BlockIds.Stone, P1);
            _editor.RequestModifyVoxel(at, BlockIds.TreeTrunk, P2);

            Assert.AreEqual(1, _editor.DeltaLog.BlockCount);
            Assert.IsTrue(_editor.DeltaLog.TryGet(at, out VoxelDelta delta));
            Assert.AreEqual(Trunk, delta.Current);
            Assert.AreEqual(Voxel.Air, delta.Generated);
            Assert.AreEqual(P2, delta.Instigator, "The last player to change a block owns it.");
        }

        [Test]
        public void EditInsideChunk_RemeshesOnlyThatChunk()
        {
            _editor.RequestModifyVoxel(new int3(5, 31, 5), BlockIds.Air, P1);
            // y = 31 is the chunk's top layer, so the chunk above remeshes too.
            CollectionAssert.AreEquivalent(new[] { new ChunkCoord(0, 0, 0), new ChunkCoord(0, 1, 0) }, TakeDirty());

            _editor.RequestModifyVoxel(new int3(5, 20, 5), BlockIds.Air, P1);
            CollectionAssert.AreEquivalent(new[] { new ChunkCoord(0, 0, 0) }, TakeDirty());
        }

        [Test]
        public void EditOnChunkCorner_RemeshesEveryChunkSharingIt()
        {
            _editor.RequestModifyVoxel(new int3(0, 31, 0), BlockIds.Air, P1);
            HashSet<ChunkCoord> dirty = TakeDirty();
            Assert.AreEqual(8, dirty.Count, "A corner block touches 8 chunks, and all of them are loaded here.");
            Assert.IsTrue(dirty.Contains(new ChunkCoord(-1, 1, -1)));
        }

        [Test]
        public void ChoppingATrunk_ClearsTheWholeTrunk_AcrossChunks()
        {
            // A trunk from y = 30 to 35 standing on stone (y = 29), crossing from chunk layer 0 into layer 1.
            for (int y = 30; y <= 35; y++)
                _world.SetVoxel(new int3(3, y, 3), Trunk);
            _world.SetVoxel(new int3(3, 36, 3), Stone); // Something that isn't trunk, above it.

            Assert.AreEqual(VoxelEditResult.Applied, _editor.RequestModifyVoxel(new int3(3, 32, 3), BlockIds.Air, P1));

            for (int y = 30; y <= 35; y++)
                Assert.IsTrue(_world.GetVoxel(new int3(3, y, 3)).IsAir, $"Trunk at y = {y} should be gone.");
            Assert.AreEqual(Stone, _world.GetVoxel(new int3(3, 29, 3)), "The ground under the tree stays.");
            Assert.AreEqual(Stone, _world.GetVoxel(new int3(3, 36, 3)));
            Assert.AreEqual(6, _edits.Count);
            Assert.AreEqual(6, _editor.DeltaLog.BlockCount);
        }

        [Test]
        public void PlacingTrunk_DoesNotClearAnything()
        {
            _world.SetVoxel(new int3(3, 33, 3), Trunk);
            _editor.RequestModifyVoxel(new int3(3, 32, 3), BlockIds.TreeTrunk, P1);
            Assert.AreEqual(Trunk, _world.GetVoxel(new int3(3, 33, 3)));
            Assert.AreEqual(1, _edits.Count);
        }

        [Test]
        public void ReapplyDeltas_RestoresEditsInRegeneratedChunk()
        {
            _editor.RequestModifyVoxel(new int3(5, 20, 5), BlockIds.Air, P1);
            _editor.RequestModifyVoxel(new int3(6, 20, 5), BlockIds.TreeTrunk, P1);

            _world.RemoveChunk(new ChunkCoord(0, 0, 0));
            VoxelChunk chunk = _world.CreateChunk(new ChunkCoord(0, 0, 0), Stone); // "Generated" again, all stone.
            VoxelWorldEditor.ReapplyDeltas(chunk, _editor.DeltaLog);

            Assert.IsTrue(_world.GetVoxel(new int3(5, 20, 5)).IsAir);
            Assert.AreEqual(Trunk, _world.GetVoxel(new int3(6, 20, 5)));
            Assert.AreEqual(Stone, _world.GetVoxel(new int3(7, 20, 5)));
            Assert.AreEqual(2, _editor.DeltaLog.BlockCount, "Putting edits back isn't a new edit.");
        }
    }

    /// <summary>Roadmap 3.1: edits come back when the real generator builds a chunk again (after it streamed out).</summary>
    public class VoxelEditRegenerationTests
    {
        const string TerrainAssetPath = "Assets/_Voxelforge/Data/Terrain/TerrainSettings.asset";
        const string RegistryAssetPath = "Assets/_Voxelforge/Data/Blocks/BlockRegistry.asset";

        [Test]
        public void EditsSurviveUnloadAndRegeneration_InMixedAndUniformChunks()
        {
            var settings = AssetDatabase.LoadAssetAtPath<TerrainSettings>(TerrainAssetPath);
            var registry = AssetDatabase.LoadAssetAtPath<BlockRegistry>(RegistryAssetPath);
            Assert.IsNotNull(settings, $"Missing {TerrainAssetPath}");
            Assert.IsNotNull(registry, $"Missing {RegistryAssetPath}");

            var world = new VoxelWorld(registry.CreateLookup(Allocator.Persistent));
            var generator = new ChunkGenerationScheduler(world, new WorldSeed(20261027), settings.ToTerrainParams(),
                settings.CreateBiomeLookup(Allocator.Persistent));
            try
            {
                var editor = new VoxelWorldEditor(world, new VoxelDeltaLog(), null);
                generator.DeltaLog = editor.DeltaLog;

                var ground = new ChunkCoord(0, 0, 0); // Deep underground: mostly stone, never empty.
                var sky = new ChunkCoord(0, 7, 0);    // High in the sky: all air, so it's stored uniform.
                generator.Request(ground);
                generator.Request(sky);
                generator.CompleteAll();
                Assert.IsTrue(world.TryGetChunk(sky, out VoxelChunk skyChunk) && skyChunk.IsUniform, "Expected an empty sky chunk.");

                int3 underground = ground.WorldOrigin + new int3(4, 4, 4);
                int3 high = sky.WorldOrigin + new int3(4, 4, 4);
                ushort undergroundBefore = world.GetVoxel(underground).BlockId;
                ushort placed = undergroundBefore == BlockIds.Air ? BlockIds.Stone : BlockIds.Air;
                Assert.AreEqual(VoxelEditResult.Applied, editor.RequestModifyVoxel(underground, placed, new PlayerId(1)));
                Assert.AreEqual(VoxelEditResult.Applied, editor.RequestModifyVoxel(high, BlockIds.Stone, new PlayerId(2)));

                world.RemoveChunk(ground);
                world.RemoveChunk(sky);
                generator.Request(ground);
                generator.Request(sky);
                generator.CompleteAll();

                Assert.AreEqual(placed, world.GetVoxel(underground).BlockId);
                Assert.AreEqual(BlockIds.Stone, world.GetVoxel(high).BlockId);
                Assert.IsTrue(world.GetVoxel(high + new int3(1, 0, 0)).IsAir, "The rest of the sky chunk is still air.");
                Assert.AreEqual(2, editor.DeltaLog.BlockCount);
            }
            finally
            {
                generator.Dispose();
                world.Dispose();
            }
        }
    }
}
