using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using Voxelforge.Voxels;
using static Voxelforge.Voxels.ChunkGatherJob;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 2.6 biome tint: grass faces carry their biome's colour, other blocks stay white.</summary>
    public class BiomeTintTests
    {
        static readonly float3 GrassColour = new float3(0.5f, 0.8f, 0.25f);
        static readonly float3 LeafColour = new float3(0.9f, 0.6f, 0.3f);

        NativeArray<BiomeInfo> _biomes;
        TerrainParams _terrain;

        [SetUp]
        public void SetUp()
        {
            _biomes = new NativeArray<BiomeInfo>(1, Allocator.Persistent);
            _biomes[0] = new BiomeInfo { Octaves = 1, GrassTint = GrassColour, FoliageTint = LeafColour };
            _terrain = new TerrainParams { ClimateFrequency = 0.004f, BiomeBlend = 0.25f };
        }

        [TearDown]
        public void TearDown() => _biomes.Dispose();

        TerrainSampler Sampler() => new TerrainSampler(new WorldSeed(42), _terrain, _biomes, Allocator.Persistent);

        [Test]
        public void TintAt_ReturnsTheBiomeColours_AndWhiteForNone()
        {
            TerrainSampler sampler = Sampler();
            try
            {
                Assert.That(math.distance(GrassColour, sampler.TintAt(new float2(10, 20), BiomeTint.Grass)), Is.LessThan(1e-5f));
                Assert.That(math.distance(LeafColour, sampler.TintAt(new float2(10, 20), BiomeTint.Foliage)), Is.LessThan(1e-5f));
                Assert.AreEqual(new float3(1f), sampler.TintAt(new float2(10, 20), BiomeTint.None));
            }
            finally
            {
                sampler.Dispose();
            }
        }

        [Test]
        public void GrassFaces_CarryTheTint_StoneStaysWhite()
        {
            var blocks = new NativeArray<BlockInfo>(BlockIds.Stone + 1, Allocator.Persistent);
            blocks[BlockIds.Air] = new BlockInfo { Flags = BlockFlags.Registered | BlockFlags.Transparent };
            blocks[BlockIds.Grass] = Textured(BlockIds.Grass, BiomeTint.Grass);
            blocks[BlockIds.Stone] = Textured(BlockIds.Stone, BiomeTint.None);
            var padded = new NativeArray<ushort>(PaddedVolume, Allocator.Persistent);
            padded[PaddedIndex(3, 1, 3)] = BlockIds.Grass;
            padded[PaddedIndex(10, 1, 10)] = BlockIds.Stone;
            var vertices = new NativeList<ChunkVertex>(Allocator.Persistent);
            TerrainSampler sampler = Sampler();
            var tints = new ChunkTints(sampler, new int3(64, 0, -32), Allocator.Persistent);
            try
            {
                GreedyMesher.Build(padded, blocks, vertices, ref tints);

                Assert.AreEqual(12 * 4, vertices.Length);
                UnityEngine.Color32 expected = BiomeTints.ToColor32(GrassColour, 255);
                foreach (ChunkVertex vertex in vertices)
                {
                    bool isGrass = vertex.Position.x < 5f;
                    Assert.AreEqual(isGrass ? expected.r : (byte)255, vertex.Color.r);
                    Assert.AreEqual(isGrass ? expected.g : (byte)255, vertex.Color.g);
                    Assert.AreEqual(isGrass ? expected.b : (byte)255, vertex.Color.b);
                    Assert.AreEqual((byte)255, vertex.Color.a, "Lone blocks have open corners.");
                }
            }
            finally
            {
                tints.Dispose();
                sampler.Dispose();
                vertices.Dispose();
                padded.Dispose();
                blocks.Dispose();
            }
        }

        static BlockInfo Textured(ushort id, BiomeTint tint) => new BlockInfo
        {
            Id = id,
            Flags = BlockFlags.Registered | BlockFlags.Solid,
            Tint = tint,
            TopLayer = 0,
            SideLayer = 0,
            BottomLayer = 0,
        };
    }
}
