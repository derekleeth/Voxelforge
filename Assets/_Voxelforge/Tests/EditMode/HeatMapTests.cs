using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using Voxelforge.Gameplay;
using Voxelforge.Voxels;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 5.7 step E: the resource heat maps: what's rich where, and what each player is shown.</summary>
    public class HeatMapTests
    {
        const string TerrainAssetPath = "Assets/_Voxelforge/Data/Terrain/TerrainSettings.asset";
        static readonly WorldSeed TestSeed = new WorldSeed(20261027);

        ProfessionBook _book;
        MapHeat _heat;

        [SetUp]
        public void SetUp()
        {
            _book = ProfessionBook.Create(heatMapSkill: 10);
            var ore = new[]
            {
                new MapHeat.Layer("Mana", new Color32(150, 80, 255, 255), ProfessionId.Mining, 50, 0, null),
                new MapHeat.Layer("Iron", new Color32(200, 60, 45, 255), ProfessionId.Mining, 25, 1, null),
                new MapHeat.Layer("Copper", new Color32(240, 130, 50, 255), ProfessionId.Mining, 1, 2, null),
            };
            var herbs = new[]
            {
                new MapHeat.Layer("Sunpetal", new Color32(255, 205, 40, 255), ProfessionId.Herbalism, 1, -1, new[] { 1f, 0f }),
            };
            _heat = new MapHeat(null, _book, ore, herbs);
        }

        [TearDown]
        public void TearDown()
        {
            _heat.Dispose();
            Object.DestroyImmediate(_book);
        }

        [Test]
        public void OreScore_IsZeroWhereAVeinHardlyGrows_AndOneWhereItsRichest()
        {
            Assert.AreEqual(0f, ResourceHeatRules.OreScore(0f, 0.8f));
            Assert.AreEqual(0f, ResourceHeatRules.OreScore(0.85f, 0.8f), "Richness barely over the threshold: a trace.");
            Assert.AreEqual(1f, ResourceHeatRules.OreScore(1.4f, 0.8f), 1e-5f);
            Assert.Greater(ResourceHeatRules.OreScore(1.1f, 0.8f), ResourceHeatRules.OreScore(1.0f, 0.8f));
        }

        [Test]
        public void HeatMaps_UnlockAtTen_AndCycleOffOreHerbs()
        {
            ProfessionSkills novice = ProfessionSkills.Start;
            ProfessionSkills miner = novice.With(ProfessionId.Mining, 10);
            ProfessionSkills both = miner.With(ProfessionId.Herbalism, 12);
            Assert.AreEqual(HeatMode.Off, MapHeat.Next(HeatMode.Off, novice, _book), "Nothing to show yet.");
            Assert.AreEqual(HeatMode.Ore, MapHeat.Next(HeatMode.Off, miner, _book));
            Assert.AreEqual(HeatMode.Off, MapHeat.Next(HeatMode.Ore, miner, _book), "Herbs skipped: Herbalism is too low.");
            Assert.AreEqual(HeatMode.Herbs, MapHeat.Next(HeatMode.Ore, both, _book));
            Assert.AreEqual(HeatMode.Off, MapHeat.Next(HeatMode.Herbs, both, _book));
        }

        [Test]
        public void TheMask_HoldsOnlyTheTiersThePlayerCanGather_LessCopperOnceTheresMore()
        {
            Assert.AreEqual(0, _heat.MaskFor(HeatMode.Ore, ProfessionSkills.Start), "Below the heat map's skill: nothing.");
            Assert.AreEqual(0b100, _heat.MaskFor(HeatMode.Ore, ProfessionSkills.Start.With(ProfessionId.Mining, 10)), "Copper only.");
            Assert.AreEqual(0b010, _heat.MaskFor(HeatMode.Ore, ProfessionSkills.Start.With(ProfessionId.Mining, 30)),
                "Iron: copper (rich nearly everywhere) drops off.");
            Assert.AreEqual(0b011, _heat.MaskFor(HeatMode.Ore, ProfessionSkills.Start.With(ProfessionId.Mining, 60)));
        }

        [Test]
        public void APatch_ShowsTheTiersThatAreRichThere_AndOnlyIfThePlayerCanGatherThem()
        {
            var patch = new ResourceHeat { Ore = new float4(0.9f, 0.6f, 1f, 0f), Biome = 0 };
            Color32 manaOnly = _heat.Paint(HeatMode.Ore, 0b001, patch);
            Assert.AreEqual(150, manaOnly.r);
            Assert.AreEqual(255, manaOnly.b);
            Color32 manaAndIron = _heat.Paint(HeatMode.Ore, 0b011, patch);
            Assert.That(manaAndIron.r, Is.InRange(151, 199), "Where both are rich, their colours blend, leaning to the richer.");
            Assert.Less(manaAndIron.b, 255);
            Color32 copperOnly = _heat.Paint(HeatMode.Ore, 0b100, patch);
            Assert.AreEqual(240, copperOnly.r);
            Assert.Greater(manaOnly.a, 0);
            Assert.Less(manaOnly.a, 200, "See-through, so the land reads under it.");

            var poor = new ResourceHeat { Ore = new float4(0.05f, 0.1f, 0.1f, 0f) };
            Assert.AreEqual(0, _heat.Paint(HeatMode.Ore, 0b111, poor).a, "Nothing worth going for: clear.");

            Assert.Greater(_heat.Paint(HeatMode.Herbs, 0b1, new ResourceHeat { Biome = 0 }).a, 0, "Sunpetal grows in biome 0.");
            Assert.AreEqual(0, _heat.Paint(HeatMode.Herbs, 0b1, new ResourceHeat { Biome = 1 }).a);
        }

        [Test]
        public void TheProjectsWorld_HasIronRichPatches_AndCopperAlmostEverywhere()
        {
            var terrain = AssetDatabase.LoadAssetAtPath<TerrainSettings>(TerrainAssetPath);
            using NativeArray<BiomeInfo> biomes = terrain.CreateBiomeLookup(Allocator.Persistent);
            using var cache = new ResourceHeatCache(TestSeed, terrain.ToTerrainParams(), biomes);
            int iron = 0, copper = 0, patches = 0;
            for (int tz = -6; tz < 6; tz++)
            for (int tx = -6; tx < 6; tx++)
            {
                foreach (ResourceHeat patch in cache.SampleNow(new int2(tx, tz)))
                {
                    patches++;
                    iron += patch.Ore[1] >= 0.5f ? 1 : 0;
                    copper += patch.Ore[2] >= 0.5f ? 1 : 0;
                }
            }
            Assert.AreEqual(144 * ResourceHeatRules.Cells * ResourceHeatRules.Cells, patches);
            Assert.Greater(copper, patches * 0.6f, "Copper grows nearly everywhere.");
            Assert.Greater(iron, 0, "Somewhere in 1.5 km there's a rich iron biome.");
            Assert.Less(iron, copper, "Iron is choosier.");
        }
    }
}
