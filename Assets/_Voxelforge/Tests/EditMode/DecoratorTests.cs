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

        [Test]
        public void Torch_OnAFloor_StandsUp_AndAgainstAWall_HangsFacingAway()
        {
            BlockDefinition torch = BlockDefinition.Create(BlockIds.Torch, "Torch", isSolid: false, isTransparent: true,
                isProp: true, needsSupport: true);
            DecorationDefinition floor = DecorationDefinition.CreateProp(torch, PropAttach.Floor);
            DecorationDefinition wall = DecorationDefinition.CreateProp(torch, PropAttach.Wall);
            try
            {
                _padded[PaddedIndex(5, 5, 5)] = BlockIds.Stone; // Floor under the first torch.
                _padded[PaddedIndex(5, 6, 5)] = BlockIds.Torch;
                _padded[PaddedIndex(11, 6, 10)] = BlockIds.Stone; // A wall east (+X) of the second torch, nothing below.
                _padded[PaddedIndex(10, 6, 10)] = BlockIds.Torch;

                using NativeList<DecorationInstance> instances = Decorate(TestSeed, floor, wall);

                Assert.AreEqual(2, instances.Length, "Stone blocks have no ground plants here, so just the two torches.");
                DecorationInstance standing = Find(instances, new float3(4.5f, 5f, 4.5f));
                DecorationInstance hanging = Find(instances, new float3(9.5f, 5f, 9.5f));
                Assert.AreEqual(0u, standing.Type, "The floor model.");
                Assert.AreEqual(1u, hanging.Type, "The wall model.");
                Assert.AreEqual(0.5f * math.PI, hanging.Yaw, 1e-5f, "The mesh's +Z side (its wall side) turned to face east.");
            }
            finally
            {
                Object.DestroyImmediate(torch);
                Object.DestroyImmediate(floor);
                Object.DestroyImmediate(wall);
            }
        }

        [Test]
        public void Workbench_GetsItsModel_AndNoGrassGrowsUnderIt()
        {
            BlockDefinition bench = BlockDefinition.Create(BlockIds.Workbench, "Workbench", isTransparent: true, isProp: true);
            DecorationDefinition model = DecorationDefinition.CreateProp(bench, PropAttach.Any);
            try
            {
                FillLayer(10, BlockIds.Grass);
                _padded[PaddedIndex(3, 12, 3)] = BlockIds.Workbench;

                using NativeList<DecorationInstance> instances = Decorate(TestSeed, _tuft, model);

                DecorationInstance placed = Find(instances, new float3(2.5f, 11f, 2.5f));
                Assert.AreEqual(1u, placed.Type);
                Assert.AreEqual(0f, math.fmod(placed.Yaw, 0.5f * math.PI), 1e-5f, "Turned to a whole quarter.");
                Assert.AreEqual(32 * 32 - 1 + 1, instances.Length, "Every grass top but the one under the bench, plus the bench.");
            }
            finally
            {
                Object.DestroyImmediate(bench);
                Object.DestroyImmediate(model);
            }
        }

        static DecorationInstance Find(NativeList<DecorationInstance> instances, float3 position)
        {
            foreach (DecorationInstance instance in instances)
            {
                if (math.distance(instance.Position, position) < 1e-4f)
                    return instance;
            }
            Assert.Fail($"No decoration at {position}.");
            return default;
        }

        /// <summary>Block lookups for the IDs these tests use: Air, Grass and Stone, a trunk, a torch and a workbench.</summary>
        static NativeArray<BlockInfo> Blocks()
        {
            var blocks = new NativeArray<BlockInfo>(BlockIds.Workbench + 1, Allocator.Temp);
            blocks[BlockIds.Air] = new BlockInfo { Flags = BlockFlags.Registered | BlockFlags.Transparent };
            blocks[BlockIds.Grass] = new BlockInfo { Flags = BlockFlags.Registered | BlockFlags.Solid };
            blocks[BlockIds.Stone] = new BlockInfo { Flags = BlockFlags.Registered | BlockFlags.Solid };
            blocks[BlockIds.TreeTrunk] = new BlockInfo { Flags = BlockFlags.Registered | BlockFlags.Solid | BlockFlags.Transparent };
            blocks[BlockIds.Torch] = new BlockInfo
                { Flags = BlockFlags.Registered | BlockFlags.Transparent | BlockFlags.Prop | BlockFlags.NeedsSupport };
            blocks[BlockIds.Workbench] = new BlockInfo
                { Flags = BlockFlags.Registered | BlockFlags.Solid | BlockFlags.Transparent | BlockFlags.Prop };
            return blocks;
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
            using NativeArray<BlockInfo> blocks = Blocks();
            Decorator.Build(_padded, blocks, new int3(0, 0, 0), seed, sampler, _terrain, infos, weights, output);
            infos.Dispose();
            weights.Dispose();
            return output;
        }
    }
}
