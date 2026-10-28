using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Voxelforge.Voxels;
using static Voxelforge.Voxels.ChunkGatherJob;
using static Voxelforge.Voxels.VoxelConstants;

namespace Voxelforge.Tests
{
    /// <summary>
    /// Roadmap 2.6 corner shading: face corners darken next to blocks, quads only merge when their shading matches,
    /// quads split through their dark corner, and the gather job fills the padding's edges and corners from the
    /// diagonal chunks so shading is right across chunk seams.
    /// </summary>
    public class AmbientOcclusionTests
    {
        const byte Open = 255, OneSide = 170, Tucked = 0;

        NativeArray<BlockInfo> _blocks;
        NativeArray<ushort> _padded;
        NativeList<ChunkVertex> _vertices;

        [SetUp]
        public void SetUp()
        {
            _blocks = new NativeArray<BlockInfo>(BlockIds.Stone + 1, Allocator.Persistent);
            _blocks[BlockIds.Air] = new BlockInfo { Flags = BlockFlags.Registered | BlockFlags.Transparent };
            _blocks[BlockIds.Stone] = new BlockInfo { Id = BlockIds.Stone, Flags = BlockFlags.Registered | BlockFlags.Solid };
            _padded = new NativeArray<ushort>(PaddedVolume, Allocator.Persistent);
            _vertices = new NativeList<ChunkVertex>(Allocator.Persistent);
        }

        [TearDown]
        public void TearDown()
        {
            _blocks.Dispose();
            _padded.Dispose();
            _vertices.Dispose();
        }

        /// <summary>Puts stone at a chunk-local position (the padding is -1 and 32).</summary>
        void Stone(int x, int y, int z) => _padded[PaddedIndex(x + 1, y + 1, z + 1)] = BlockIds.Stone;

        void Floor(int size)
        {
            for (int z = 0; z < size; z++)
            for (int x = 0; x < size; x++)
                Stone(x, 0, z);
        }

        void Mesh()
        {
            _vertices.Clear();
            GreedyMesher.Build(_padded, _blocks, _vertices);
        }

        int QuadsFacing(float3 normal)
        {
            int count = 0;
            for (int i = 0; i < _vertices.Length; i += 4)
            {
                if (_vertices[i].Normal.Equals(normal))
                    count++;
            }
            return count;
        }

        /// <summary>The corner shading of every upward-facing vertex at <paramref name="position"/>.</summary>
        byte[] TopShadingAt(float3 position)
        {
            var found = new System.Collections.Generic.List<byte>();
            foreach (ChunkVertex vertex in _vertices)
            {
                if (vertex.Normal.Equals(new float3(0, 1, 0)) && vertex.Position.Equals(position))
                    found.Add(vertex.Color.a);
            }
            return found.ToArray();
        }

        [Test]
        public void OpenFloor_IsUnshaded_AndStaysOneQuad()
        {
            Floor(5);
            Mesh();

            Assert.AreEqual(1, QuadsFacing(new float3(0, 1, 0)));
            foreach (ChunkVertex vertex in _vertices)
                Assert.AreEqual(Open, vertex.Color.a);
        }

        [Test]
        public void BlockOnAFloor_ShadesTheFloorAroundIt_AndSplitsItsTop()
        {
            Floor(5);
            Stone(2, 1, 2);
            Mesh();

            Assert.Greater(QuadsFacing(new float3(0, 1, 0)), 2, "Shaded faces can't merge with open ones.");
            // The floor block diagonal to the stone, (1, 0, 1), only touches it with one corner.
            CollectionAssert.Contains(TopShadingAt(new float3(2, 1, 2)), OneSide);
            // Floor corners away from the stone stay open.
            CollectionAssert.AreEqual(new[] { Open }, TopShadingAt(new float3(0, 1, 0)));
            // The stone's own top is open.
            foreach (byte shade in TopShadingAt(new float3(2, 2, 2)))
                Assert.AreEqual(Open, shade);
        }

        [Test]
        public void CornerBetweenTwoWalls_IsFullyTuckedIn()
        {
            Floor(4);
            for (int i = 0; i < 4; i++)
            {
                Stone(0, 1, i); // Wall along x = 0.
                Stone(i, 1, 0); // Wall along z = 0.
            }
            Mesh();

            // The floor block at (1, 0, 1) has walls on two sides of its corner at (1, 1, 1).
            CollectionAssert.Contains(TopShadingAt(new float3(1, 1, 1)), Tucked);
        }

        [Test]
        public void QuadWithOneDarkCorner_IsSplitThroughThatCorner()
        {
            // The floor faces diagonal to a single block each have one shaded corner, in different positions.
            Floor(5);
            Stone(2, 1, 2);
            Mesh();

            bool checkedOne = false;
            for (int quad = 0; quad < _vertices.Length; quad += 4)
            {
                int dark = -1, darkCount = 0;
                for (int k = 0; k < 4; k++)
                {
                    if (_vertices[quad + k].Color.a < Open)
                    {
                        dark = k;
                        darkCount++;
                    }
                }
                if (darkCount != 1)
                    continue;
                // Triangles are (0, 1, 2) and (0, 2, 3): the split runs from vertex 0 to vertex 2.
                Assert.That(dark == 0 || dark == 2, $"Quad at vertex {quad} splits away from its dark corner ({dark}).");
                checkedOne = true;
            }
            Assert.IsTrue(checkedOne, "Expected at least one quad with exactly one shaded corner.");
        }

        [Test]
        public void Gather_FillsEdgesAndCorners_FromDiagonalChunks()
        {
            var edge = new NativeArray<Voxel>(ChunkVolume, Allocator.Persistent);
            edge[VoxelMath.ToIndex(0, 0, 5)] = new Voxel(BlockIds.Stone); // Chunk (+1, +1, 0): its bottom-west edge.
            var sources = new NativeArray<GatherSource>(SourceCount, Allocator.Persistent);
            try
            {
                for (int i = 0; i < SourceCount; i++)
                    sources[i] = GatherSource.Uniform(BlockIds.Air);
                sources[SourceIndex(new int3(1, 1, 1))] = GatherSource.Uniform(BlockIds.Stone);
                sources[SourceIndex(new int3(1, 1, 0))] = GatherSource.Of(edge.AsReadOnly());

                new ChunkGatherJob { Sources = sources, Padded = _padded }.Run();

                Assert.AreEqual(BlockIds.Stone, _padded[PaddedIndex(PaddedSize - 1, PaddedSize - 1, PaddedSize - 1)], "Corner");
                Assert.AreEqual(BlockIds.Stone, _padded[PaddedIndex(PaddedSize - 1, PaddedSize - 1, 6)], "Edge block");
                Assert.AreEqual(BlockIds.Air, _padded[PaddedIndex(PaddedSize - 1, PaddedSize - 1, 7)], "Rest of the edge");
                Assert.AreEqual(BlockIds.Air, _padded[PaddedIndex(0, 0, 0)], "Opposite corner");
            }
            finally
            {
                edge.Dispose();
                sources.Dispose();
            }
        }

        [Test]
        public void SourceIndex_AndSourceOffset_RoundTrip()
        {
            for (int i = 0; i < SourceCount; i++)
                Assert.AreEqual(i, SourceIndex(SourceOffset(i)));
            Assert.AreEqual(CenterSource, SourceIndex(int3.zero));
        }

        [Test]
        public void EditingAChunkCorner_RemeshesAllSevenChunksAroundIt()
        {
            var world = new VoxelWorld(new NativeArray<BlockInfo>(_blocks, Allocator.Persistent));
            try
            {
                for (int y = 0; y < 2; y++)
                for (int z = 0; z < 2; z++)
                for (int x = 0; x < 2; x++)
                    world.CreateChunk(new ChunkCoord(x, y, z));
                while (world.TryTakeDirty(out _)) { }

                Assert.IsTrue(world.SetVoxel(new int3(ChunkMask, ChunkMask, ChunkMask), new Voxel(BlockIds.Stone)));

                Assert.AreEqual(8, world.DirtyCount, "The chunk and its 7 neighbours sharing that corner.");
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void EditingTheMiddleOfAChunk_RemeshesOnlyThatChunk()
        {
            var world = new VoxelWorld(new NativeArray<BlockInfo>(_blocks, Allocator.Persistent));
            try
            {
                world.CreateChunk(new ChunkCoord(0, 0, 0));
                world.CreateChunk(new ChunkCoord(1, 0, 0));
                while (world.TryTakeDirty(out _)) { }

                Assert.IsTrue(world.SetVoxel(new int3(5, 5, 5), new Voxel(BlockIds.Stone)));

                Assert.AreEqual(1, world.DirtyCount);
            }
            finally
            {
                world.Dispose();
            }
        }
    }
}
