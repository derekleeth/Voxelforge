using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 2.4: terrain loads around every viewer, nearest first, and unloads (with a margin) when they leave.</summary>
    public class ChunkStreamerTests
    {
        const string TerrainAssetPath = "Assets/_Voxelforge/Data/Terrain/TerrainSettings.asset";
        const string RegistryAssetPath = "Assets/_Voxelforge/Data/Blocks/BlockRegistry.asset";
        const int Height = 2; // Chunk layers per column. Small, to keep the tests fast.
        const int Margin = 2;

        VoxelWorld _world;
        ChunkGenerationScheduler _generator;
        ChunkMeshScheduler _meshes;
        ChunkStreamer _streamer;
        GameObject _parent;

        [SetUp]
        public void SetUp()
        {
            var settings = AssetDatabase.LoadAssetAtPath<TerrainSettings>(TerrainAssetPath);
            var registry = AssetDatabase.LoadAssetAtPath<BlockRegistry>(RegistryAssetPath);
            Assert.IsNotNull(settings, $"Missing {TerrainAssetPath}");
            Assert.IsNotNull(registry, $"Missing {RegistryAssetPath}");

            _world = new VoxelWorld(registry.CreateLookup(Allocator.Persistent));
            _generator = new ChunkGenerationScheduler(_world, new WorldSeed(20261027), settings.ToTerrainParams(),
                settings.CreateBiomeLookup(Allocator.Persistent));
            _parent = new GameObject("ChunkStreamerTests");
            _meshes = new ChunkMeshScheduler(_world, _parent.transform, null);
            _streamer = new ChunkStreamer(_world, _generator, _meshes, Height, Margin, maxUnloadsPerFrame: 4);
        }

        [TearDown]
        public void TearDown()
        {
            _meshes.Dispose();
            _generator.Dispose();
            _world.Dispose();
            Object.DestroyImmediate(_parent);
        }

        /// <summary>A viewer standing in the middle of chunk column (x, z).</summary>
        static ChunkViewer At(int x, int z, int radius) => new ChunkViewer(new float3(x * 32 + 16, 80, z * 32 + 16), radius);

        /// <summary>Runs the streamer until everything it asked for has generated and everything out of range has unloaded.</summary>
        void Settle(params ChunkViewer[] viewers)
        {
            for (int frame = 0; frame < 1000; frame++)
            {
                _streamer.Update(viewers);
                _generator.CompleteAll();
                if (_streamer.UnloadingCount == 0)
                    return;
            }
            Assert.Fail("Streaming never settled.");
        }

        bool IsColumnLoaded(int x, int z)
        {
            for (int y = 0; y < Height; y++)
            {
                if (!_world.TryGetChunk(new ChunkCoord(x, y, z), out _))
                    return false;
            }
            return true;
        }

        [Test]
        public void WantedColumns_AreTheUnionOfViewerCircles()
        {
            // A radius-2 circle covers 13 columns (dx² + dz² <= 4).
            Assert.AreEqual(13, ChunkStreamer.ComputeWantedColumns(new[] { At(0, 0, 2) }).Count);
            Assert.AreEqual(26, ChunkStreamer.ComputeWantedColumns(new[] { At(0, 0, 2), At(100, 0, 2) }).Count,
                "Far apart: two whole circles.");
            Assert.AreEqual(13, ChunkStreamer.ComputeWantedColumns(new[] { At(0, 0, 2), At(0, 0, 2) }).Count,
                "Same spot: no duplicates.");
        }

        [Test]
        public void WantedColumns_AreNearestFirst()
        {
            var viewers = new[] { At(0, 0, 4), At(20, 0, 4) };
            List<int2> columns = ChunkStreamer.ComputeWantedColumns(viewers);

            Assert.That(columns[0], Is.EqualTo(new int2(0, 0)).Or.EqualTo(new int2(20, 0)), "A viewer's own column comes first.");
            int previous = 0;
            foreach (int2 column in columns)
            {
                int distanceSq = math.min(math.dot(column, column), math.dot(column - new int2(20, 0), column - new int2(20, 0)));
                Assert.GreaterOrEqual(distanceSq, previous, $"{column} came after a farther column.");
                previous = distanceSq;
            }
        }

        [Test]
        public void LoadsEveryLayer_AroundEveryViewer()
        {
            Settle(At(0, 0, 1), At(10, 10, 1));

            Assert.AreEqual(2 * 5 * Height, _world.ChunkCount, "Two radius-1 circles of 5 columns each.");
            Assert.IsTrue(IsColumnLoaded(0, 0));
            Assert.IsTrue(IsColumnLoaded(1, 0));
            Assert.IsTrue(IsColumnLoaded(10, 11));
            Assert.IsFalse(IsColumnLoaded(5, 5));
        }

        [Test]
        public void KeepsColumns_InsideTheUnloadMargin()
        {
            Settle(At(0, 0, 1));
            Settle(At(2, 0, 1)); // Column (0, 0) is now 2 away: outside the radius, inside radius + margin.

            Assert.IsTrue(IsColumnLoaded(0, 0), "Should stay loaded inside the margin.");
            Assert.IsTrue(IsColumnLoaded(-1, 0), "3 away is still inside radius 1 + margin 2.");
        }

        [Test]
        public void UnloadsColumns_WhenEveryViewerLeaves()
        {
            Settle(At(0, 0, 1), At(10, 0, 1));
            Settle(At(10, 0, 1)); // The viewer at (0, 0) leaves the game.

            Assert.IsFalse(IsColumnLoaded(0, 0));
            Assert.IsTrue(IsColumnLoaded(10, 0), "The other viewer's terrain stays.");
            Assert.AreEqual(5 * Height, _world.ChunkCount);
            Assert.AreEqual(5, _streamer.ColumnCount);

            Settle();
            Assert.AreEqual(0, _world.ChunkCount, "No viewers: nothing stays loaded.");
        }

        [Test]
        public void Unloading_IsSpreadOverFrames()
        {
            Settle(At(0, 0, 2)); // 13 columns.
            _streamer.Update(new[] { At(100, 0, 2) });

            Assert.AreEqual(13 - 4, _streamer.UnloadingCount, "At most 4 columns unload per frame.");
        }

        [Test]
        public void RemoveChunk_FreesIt_AndForgetsItWasDirty()
        {
            var coord = new ChunkCoord(0, 0, 0);
            VoxelChunk chunk = _world.CreateChunk(coord);
            Assert.AreEqual(1, _world.DirtyCount);

            Assert.IsTrue(_world.RemoveChunk(coord));
            Assert.IsTrue(chunk.IsDisposed);
            Assert.AreEqual(0, _world.DirtyCount);
            Assert.IsFalse(_world.TryTakeDirty(out _));
            Assert.IsFalse(_world.RemoveChunk(coord), "Already gone.");
        }

        [Test]
        public void MeshOfAnUnloadedChunk_IsThrownAway()
        {
            var coord = new ChunkCoord(0, 0, 0);
            _world.CreateChunk(coord);
            _world.SetVoxel(new int3(1, 1, 1), new Voxel(BlockIds.Stone));
            _meshes.Update(); // Starts meshing it.
            Assert.AreEqual(1, _meshes.PendingCount);

            _meshes.CompleteGatherJobs();
            _world.RemoveChunk(coord);
            _meshes.CompletePending();

            Assert.AreEqual(1, _meshes.DiscardedCount);
            Assert.IsFalse(_meshes.TryGetRenderer(coord, out _));
        }

        [Test]
        public void ReleasedRenderers_AreReused()
        {
            var first = new ChunkCoord(0, 0, 0);
            _world.CreateChunk(first);
            _world.SetVoxel(new int3(1, 1, 1), new Voxel(BlockIds.Stone));
            _meshes.CompleteAll();
            Assert.IsTrue(_meshes.TryGetRenderer(first, out ChunkRenderer original));

            _world.RemoveChunk(first);
            _meshes.ReleaseRenderer(first);
            Assert.IsFalse(original.gameObject.activeSelf, "Pooled renderers are hidden.");

            var second = new ChunkCoord(5, 0, 0);
            _world.CreateChunk(second);
            _world.SetVoxel(new int3(5 * 32 + 1, 1, 1), new Voxel(BlockIds.Stone));
            _meshes.CompleteAll();

            Assert.IsTrue(_meshes.TryGetRenderer(second, out ChunkRenderer reused));
            Assert.AreSame(original, reused);
            Assert.IsTrue(reused.gameObject.activeSelf);
            Assert.AreEqual(new Vector3(160, 0, 0), reused.transform.localPosition);
            Assert.Greater(reused.Mesh.vertexCount, 0);
        }
    }
}
