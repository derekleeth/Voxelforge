using System.Collections.Generic;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using Voxelforge.Gameplay;
using Voxelforge.Voxels;
using static Voxelforge.Voxels.ChunkGatherJob;

namespace Voxelforge.Tests
{
    /// <summary>
    /// Roadmap 5.7 step C: herbs grow on seed-picked spots in the foliage layer, the host's check agrees with what's
    /// drawn, picked herbs hide and stay hidden through a remesh, and the project's herbs, items and nodes line up.
    /// </summary>
    public class HerbTests
    {
        const string Biome = "Meadows";
        const string TerrainAssetPath = "Assets/_Voxelforge/Data/Terrain/TerrainSettings.asset";
        const string RegistryAssetPath = "Assets/_Voxelforge/Data/Blocks/BlockRegistry.asset";
        const string DecorationSetPath = "Assets/_Voxelforge/Data/Decorations/DecorationSet.asset";
        const string ItemRegistryPath = "Assets/_Voxelforge/Data/Items/ItemRegistry.asset";
        const string ProfessionBookPath = "Assets/_Voxelforge/Data/Professions/ProfessionBook.asset";
        static readonly WorldSeed TestSeed = new WorldSeed(20261027);

        // ---- Spots ----

        [Test]
        public void EverySquare_HasExactlyOneSpot_NeverOnItsLastRowOrColumn()
        {
            uint hash = HerbPlacement.SeedHash(TestSeed);
            const int size = HerbPlacement.CellSize;
            for (int cz = -3; cz < 3; cz++)
            for (int cx = -3; cx < 3; cx++)
            {
                int spots = 0;
                for (int z = 0; z < size; z++)
                for (int x = 0; x < size; x++)
                {
                    if (!HerbPlacement.IsSpot(hash, cx * size + x, cz * size + z))
                        continue;
                    spots++;
                    Assert.Less(x, size - 1, "Never on a square's last column, so herbs never touch.");
                    Assert.Less(z, size - 1);
                }
                Assert.AreEqual(1, spots, $"Square ({cx}, {cz}).");
            }
        }

        [Test]
        public void Choose_NeedsTheSurface_TheBiomeAndTheRoll()
        {
            BlockDefinition grass = BlockDefinition.Create(BlockIds.Grass, "Grass");
            DecorationDefinition sure = DecorationDefinition.CreateHerb(ItemIds.Sunpetal, 1f, new[] { grass },
                new[] { new BiomeWeight { biome = Biome, weight = 1f } });
            DecorationDefinition never = DecorationDefinition.CreateHerb(ItemIds.Frostbloom, 0f, new[] { grass },
                new[] { new BiomeWeight { biome = Biome, weight = 1f } });
            try
            {
                using var infos = new NativeArray<DecorationInfo>(new[] { never.ToDecorationInfo(), sure.ToDecorationInfo() }, Allocator.Temp);
                using var weights = new NativeArray<float>(new[] { 1f, 1f }, Allocator.Temp);
                using var noBiome = new NativeArray<float>(new[] { 0f, 0f }, Allocator.Temp);
                uint hash = HerbPlacement.SeedHash(TestSeed);
                Assert.AreEqual(1, HerbPlacement.Choose(hash, 3, 4, BlockIds.Grass, 0, 1, infos, weights), "Density 1 always grows.");
                Assert.AreEqual(-1, HerbPlacement.Choose(hash, 3, 4, BlockIds.Stone, 0, 1, infos, weights), "Not on stone.");
                Assert.AreEqual(-1, HerbPlacement.Choose(hash, 3, 4, BlockIds.Grass, 0, 1, infos, noBiome), "Not in a biome it doesn't list.");
                Assert.AreEqual(ItemIds.Sunpetal, infos[1].HerbItem);
            }
            finally
            {
                Object.DestroyImmediate(grass);
                Object.DestroyImmediate(sure);
                Object.DestroyImmediate(never);
            }
        }

        [Test]
        public void AboutTheDensityOfSpots_GrowAHerb()
        {
            BlockDefinition grass = BlockDefinition.Create(BlockIds.Grass, "Grass");
            DecorationDefinition herb = DecorationDefinition.CreateHerb(ItemIds.Sunpetal, 0.35f, new[] { grass },
                new[] { new BiomeWeight { biome = Biome, weight = 1f } });
            try
            {
                using var infos = new NativeArray<DecorationInfo>(new[] { herb.ToDecorationInfo() }, Allocator.Temp);
                using var weights = new NativeArray<float>(new[] { 1f }, Allocator.Temp);
                uint hash = HerbPlacement.SeedHash(TestSeed);
                int spots = 0, grown = 0;
                for (int z = -600; z < 600; z++)
                for (int x = -600; x < 600; x++)
                {
                    if (!HerbPlacement.IsSpot(hash, x, z))
                        continue;
                    spots++;
                    grown += HerbPlacement.Choose(hash, x, z, BlockIds.Grass, 0, 1, infos, weights) >= 0 ? 1 : 0;
                }
                Assert.AreEqual(10000, spots);
                Assert.AreEqual(0.35f, grown / (float)spots, 0.02f, "Neighbouring spots mustn't roll alike.");
            }
            finally
            {
                Object.DestroyImmediate(grass);
                Object.DestroyImmediate(herb);
            }
        }

        [Test]
        public void Decorator_PutsAHerbOnEverySpot_InsteadOfGrass()
        {
            BlockDefinition grass = BlockDefinition.Create(BlockIds.Grass, "Grass");
            var meadow = new[] { new BiomeWeight { biome = Biome, weight = 1f } };
            DecorationDefinition tuft = DecorationDefinition.Create(DecorationKind.Ground, 1f, new[] { grass }, meadow);
            DecorationDefinition herb = DecorationDefinition.CreateHerb(ItemIds.Sunpetal, 1f, new[] { grass }, meadow);
            var biomes = new NativeArray<BiomeInfo>(1, Allocator.Temp);
            biomes[0] = new BiomeInfo { SurfaceBlock = BlockIds.Grass, Octaves = 1 };
            var terrain = new TerrainParams { ClimateFrequency = 0.004f, BiomeBlend = 0.25f, TreeTrunkBlock = BlockIds.TreeTrunk, TreeCellSize = 6 };
            var padded = new NativeArray<ushort>(PaddedVolume, Allocator.Temp);
            for (int z = 1; z <= 32; z++)
            for (int x = 1; x <= 32; x++)
                padded[PaddedIndex(x, 11, z)] = BlockIds.Grass; // World y 10.
            try
            {
                using var infos = new NativeArray<DecorationInfo>(new[] { tuft.ToDecorationInfo(), herb.ToDecorationInfo() }, Allocator.Temp);
                using var weights = new NativeArray<float>(new[] { 1f, 1f }, Allocator.Temp);
                using var output = new NativeList<DecorationInstance>(Allocator.Temp);
                using var blocks = new NativeArray<BlockInfo>(BlockIds.Grass + 1, Allocator.Temp);
                using var sampler = new TerrainSampler(TestSeed, terrain, biomes, Allocator.Temp);
                Decorator.Build(padded, blocks, int3.zero, TestSeed, sampler, terrain, infos, weights, output);

                uint hash = HerbPlacement.SeedHash(TestSeed);
                int spots = 0, herbs = 0;
                for (int z = 0; z < 32; z++)
                for (int x = 0; x < 32; x++)
                    spots += HerbPlacement.IsSpot(hash, x, z) ? 1 : 0;
                foreach (DecorationInstance instance in output)
                {
                    if (instance.Type != 1)
                        continue;
                    herbs++;
                    int3 cell = HerbPlacement.CellOf(instance.Position);
                    Assert.AreEqual(11, cell.y, "In the open block over the grass.");
                    Assert.IsTrue(HerbPlacement.IsSpot(hash, cell.x, cell.z), $"Herb off its spot at {cell}.");
                }
                Assert.Greater(spots, 0);
                Assert.AreEqual(spots, herbs, "One herb per spot at full density.");
                Assert.AreEqual(32 * 32, output.Length, "Every other grass top still gets its tuft.");
            }
            finally
            {
                biomes.Dispose();
                padded.Dispose();
                Object.DestroyImmediate(grass);
                Object.DestroyImmediate(tuft);
                Object.DestroyImmediate(herb);
            }
        }

        /// <summary>Herb spots and picks under Burst, as the decoration job runs them.</summary>
        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true)]
        struct HerbProbeJob : IJob
        {
            public uint Hash;
            public int Size;
            [ReadOnly] public NativeArray<DecorationInfo> Infos;
            [ReadOnly] public NativeArray<float> Weights;
            /// <summary>Per column: -2 off a spot, else the herb chosen there (-1 for none).</summary>
            public NativeArray<int> Picks;

            public void Execute()
            {
                for (int z = 0; z < Size; z++)
                for (int x = 0; x < Size; x++)
                {
                    Picks[z * Size + x] = HerbPlacement.IsSpot(Hash, x, z)
                        ? HerbPlacement.Choose(Hash, x, z, BlockIds.Grass, 0, 1, Infos, Weights)
                        : -2;
                }
            }
        }

        [Test]
        public void Burst_PlacesTheSameHerbs_AsTheHostsCheck()
        {
            BlockDefinition grass = BlockDefinition.Create(BlockIds.Grass, "Grass");
            DecorationDefinition herb = DecorationDefinition.CreateHerb(ItemIds.Sunpetal, 0.35f, new[] { grass },
                new[] { new BiomeWeight { biome = Biome, weight = 1f } });
            const int size = 96;
            try
            {
                using var infos = new NativeArray<DecorationInfo>(new[] { herb.ToDecorationInfo() }, Allocator.TempJob);
                using var weights = new NativeArray<float>(new[] { 1f }, Allocator.TempJob);
                using var picks = new NativeArray<int>(size * size, Allocator.TempJob);
                uint hash = HerbPlacement.SeedHash(TestSeed);
                new HerbProbeJob { Hash = hash, Size = size, Infos = infos, Weights = weights, Picks = picks }.Run();
                int grown = 0;
                for (int z = 0; z < size; z++)
                for (int x = 0; x < size; x++)
                {
                    int expected = HerbPlacement.IsSpot(hash, x, z)
                        ? HerbPlacement.Choose(hash, x, z, BlockIds.Grass, 0, 1, infos, weights)
                        : -2;
                    Assert.AreEqual(expected, picks[z * size + x], $"Burst disagrees at ({x}, {z}).");
                    grown += expected == 0 ? 1 : 0;
                }
                Assert.Greater(grown, 0);
            }
            finally
            {
                Object.DestroyImmediate(grass);
                Object.DestroyImmediate(herb);
            }
        }

        // ---- With the project's world ----

        [Test]
        public void TheHostsCheck_MatchesTheDrawnHerbs_AndPickedHerbsStayHidden()
        {
            var terrain = AssetDatabase.LoadAssetAtPath<TerrainSettings>(TerrainAssetPath);
            var registry = AssetDatabase.LoadAssetAtPath<BlockRegistry>(RegistryAssetPath);
            var set = AssetDatabase.LoadAssetAtPath<DecorationSet>(DecorationSetPath);
            var world = new VoxelWorld(registry.CreateLookup(Allocator.Persistent));
            var generator = new ChunkGenerationScheduler(world, TestSeed, terrain.ToTerrainParams(), terrain.CreateBiomeLookup(Allocator.Persistent));
            var parent = new GameObject("Herb test chunks");
            var meshes = new ChunkMeshScheduler(world, parent.transform, null);
            var layer = new DecorationLayer(set, terrain, registry, TestSeed, generator.Terrain, generator.Biomes);
            meshes.Decorations = layer;
            try
            {
                var centre = new ChunkCoord(8, 2, 8);
                for (int y = 0; y < 8; y++)
                for (int z = -1; z <= 1; z++)
                for (int x = -1; x <= 1; x++)
                    generator.Request(new ChunkCoord(centre.X + x, y, centre.Z + z));
                generator.CompleteAll();
                meshes.CompleteAll();

                // Every drawn herb is one the host would let a gatherer pick, with the same item.
                var drawn = new List<(int3 cell, ushort item, int type, int slot)>();
                for (int type = 0; type < layer.Types.Count; type++)
                {
                    DecorationLayer.TypeData data = layer.Types[type];
                    if (data.Definition.Kind != DecorationKind.Herb)
                        continue;
                    for (int i = 0; i < data.Allocator.UsedEnd; i++)
                    {
                        if (data.Instances[i].Scale > 0f)
                            drawn.Add((HerbPlacement.CellOf(data.Instances[i].Position), data.Definition.HerbItem, type, i));
                    }
                }
                Assert.Greater(drawn.Count, 0, "No herbs in the meadow round (256, 256).");
                foreach ((int3 cell, ushort item, _, _) in drawn)
                {
                    Assert.IsTrue(layer.TryGetHerb(cell, world.GetVoxel(cell).BlockId, world.GetVoxel(cell - new int3(0, 1, 0)).BlockId,
                        out ushort found), $"The host sees no herb at {cell}.");
                    Assert.AreEqual(item, found);
                }
                Assert.IsFalse(layer.TryGetHerb(drawn[0].cell + new int3(1, 0, 0), BlockIds.Air, BlockIds.Grass, out _),
                    "Next to a herb there's none (herbs never touch).");

                // Picking hides it; a remesh of its chunk keeps it hidden; growing back shows it at its old size.
                (int3 picked, _, int herbType, int slot) = drawn[0];
                float scale = layer.Types[herbType].Instances[slot].Scale;
                layer.SetHerbHidden(picked, true);
                Assert.AreEqual(0f, layer.Types[herbType].Instances[slot].Scale);

                ChunkCoord chunk = VoxelMath.WorldToChunk(picked - new int3(0, 1, 0));
                int3 elsewhere = chunk.WorldOrigin;
                meshes.CompleteGatherJobs();
                world.SetVoxel(elsewhere, world.GetVoxel(elsewhere).BlockId == BlockIds.Air ? new Voxel(BlockIds.Stone) : Voxel.Air);
                meshes.CompleteAll();
                Assert.IsTrue(layer.IsHerbHidden(picked));
                Assert.AreEqual(0f, FindHerbScale(layer, picked), "Still hidden after its chunk was rebuilt.");

                layer.SetHerbHidden(picked, false);
                Assert.AreEqual(scale, FindHerbScale(layer, picked), 1e-6f, "Grown back at its own size.");
            }
            finally
            {
                meshes.Dispose();
                layer.Dispose();
                generator.Dispose();
                world.Dispose();
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void TheDecorationJob_UnderBurst_PlacesTheSameAsWithoutIt()
        {
            // Roadmap 5.7: the editor's Burst cache once kept running a build of this job from before herbs, with no
            // error. This catches that (and any Burst-only difference) by running the job both ways on real chunks.
            var terrain = AssetDatabase.LoadAssetAtPath<TerrainSettings>(TerrainAssetPath);
            var registry = AssetDatabase.LoadAssetAtPath<BlockRegistry>(RegistryAssetPath);
            var set = AssetDatabase.LoadAssetAtPath<DecorationSet>(DecorationSetPath);
            var world = new VoxelWorld(registry.CreateLookup(Allocator.Persistent));
            var generator = new ChunkGenerationScheduler(world, TestSeed, terrain.ToTerrainParams(), terrain.CreateBiomeLookup(Allocator.Persistent));
            set.CreateLookups(terrain, registry, Allocator.TempJob, out NativeArray<DecorationInfo> infos, out NativeArray<float> weights);
            var padded = new NativeArray<ushort>(PaddedVolume, Allocator.TempJob);
            var burst = new NativeList<DecorationInstance>(Allocator.TempJob);
            var managed = new NativeList<DecorationInstance>(Allocator.TempJob);
            try
            {
                for (int y = 1; y < 5; y++)
                for (int z = 7; z <= 9; z++)
                for (int x = 7; x <= 9; x++)
                    generator.Request(new ChunkCoord(x, y, z));
                generator.CompleteAll();

                int herbs = 0;
                for (int cy = 2; cy < 4; cy++)
                {
                    var origin = new int3(8 * 32, cy * 32, 8 * 32);
                    for (int z = 0; z < 34; z++)
                    for (int y = 0; y < 34; y++)
                    for (int x = 0; x < 34; x++)
                        padded[PaddedIndex(x, y, z)] = world.GetVoxel(origin + new int3(x - 1, y - 1, z - 1)).BlockId;
                    var job = new DecorateChunkJob
                    {
                        Padded = padded, Blocks = world.Blocks, Origin = origin, Seed = TestSeed, Terrain = generator.Terrain,
                        Biomes = generator.Biomes, Decorations = infos, BiomeWeights = weights, Output = burst,
                    };
                    job.Run();
                    job.Output = managed;
                    job.Execute();
                    Assert.AreEqual(managed.Length, burst.Length, $"Chunk y {cy}: Burst placed a different number of decorations.");
                    for (int i = 0; i < managed.Length; i++)
                    {
                        Assert.AreEqual(managed[i].Type, burst[i].Type, $"Chunk y {cy}, decoration {i}.");
                        Assert.Less(math.distance(managed[i].Position, burst[i].Position), 1e-4f, "Same spot (to rounding).");
                        herbs += layerIsHerb(set, managed[i].Type) ? 1 : 0;
                    }
                }
                Assert.Greater(herbs, 0, "Expected a herb or two in the meadow chunk round (256, 256).");
            }
            finally
            {
                burst.Dispose();
                managed.Dispose();
                padded.Dispose();
                infos.Dispose();
                weights.Dispose();
                generator.Dispose();
                world.Dispose();
            }

            static bool layerIsHerb(DecorationSet set, uint type) => set.Decorations[(int)type].Kind == DecorationKind.Herb;
        }

        static float FindHerbScale(DecorationLayer layer, int3 cell)
        {
            foreach (DecorationLayer.TypeData data in layer.Types)
            {
                if (data.Definition.Kind != DecorationKind.Herb)
                    continue;
                for (int i = 0; i < data.Allocator.UsedEnd; i++)
                {
                    DecorationInstance instance = data.Instances[i];
                    if (instance.Type < layer.Types.Count && math.all(HerbPlacement.CellOf(instance.Position) == cell) &&
                        math.lengthsq(instance.Position) > 0f)
                        return instance.Scale;
                }
            }
            Assert.Fail($"No herb drawn at {cell}.");
            return -1f;
        }

        [Test]
        public void ProjectHerbs_HaveItems_AndNodesNeedingOneTwentyFiveAndFifty()
        {
            var set = AssetDatabase.LoadAssetAtPath<DecorationSet>(DecorationSetPath);
            var items = AssetDatabase.LoadAssetAtPath<ItemRegistry>(ItemRegistryPath);
            var book = AssetDatabase.LoadAssetAtPath<ProfessionBook>(ProfessionBookPath);
            CollectionAssert.IsEmpty(book.Validate());
            var herbs = new Dictionary<ushort, int>();
            foreach (DecorationDefinition decoration in set.Decorations)
            {
                if (decoration == null || decoration.Kind != DecorationKind.Herb)
                    continue;
                Assert.IsTrue(items.TryGet(decoration.HerbItem, out ItemDefinition item), $"{decoration.name}: no item.");
                Assert.AreEqual(ItemCategory.Materials, item.Category);
                Assert.IsTrue(book.TryGetNode(GatherNodeKind.Herb, decoration.HerbItem, out GatherNode node), $"{decoration.name}: no node.");
                Assert.AreEqual(ProfessionId.Herbalism, node.Profession);
                Assert.Greater(node.GatherSeconds, 0f);
                bool givesItself = false;
                foreach (LootDrop drop in node.Yield.Drops)
                    givesItself |= drop.Item == decoration.HerbItem;
                Assert.IsTrue(givesItself, $"Picking {item.DisplayName} gives no {item.DisplayName}.");
                herbs[decoration.HerbItem] = node.RequiredSkill;
            }
            CollectionAssert.AreEquivalent(new Dictionary<ushort, int>
            {
                { ItemIds.Sunpetal, 1 }, { ItemIds.Frostbloom, 25 }, { ItemIds.Emberthorn, 50 },
            }, herbs);
            Assert.AreEqual(300f, book.HerbRegrowSeconds);
        }
    }
}
