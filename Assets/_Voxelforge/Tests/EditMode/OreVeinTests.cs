using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using Voxelforge.Gameplay;
using Voxelforge.Voxels;
using Object = UnityEngine.Object;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 5.7 step B: ore tiers by depth and biome, ore that drops items, and the Mining gate's data.</summary>
    public class OreVeinTests
    {
        const string TerrainAssetPath = "Assets/_Voxelforge/Data/Terrain/TerrainSettings.asset";
        const string BlockRegistryPath = "Assets/_Voxelforge/Data/Blocks/BlockRegistry.asset";
        const string ItemRegistryPath = "Assets/_Voxelforge/Data/Items/ItemRegistry.asset";
        const string ProfessionBookPath = "Assets/_Voxelforge/Data/Professions/ProfessionBook.asset";
        const string RedCrags = "Red Crags (Redridge style)";
        const string SnowyPeaks = "Snowy Peaks (Dun Morogh style)";
        static readonly WorldSeed TestSeed = new WorldSeed(20261027);

        TerrainSettings _settings;
        NativeArray<BiomeInfo> _biomes;
        TerrainSampler _sampler;

        [SetUp]
        public void SetUp()
        {
            _settings = AssetDatabase.LoadAssetAtPath<TerrainSettings>(TerrainAssetPath);
            Assert.IsNotNull(_settings, $"Missing {TerrainAssetPath}");
            _biomes = _settings.CreateBiomeLookup(Allocator.Persistent);
            _sampler = new TerrainSampler(TestSeed, _settings.ToTerrainParams(), _biomes, Allocator.Persistent);
        }

        [TearDown]
        public void TearDown()
        {
            _sampler.Dispose();
            if (_biomes.IsCreated)
                _biomes.Dispose();
        }

        // ---- The project's veins ----

        [Test]
        public void ProjectVeins_AreManaThenIronThenCopper_AndValid()
        {
            var registry = AssetDatabase.LoadAssetAtPath<BlockRegistry>(BlockRegistryPath);
            CollectionAssert.IsEmpty(_settings.Validate(registry));
            Assert.AreEqual(3, _settings.OreVeins.Count, "Run Voxelforge > Build Professions.");
            Assert.AreEqual(BlockIds.ManaCrystalOre, _settings.OreVeins[0].Block.Id, "Rarest first.");
            Assert.AreEqual(BlockIds.IronOre, _settings.OreVeins[1].Block.Id);
            Assert.AreEqual(BlockIds.CopperOre, _settings.OreVeins[2].Block.Id);
            Assert.Less(_settings.OreVeins[0].MaxY, _settings.OreVeins[1].MaxY, "Mana Crystal is the deepest...");
            Assert.Less(_settings.OreVeins[1].MaxY, _settings.OreVeins[2].MaxY, "...then Iron, then Copper.");
        }

        [Test]
        public void VeinRichness_IsTheVeinsTimesTheBiomes()
        {
            BiomeSettings crags = Find(RedCrags);
            float4 richness = _settings.VeinRichnessIn(crags);
            for (int v = 0; v < _settings.OreVeins.Count; v++)
                Assert.AreEqual(_settings.OreVeins[v].RichnessIn(RedCrags) * crags.OreRichness, richness[v], 1e-6f);
            Assert.AreEqual(0f, richness[3], "No fourth vein.");
        }

        [Test]
        public void Ore_NeverGrowsAboveItsVeinsTop_AndCopperIsEverywhere()
        {
            var counts = Sample(1500);
            foreach (KeyValuePair<string, Dictionary<ushort, int>> biome in counts.ByBiome)
                Assert.Greater(Get(biome.Value, BlockIds.CopperOre), 0, $"No copper in {biome.Key}.");
            Assert.AreEqual(0, counts.ManaAbove40, "Mana Crystal Ore above y 40.");
            Assert.AreEqual(0, counts.IronAbove56, "Iron Ore above y 56.");
        }

        [Test]
        public void HigherTiers_AreRicherInTheMountains()
        {
            var counts = Sample(1500);
            float Share(string biome, ushort ore) => Get(counts.ByBiome[biome], ore) / (float)counts.Rock[biome];
            const string meadows = "Meadows (Elwynn style)";
            Assert.Greater(Share(RedCrags, BlockIds.IronOre), 3f * Share(meadows, BlockIds.IronOre));
            Assert.Greater(Share(SnowyPeaks, BlockIds.ManaCrystalOre), 3f * Share(meadows, BlockIds.ManaCrystalOre));
            Assert.Greater(Share(meadows, BlockIds.CopperOre), 5f * Share(meadows, BlockIds.IronOre),
                "Copper is the common ore.");
        }

        [Test]
        public void TheFirstVeinWins_WhereTwoOverlap()
        {
            BlockDefinition a = BlockDefinition.Create(BlockIds.ManaCrystalOre, "A");
            BlockDefinition b = BlockDefinition.Create(BlockIds.CopperOre, "B");
            try
            {
                // Two veins that fill every block (threshold below any noise): only the first ever shows.
                var veins = new FixedList128Bytes<OreVeinInfo>();
                veins.Add(OreVein.Create(a, 0.1f, -2f, 1, 100, 0, 1f).ToVeinInfo());
                veins.Add(OreVein.Create(b, 0.1f, -2f, 1, 100, 0, 1f).ToVeinInfo());
                TerrainParams terrain = _settings.ToTerrainParams();
                terrain.OreVeins = veins;
                var sampler = new TerrainSampler(TestSeed, terrain, _biomes, Allocator.Temp);
                var column = new TerrainColumn { Height = 120, Biome = 0, OreRichness = new float4(1f, 1f, 0f, 0f) };
                for (int y = 20; y < 30; y++)
                    Assert.AreEqual(BlockIds.ManaCrystalOre, sampler.BlockAt(new int3(5, y, 5), column));
                column.OreRichness = new float4(0f, 1f, 0f, 0f);
                Assert.AreEqual(BlockIds.CopperOre, sampler.BlockAt(new int3(5, 25, 5), column), "No richness: the vein is off.");
                sampler.Dispose();
            }
            finally
            {
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
            }
        }

        // ---- Blocks, items and the gate ----

        [Test]
        public void OreBlocks_DropTheirItems_AreBlastProof_AndTheItemsExist()
        {
            var blocks = AssetDatabase.LoadAssetAtPath<BlockRegistry>(BlockRegistryPath);
            var items = AssetDatabase.LoadAssetAtPath<ItemRegistry>(ItemRegistryPath);
            CollectionAssert.IsEmpty(items.Validate(blocks));
            foreach ((ushort block, ushort item) in new[]
                     {
                         (BlockIds.CopperOre, ItemIds.CopperOre), (BlockIds.IronOre, ItemIds.IronOre),
                         (BlockIds.ManaCrystalOre, ItemIds.ManaCrystal),
                     })
            {
                Assert.IsTrue(blocks.TryGet(block, out BlockDefinition definition));
                Assert.AreEqual(item, definition.DropId, definition.DisplayName);
                Assert.IsTrue(definition.IsBlastProof, $"{definition.DisplayName}: only mining gets ore out.");
                Assert.AreEqual(HarvestKind.Pick, definition.HarvestKind);
                Assert.IsTrue(items.TryGet(item, out ItemDefinition ore), $"No item {item}.");
                Assert.AreEqual(ItemCategory.Materials, ore.Category);
            }
        }

        [Test]
        public void OreNodes_NeedOneTwentyFiveAndFifty()
        {
            var book = AssetDatabase.LoadAssetAtPath<ProfessionBook>(ProfessionBookPath);
            Assert.IsTrue(book.TryGetNode(GatherNodeKind.Ore, BlockIds.CopperOre, out GatherNode copper));
            Assert.IsTrue(book.TryGetNode(GatherNodeKind.Ore, BlockIds.IronOre, out GatherNode iron));
            Assert.IsTrue(book.TryGetNode(GatherNodeKind.Ore, BlockIds.ManaCrystalOre, out GatherNode mana));
            Assert.AreEqual(1, copper.RequiredSkill);
            Assert.AreEqual(25, iron.RequiredSkill);
            Assert.AreEqual(50, mana.RequiredSkill);
            Assert.AreEqual(ProfessionId.Mining, iron.Profession);
            Assert.IsFalse(book.TryGetNode(GatherNodeKind.Ore, BlockIds.Stone, out _), "Stone isn't ore.");

            ProfessionSkills novice = ProfessionSkills.Start;
            Assert.IsTrue(ProfessionRules.CanGather(novice, copper));
            Assert.IsFalse(ProfessionRules.CanGather(novice, iron), "A new miner's pick bounces off iron.");
            Assert.IsTrue(ProfessionRules.CanGather(novice.With(ProfessionId.Mining, 25), iron));
            Assert.IsFalse(ProfessionRules.CanGather(novice.With(ProfessionId.Mining, 49), mana));
        }

        [Test]
        public void ManaCrystalRecipes_TakeTheItem()
        {
            var book = AssetDatabase.LoadAssetAtPath<RecipeBook>("Assets/_Voxelforge/Data/Items/RecipeBook.asset");
            int recipes = 0;
            foreach (RecipeDefinition recipe in book.Recipes)
            {
                foreach (ItemAmount input in recipe.Inputs)
                {
                    Assert.IsFalse(input.block != null && input.block.Id == BlockIds.ManaCrystalOre,
                        $"{recipe.name} still takes the Mana Crystal Ore block.");
                    if (input.item != null && input.item.Id == ItemIds.ManaCrystal)
                        recipes++;
                }
            }
            Assert.AreEqual(4, recipes, "The Pick of Destiny, M.G.R.S., Home Totem and Delver's Rucksack.");
        }

        // ---- Helpers ----

        sealed class Counts
        {
            public readonly Dictionary<string, Dictionary<ushort, int>> ByBiome = new Dictionary<string, Dictionary<ushort, int>>();
            public readonly Dictionary<string, int> Rock = new Dictionary<string, int>();
            public int ManaAbove40;
            public int IronAbove56;
        }

        /// <summary>The ore and rock in <paramref name="columns"/> spread-out columns, by biome.</summary>
        Counts Sample(int columns)
        {
            var counts = new Counts();
            var random = new System.Random(5);
            for (int i = 0; i < columns; i++)
            {
                int x = random.Next(-6000, 6000), z = random.Next(-6000, 6000);
                TerrainColumn column = _sampler.SampleColumn(x, z);
                string biome = _settings.Biomes[column.Biome].Name;
                if (!counts.ByBiome.TryGetValue(biome, out Dictionary<ushort, int> ores))
                {
                    counts.ByBiome[biome] = ores = new Dictionary<ushort, int>();
                    counts.Rock[biome] = 0;
                }
                for (int y = 1; y < column.Height - 4; y++)
                {
                    ushort block = _sampler.BlockAt(new int3(x, y, z), column);
                    bool ore = block == BlockIds.CopperOre || block == BlockIds.IronOre || block == BlockIds.ManaCrystalOre;
                    if (!ore && block != BlockIds.Stone)
                        continue;
                    counts.Rock[biome]++;
                    if (!ore)
                        continue;
                    ores[block] = Get(ores, block) + 1;
                    if (block == BlockIds.ManaCrystalOre && y > 40)
                        counts.ManaAbove40++;
                    if (block == BlockIds.IronOre && y > 56)
                        counts.IronAbove56++;
                }
            }
            return counts;
        }

        static int Get(Dictionary<ushort, int> counts, ushort block) => counts.TryGetValue(block, out int n) ? n : 0;

        BiomeSettings Find(string name)
        {
            foreach (BiomeSettings biome in _settings.Biomes)
            {
                if (biome.Name == name)
                    return biome;
            }
            Assert.Fail($"No biome called {name}.");
            return null;
        }
    }
}
