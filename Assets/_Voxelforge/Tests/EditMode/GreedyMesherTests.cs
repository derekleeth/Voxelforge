using System.Diagnostics;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Voxelforge.Voxels;
using static Voxelforge.Voxels.VoxelConstants;
using Debug = UnityEngine.Debug;

namespace Voxelforge.Tests
{
    /// <summary>
    /// Meshes real chunks through the full pipeline (gather job, then Burst greedy mesh job, then Unity
    /// Mesh) and checks face counts, culling across chunk borders, winding, UVs and stale results.
    /// </summary>
    public class GreedyMesherTests
    {
        static readonly ChunkCoord Origin = new ChunkCoord(0, 0, 0);

        VoxelWorld _world;
        ChunkMeshScheduler _scheduler;
        GameObject _parent;

        [SetUp]
        public void SetUp()
        {
            var blocks = new NativeArray<BlockInfo>(5, Allocator.Persistent);
            blocks[BlockIds.Air] = new BlockInfo { Id = BlockIds.Air, Flags = BlockFlags.Registered | BlockFlags.Transparent };
            blocks[BlockIds.Grass] = Solid(BlockIds.Grass);
            blocks[BlockIds.Dirt] = Solid(BlockIds.Dirt);
            blocks[BlockIds.Stone] = Solid(BlockIds.Stone);
            blocks[BlockIds.ManaCrystalOre] = Solid(BlockIds.ManaCrystalOre);

            _world = new VoxelWorld(blocks);
            _parent = new GameObject("GreedyMesherTests");
            _scheduler = new ChunkMeshScheduler(_world, _parent.transform, null);
        }

        [TearDown]
        public void TearDown()
        {
            _scheduler.Dispose();
            _world.Dispose();
            Object.DestroyImmediate(_parent);
        }

        static BlockInfo Solid(ushort id) => new BlockInfo { Id = id, Flags = BlockFlags.Registered | BlockFlags.Solid };

        static Voxel Stone => new Voxel(BlockIds.Stone);

        /// <summary>Meshes everything dirty and returns the chunk's quad count (0 if it has no mesh).</summary>
        int QuadsIn(ChunkCoord coord)
        {
            _scheduler.CompleteAll();
            return _scheduler.TryGetRenderer(coord, out ChunkRenderer chunkRenderer) ? chunkRenderer.Mesh.vertexCount / 4 : 0;
        }

        Mesh MeshOf(ChunkCoord coord)
        {
            _scheduler.CompleteAll();
            Assert.IsTrue(_scheduler.TryGetRenderer(coord, out ChunkRenderer chunkRenderer), $"{coord} has no renderer.");
            return chunkRenderer.Mesh;
        }

        [Test]
        public void SingleBlock_HasSixQuads()
        {
            _world.CreateChunk(Origin).SetVoxel(new int3(5, 5, 5), Stone);
            Assert.AreEqual(6, QuadsIn(Origin));
        }

        [Test]
        public void BarOfSameBlock_MergesIntoSixQuads()
        {
            VoxelChunk chunk = _world.CreateChunk(Origin);
            chunk.SetVoxel(new int3(5, 5, 5), Stone);
            chunk.SetVoxel(new int3(5, 5, 6), Stone);
            Assert.AreEqual(6, QuadsIn(Origin));
        }

        [Test]
        public void DifferentBlocksSideBySide_DoNotMerge()
        {
            VoxelChunk chunk = _world.CreateChunk(Origin);
            chunk.SetVoxel(new int3(5, 5, 5), Stone);
            chunk.SetVoxel(new int3(5, 5, 6), new Voxel(BlockIds.Dirt));
            // The hidden face between them is culled; the other sides can't merge across block types.
            Assert.AreEqual(10, QuadsIn(Origin));
        }

        [Test]
        public void FullChunkWithAirAround_IsSixBigQuads()
        {
            _world.CreateChunk(Origin, Stone);
            Mesh mesh = MeshOf(Origin);
            Assert.AreEqual(6 * 4, mesh.vertexCount);
            foreach (Vector2 uv in mesh.uv)
                Assert.IsTrue(uv.x is 0 or ChunkSize && uv.y is 0 or ChunkSize, $"UV {uv} should be a corner of a 32x32 quad.");
        }

        [Test]
        public void ExpandedFullChunk_IsSixBigQuads()
        {
            // Same as above, but stored as an array so the job really runs (no uniform shortcut).
            VoxelChunk chunk = _world.CreateChunk(Origin, Stone);
            chunk.SetVoxel(new int3(0, 0, 0), new Voxel(BlockIds.Dirt));
            chunk.SetVoxel(new int3(0, 0, 0), Stone);
            Assert.IsFalse(chunk.IsUniform);
            Assert.AreEqual(6, QuadsIn(Origin));
        }

        [Test]
        public void FullChunkBuriedInStone_HasNoQuads()
        {
            VoxelChunk center = _world.CreateChunk(Origin, Stone);
            center.SetVoxel(new int3(0, 0, 0), new Voxel(BlockIds.Dirt)); // Force the real job, not the shortcut.
            foreach (int3 offset in new[] { new int3(-1, 0, 0), new int3(1, 0, 0), new int3(0, -1, 0), new int3(0, 1, 0), new int3(0, 0, -1), new int3(0, 0, 1) })
                _world.CreateChunk(new ChunkCoord(offset), Stone);
            Assert.AreEqual(0, QuadsIn(Origin));
        }

        [Test]
        public void AirChunk_HasNoMesh()
        {
            _world.CreateChunk(Origin);
            Assert.AreEqual(0, QuadsIn(Origin));
            Assert.IsFalse(_scheduler.TryGetRenderer(Origin, out _), "An all-air chunk shouldn't even get a GameObject.");
        }

        [Test]
        public void BlocksTouchingAcrossChunkBorder_HideSharedFace()
        {
            var east = new ChunkCoord(1, 0, 0);
            _world.CreateChunk(Origin).SetVoxel(new int3(ChunkMask, 0, 0), Stone);
            _world.CreateChunk(east).SetVoxel(new int3(0, 0, 0), Stone);
            Assert.AreEqual(5, QuadsIn(Origin));
            Assert.AreEqual(5, QuadsIn(east));
        }

        [Test]
        public void LoadingNeighbour_RemeshesBorder()
        {
            _world.CreateChunk(Origin).SetVoxel(new int3(ChunkMask, 0, 0), Stone);
            Assert.AreEqual(6, QuadsIn(Origin));

            _world.CreateChunk(new ChunkCoord(1, 0, 0)).SetVoxel(new int3(0, 0, 0), Stone);
            Assert.AreEqual(5, QuadsIn(Origin), "Loading the neighbour should hide the shared face.");
        }

        [Test]
        public void EditingBorderBlock_RemeshesNeighbour()
        {
            var east = new ChunkCoord(1, 0, 0);
            _world.CreateChunk(Origin).SetVoxel(new int3(ChunkMask, 0, 0), Stone);
            _world.CreateChunk(east);
            Assert.AreEqual(6, QuadsIn(Origin));

            Assert.IsTrue(_world.SetVoxel(new int3(ChunkSize, 0, 0), Stone)); // World x 32 = east chunk, local 0.
            Assert.AreEqual(5, QuadsIn(Origin));
        }

        [Test]
        public void Checkerboard_DrawsEveryFace()
        {
            VoxelChunk chunk = _world.CreateChunk(Origin);
            for (int i = 0; i < ChunkVolume; i++)
            {
                int3 p = VoxelMath.FromIndex(i);
                if (((p.x + p.y + p.z) & 1) == 0)
                    chunk.SetVoxel(i, Stone);
            }

            Mesh mesh = MeshOf(Origin);
            Assert.AreEqual(ChunkVolume / 2 * 6, mesh.vertexCount / 4);
            Assert.AreEqual(UnityEngine.Rendering.IndexFormat.UInt32, mesh.indexFormat, "Over 65k vertices needs 32-bit indices.");
        }

        [Test]
        public void EveryTriangle_FacesOutward()
        {
            VoxelChunk chunk = _world.CreateChunk(Origin);
            chunk.SetVoxel(new int3(3, 4, 5), Stone);
            chunk.SetVoxel(new int3(3, 5, 5), Stone);
            chunk.SetVoxel(new int3(4, 4, 5), new Voxel(BlockIds.Grass));

            Mesh mesh = MeshOf(Origin);
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            int[] triangles = mesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                Vector3 front = Vector3.Cross(b - a, c - a).normalized; // Unity's front side for clockwise triangles.
                Assert.That(Vector3.Dot(front, normals[triangles[i]]), Is.GreaterThan(0.99f),
                    $"Triangle {i / 3} faces {front} but its normal is {normals[triangles[i]]}.");
            }
        }

        [Test]
        public void MergedQuadUVs_CountBlocks()
        {
            VoxelChunk chunk = _world.CreateChunk(Origin);
            for (int x = 0; x < 5; x++)
            for (int z = 0; z < 3; z++)
                chunk.SetVoxel(new int3(x, 0, z), Stone); // A 5x3 floor, one block thick.

            Mesh mesh = MeshOf(Origin);
            Vector3[] normals = mesh.normals;
            Vector2[] uvs = mesh.uv;
            var maxUv = Vector2.zero;
            for (int i = 0; i < normals.Length; i++)
            {
                if (normals[i] == Vector3.up)
                    maxUv = Vector2.Max(maxUv, uvs[i]);
            }
            Assert.AreEqual(new Vector2(5, 3), maxUv, "The top quad's UVs should span 5 x 3 blocks.");
        }

        [Test]
        public void Faces_CarryTheirTextureLayer_ByFacing()
        {
            _world.Dispose();
            var blocks = new NativeArray<BlockInfo>(3, Allocator.Persistent);
            blocks[BlockIds.Air] = new BlockInfo { Id = BlockIds.Air, Flags = BlockFlags.Registered | BlockFlags.Transparent };
            BlockInfo grass = Solid(BlockIds.Grass);
            grass.TopLayer = 4;
            grass.SideLayer = 5;
            grass.BottomLayer = 6;
            blocks[BlockIds.Grass] = grass;
            _scheduler.Dispose();
            _world = new VoxelWorld(blocks);
            _scheduler = new ChunkMeshScheduler(_world, _parent.transform, null);

            _world.CreateChunk(Origin).SetVoxel(new int3(5, 5, 5), new Voxel(BlockIds.Grass));

            using Mesh.MeshDataArray data = Mesh.AcquireReadOnlyMeshData(MeshOf(Origin));
            NativeArray<ChunkVertex> vertices = data[0].GetVertexData<ChunkVertex>();
            Assert.AreEqual(24, vertices.Length);
            foreach (ChunkVertex vertex in vertices)
            {
                float expected = vertex.Normal.y > 0.5f ? 4f : vertex.Normal.y < -0.5f ? 6f : 5f;
                Assert.AreEqual(expected, vertex.TextureLayer, $"Face with normal {vertex.Normal} has the wrong texture layer.");
            }
        }

        [Test]
        public void FaceWithoutTexture_HasLayerMinusOne()
        {
            BlockInfo info = Solid(BlockIds.Stone);
            info.TopLayer = info.SideLayer = info.BottomLayer = BlockInfo.NoTexture;
            Assert.AreEqual(BlockInfo.NoTexture, info.LayerFor(BlockFacing.North));

            _world.Dispose();
            var blocks = new NativeArray<BlockInfo>(BlockIds.Stone + 1, Allocator.Persistent);
            blocks[BlockIds.Air] = new BlockInfo { Id = BlockIds.Air, Flags = BlockFlags.Registered | BlockFlags.Transparent };
            blocks[BlockIds.Stone] = info;
            _scheduler.Dispose();
            _world = new VoxelWorld(blocks);
            _scheduler = new ChunkMeshScheduler(_world, _parent.transform, null);

            _world.CreateChunk(Origin).SetVoxel(new int3(1, 1, 1), Stone);

            using Mesh.MeshDataArray data = Mesh.AcquireReadOnlyMeshData(MeshOf(Origin));
            foreach (ChunkVertex vertex in data[0].GetVertexData<ChunkVertex>())
                Assert.AreEqual(-1f, vertex.TextureLayer);
        }

        [Test]
        public void ChunkEditedWhileMeshing_DiscardsStaleResult()
        {
            VoxelChunk chunk = _world.CreateChunk(Origin);
            chunk.SetVoxel(new int3(1, 1, 1), Stone);

            _scheduler.Update();                 // Schedules the job.
            Assert.AreEqual(1, _scheduler.PendingCount);
            _scheduler.CompleteGatherJobs();     // Required before any edit.
            _world.SetVoxel(new int3(10, 10, 10), Stone);
            _scheduler.CompletePending();

            Assert.AreEqual(1, _scheduler.DiscardedCount);
            Assert.AreEqual(1, _world.DirtyCount, "The chunk should be queued again.");
            Assert.AreEqual(12, QuadsIn(Origin), "The next mesh should include both blocks.");
        }

        [Test]
        public void MeshingSpeed_IsLogged()
        {
            // Mesh one busy chunk directly (no scheduler) to time the Burst-compiled work.
            var padded = new NativeArray<ushort>(ChunkGatherJob.PaddedVolume, Allocator.Persistent);
            var noBiomes = new NativeArray<BiomeInfo>(0, Allocator.Persistent); // No biome tint.
            var random = new Unity.Mathematics.Random(1234);
            for (int y = 1; y <= ChunkSize; y++)
            for (int z = 1; z <= ChunkSize; z++)
            for (int x = 1; x <= ChunkSize; x++)
            {
                if (y < 20 && random.NextFloat() > 0.1f)
                    padded[ChunkGatherJob.PaddedIndex(x, y, z)] = (ushort)random.NextInt(BlockIds.Grass, BlockIds.ManaCrystalOre + 1);
            }

            const int runs = 20;
            var stopwatch = new Stopwatch();
            try
            {
                for (int run = -1; run < runs; run++) // Run -1 warms up Burst compilation.
                {
                    if (run == 0)
                        stopwatch.Start();
                    Mesh.MeshDataArray data = Mesh.AllocateWritableMeshData(1);
                    new GreedyMeshJob { Padded = padded, Blocks = _world.Blocks, MeshData = data[0], Biomes = noBiomes }.Run();
                    data.Dispose();
                }
                stopwatch.Stop();
                Debug.Log($"[Voxelforge] Greedy mesh of a busy chunk: {stopwatch.Elapsed.TotalMilliseconds / runs:F3} ms average over {runs} runs.");
            }
            finally
            {
                padded.Dispose();
                noBiomes.Dispose();
            }
        }
    }
}
