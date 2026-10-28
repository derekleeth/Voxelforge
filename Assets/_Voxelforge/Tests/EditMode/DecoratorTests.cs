using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Voxelforge.Voxels;
using static Voxelforge.Voxels.ChunkGatherJob;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 2.6: decorations grow on open block tops, trees sit on their trunks, all from the seed.</summary>
    public class DecoratorTests
    {
        const string Biome = "Meadows";
        static readonly WorldSeed TestSeed = new WorldSeed(1234);

        BlockDefinition _grass;
        DecorationDefinition _tuft;
        DecorationDefinition _tree;
        NativeArray<BiomeInfo> _biomes;
        TerrainParams _terrain;
        NativeArray<ushort> _padded;

        [SetUp]
        public void SetUp()
        {
            _grass = BlockDefinition.Create(BlockIds.Grass, "Grass");
            _tuft = DecorationDefinition.Create(DecorationKind.Ground, 1f, new[] { _grass },
                new[] { new BiomeWeight { biome = Biome, weight = 1f } }, 0.8f, 1.2f);
            _tree = DecorationDefinition.Create(DecorationKind.Tree, 0f, new BlockDefinition[0],
                new[] { new BiomeWeight { biome = Biome, weight = 1f } });

            _biomes = new NativeArray<BiomeInfo>(1, Allocator.Persistent);
            _biomes[0] = new BiomeInfo { SurfaceBlock = BlockIds.Grass, Octaves = 1, TreeChance = 1f };
            _terrain = new TerrainParams
            {
                ClimateFrequency = 0.004f, BiomeBlend = 0.25f,
                TreeTrunkBlock = BlockIds.TreeTrunk, TreeCellSize = 6, TreeMinHeight = 5, TreeMaxHeight = 5,
            };
            _padded = new NativeArray<ushort>(PaddedVolume, Allocator.Persistent);
        }

        [TearDown]
        public void TearDown()
        {
            _biomes.Dispose();
            _padded.Dispose();
            Object.DestroyImmediate(_grass);
            Object.DestroyImmediate(_tuft);
            Object.DestroyImmediate(_tree);
        }

        [Test]
        public void FullDensity_PutsOneTuft_OnEveryOpenGrassTop()
        {
            FillLayer(10, BlockIds.Grass);

            using NativeList<DecorationInstance> instances = Decorate(TestSeed, _tuft);

            Assert.AreEqual(32 * 32, instances.Length);
            foreach (DecorationInstance instance in instances)
            {
                Assert.AreEqual(11f, instance.Position.y, "Tufts stand on top of the block.");
                Assert.That(instance.Scale, Is.InRange(0.8f, 1.2f));
                Assert.AreEqual(0u, instance.Type);
            }
        }

        [Test]
        public void CoveredGrass_AndOtherBlocks_GrowNothing()
        {
            FillLayer(10, BlockIds.Grass);
            FillLayer(11, BlockIds.Stone); // Buries the grass; stone isn't a surface for the tuft.

            using NativeList<DecorationInstance> instances = Decorate(TestSeed, _tuft);

            Assert.AreEqual(0, instances.Length);
        }

        [Test]
        public void GrassOnTheChunkTop_UsesTheNeighbourAbove()
        {
            FillLayer(31, BlockIds.Grass);
            for (int z = 1; z <= 32; z++)
            for (int x = 1; x <= 32; x++)
                _padded[PaddedIndex(x, PaddedSize - 1, z)] = BlockIds.Stone; // The chunk above starts with stone.

            using NativeList<DecorationInstance> instances = Decorate(TestSeed, _tuft);

            Assert.AreEqual(0, instances.Length);
        }

        [Test]
        public void SameSeed_SamePlants_OtherSeed_OtherPlants()
        {
            FillLayer(10, BlockIds.Grass);
            SetDensity(_tuft, 0.5f);

            using NativeList<DecorationInstance> a = Decorate(TestSeed, _tuft);
            using NativeList<DecorationInstance> b = Decorate(TestSeed, _tuft);
            using NativeList<DecorationInstance> c = Decorate(new WorldSeed(TestSeed.Value + 1), _tuft);

            Assert.AreEqual(a.Length, b.Length);
            for (int i = 0; i < a.Length; i++)
                Assert.AreEqual(a[i].Position, b[i].Position);
            Assert.That(a.Length, Is.InRange(420, 600), "About half the blocks should grow a tuft.");
            Assert.IsFalse(a.Length == c.Length && a[0].Position.Equals(c[0].Position), "Another seed should place other plants.");
        }

        [Test]
        public void Trunk_GetsOneTree_AtItsBottom_AndNoTuftUnderIt()
        {
            FillLayer(10, BlockIds.Grass);
            for (int y = 11; y <= 15; y++)
                _padded[PaddedIndex(6, y + 1, 6)] = BlockIds.TreeTrunk;

            using NativeList<DecorationInstance> instances = Decorate(TestSeed, _tuft, _tree);

            int trees = 0;
            foreach (DecorationInstance instance in instances)
            {
                if (instance.Type != 1)
                    continue;
                trees++;
                Assert.AreEqual(new float3(5.5f, 11f, 5.5f), instance.Position);
                Assert.AreEqual(1f, instance.Scale, 1e-5f, "A 5-block trunk is the size the tree was modelled for.");
            }
            Assert.AreEqual(1, trees);
            Assert.AreEqual(32 * 32 - 1 + 1, instances.Length, "Every grass top but the one under the trunk, plus the tree.");
        }

        void FillLayer(int y, ushort block)
        {
            for (int z = 1; z <= 32; z++)
            for (int x = 1; x <= 32; x++)
                _padded[PaddedIndex(x, y + 1, z)] = block;
        }

        static void SetDensity(DecorationDefinition definition, float density)
        {
            var serialized = new UnityEditor.SerializedObject(definition);
            serialized.FindProperty("density").floatValue = density;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        NativeList<DecorationInstance> Decorate(WorldSeed seed, params DecorationDefinition[] definitions)
        {
            var infos = new NativeArray<DecorationInfo>(definitions.Length, Allocator.Temp);
            var weights = new NativeArray<float>(definitions.Length, Allocator.Temp);
            for (int i = 0; i < definitions.Length; i++)
            {
                infos[i] = definitions[i].ToDecorationInfo();
                weights[i] = definitions[i].WeightIn(Biome);
            }

            var output = new NativeList<DecorationInstance>(Allocator.Persistent);
            using var sampler = new TerrainSampler(seed, _terrain, _biomes, Allocator.Temp);
            Decorator.Build(_padded, new int3(0, 0, 0), seed, sampler, _terrain, infos, weights, output);
            infos.Dispose();
            weights.Dispose();
            return output;
        }
    }
}
