using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Voxelforge.Gameplay;
using Voxelforge.Voxels;

namespace Voxelforge.Editor
{
    /// <summary>
    /// <i>Voxelforge → Build Professions</i> (Roadmap 5.7, D-038): writes the <see cref="ProfessionBook"/> from the
    /// numbers below, the one place the skill cap, how skills rise, a skilled gatherer's bonus and every gatherable node
    /// live. It also makes the ore blocks and writes the ore veins into the terrain settings (step B). Re-running it
    /// updates the same assets in place. The ore items themselves come from <see cref="LootBuilder"/>, and the Mana
    /// Crystal recipes from <see cref="PropsBuilder"/>.
    /// </summary>
    public static class ProfessionBuilder
    {
        internal const string BookPath = "Assets/_Voxelforge/Data/Professions/ProfessionBook.asset";
        const string BlockDir = "Assets/_Voxelforge/Data/Blocks";
        const string TerrainPath = "Assets/_Voxelforge/Data/Terrain/TerrainSettings.asset";

        // Biome names, as in the terrain settings.
        const string Desert = "Desert (Tanaris style)";
        const string RedCrags = "Red Crags (Redridge style)";
        const string SnowyPeaks = "Snowy Peaks (Dun Morogh style)";
        const string Thunderhoof = "Thunderhoof Plains";

        const int MaxSkill = 100;

        // WoW-style: a node raises the skill every time for its first 10 points, usually for the next 15, now and then
        // for 15 more, then never ("grey"), so gatherers move on to the next tier.
        const int OrangeRange = 10;
        const int YellowRange = 25;
        const int GreenRange = 40;
        const float YellowChance = 0.6f;
        const float GreenChance = 0.25f;

        // A gatherer 50 points above a node has a 50% chance of one extra item.
        const int BonusYieldRange = 50;
        const float MaxBonusYieldChance = 0.5f;

        // The skill that shows a profession's heat map on the World Map.
        const int HeatMapSkill = 10;

        // ---- Ore (step B). Tougher tiers take longer to mine (Stone is 60) and need more skill. ----
        const int CopperDurability = 80;
        const int IronDurability = 110;
        const int ManaCrystalDurability = 150;

        // A vein grows where its noise times its richness passes its threshold, so a richness below the threshold grows
        // nothing, and small changes matter a lot. The biome's own Ore Richness multiplies in too (Desert 0.8, Thunderhoof
        // 0.9, Meadows 1, Red Crags and Snowy Peaks 1.3). Listed rarest first: where two veins overlap, the first wins.
        // Measured over 3,000 columns (seed 20261027), as a share of the rock under the surface:
        //   Copper  ~3-4% everywhere (Desert ~3%, Red Crags ~4%).
        //   Iron    ~2-2.4% in Red Crags and Snowy Peaks below y 56, a trace in Meadows and Thunderhoof, none in Desert.
        //   Mana    ~0.6% in Snowy Peaks and ~0.4% in Red Crags, below y 40 (most below 30); a trace in Meadows.
        //                          frequency threshold minY maxY fade richness
        static OreVein[] Veins(BlockDefinition copper, BlockDefinition iron, BlockDefinition mana) => new[]
        {
            OreVein.Create(mana, 0.13f, 0.8f, 1, 40, 12, 0.88f,
                new OreBiomeRichness(SnowyPeaks, 0.87f), new OreBiomeRichness(RedCrags, 0.83f)),
            OreVein.Create(iron, 0.15f, 0.78f, 1, 56, 12, 0.88f,
                new OreBiomeRichness(RedCrags, 0.97f), new OreBiomeRichness(SnowyPeaks, 0.97f)),
            OreVein.Create(copper, 0.16f, 0.66f, 1, 80, 10, 1f,
                new OreBiomeRichness(Desert, 1.2f), new OreBiomeRichness(Thunderhoof, 1.1f),
                new OreBiomeRichness(RedCrags, 0.85f), new OreBiomeRichness(SnowyPeaks, 0.85f)),
        };

        // Steps C and D add the herbs and mobs.
        static readonly GatherNode[] Nodes =
        {
            new GatherNode(ProfessionId.Mining, GatherNodeKind.Ore, BlockIds.CopperOre, 1),
            new GatherNode(ProfessionId.Mining, GatherNodeKind.Ore, BlockIds.IronOre, 25),
            new GatherNode(ProfessionId.Mining, GatherNodeKind.Ore, BlockIds.ManaCrystalOre, 50),
        };

        [MenuItem("Voxelforge/Build Professions")]
        public static void Build()
        {
            BuildOres();

            var book = AssetDatabase.LoadAssetAtPath<ProfessionBook>(BookPath);
            if (book == null)
            {
                string folder = BookPath.Substring(0, BookPath.LastIndexOf('/'));
                if (!AssetDatabase.IsValidFolder(folder))
                    AssetDatabase.CreateFolder("Assets/_Voxelforge/Data", "Professions");
                book = ScriptableObject.CreateInstance<ProfessionBook>();
                AssetDatabase.CreateAsset(book, BookPath);
            }
            var serialized = new SerializedObject(book);
            serialized.FindProperty("maxSkill").intValue = MaxSkill;
            serialized.FindProperty("orangeRange").intValue = OrangeRange;
            serialized.FindProperty("yellowRange").intValue = YellowRange;
            serialized.FindProperty("greenRange").intValue = GreenRange;
            serialized.FindProperty("yellowChance").floatValue = YellowChance;
            serialized.FindProperty("greenChance").floatValue = GreenChance;
            serialized.FindProperty("bonusYieldRange").intValue = BonusYieldRange;
            serialized.FindProperty("maxBonusYieldChance").floatValue = MaxBonusYieldChance;
            serialized.FindProperty("heatMapSkill").intValue = HeatMapSkill;
            WriteNodes(serialized.FindProperty("nodes"), Nodes);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(book);
            AssetDatabase.SaveAssets();

            List<string> problems = book.Validate();
            if (problems.Count > 0)
                Debug.LogError("[Voxelforge] Build Professions made a book with problems:\n" + string.Join("\n", problems));
            else
                Debug.Log($"[Voxelforge] Built the Profession Book: skills 1-{MaxSkill}, {Nodes.Length} gatherable nodes, " +
                          $"heat maps at {HeatMapSkill}.");
        }

        /// <summary>
        /// The ore blocks (step B): Copper and Iron Ore, new; Mana Crystal Ore, now dropping the Mana Crystal item. All
        /// are blast-proof, so only mining (and the Mining skill) gets ore out, and all go in the block registry, the
        /// texture arrays and the terrain's ore veins.
        /// </summary>
        static void BuildOres()
        {
            var registry = AssetDatabase.LoadAssetAtPath<BlockRegistry>($"{BlockDir}/BlockRegistry.asset");
            var terrain = AssetDatabase.LoadAssetAtPath<TerrainSettings>(TerrainPath);
            var mana = AssetDatabase.LoadAssetAtPath<BlockDefinition>($"{BlockDir}/ManaCrystalOre.asset");
            if (registry == null || terrain == null || mana == null)
            {
                Debug.LogError("[Voxelforge] Build Professions needs the block registry, the terrain settings and Mana Crystal Ore.");
                return;
            }

            BlockDefinition copper = StampBuilder.MakeCubeBlock("CopperOre", BlockIds.CopperOre, "Copper Ore", CopperDurability,
                blastProof: true, light: 0, new Color32(196, 112, 58, 255));
            BlockDefinition iron = StampBuilder.MakeCubeBlock("IronOre", BlockIds.IronOre, "Iron Ore", IronDurability,
                blastProof: true, light: 0, new Color32(128, 92, 80, 255));
            SetOreDrop(copper, ItemIds.CopperOre, CopperDurability);
            SetOreDrop(iron, ItemIds.IronOre, IronDurability);
            SetOreDrop(mana, ItemIds.ManaCrystal, ManaCrystalDurability);

            var serialized = new SerializedObject(registry);
            SerializedProperty list = serialized.FindProperty("blocks");
            foreach (BlockDefinition block in new[] { copper, iron })
            {
                bool listed = false;
                for (int i = 0; i < list.arraySize; i++)
                    listed |= list.GetArrayElementAtIndex(i).objectReferenceValue == block;
                if (listed)
                    continue;
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = block;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(registry);

            WriteVeins(terrain, Veins(copper, iron, mana));
            AssetDatabase.SaveAssets();
            BlockTextureArrayBuilder.Rebuild();

            List<string> problems = terrain.Validate(registry);
            if (problems.Count > 0)
                Debug.LogError("[Voxelforge] The terrain settings have problems after adding the ore:\n" + string.Join("\n", problems));
        }

        static void SetOreDrop(BlockDefinition block, ushort item, int durability)
        {
            var serialized = new SerializedObject(block);
            serialized.FindProperty("maxDurability").intValue = durability;
            serialized.FindProperty("drop").objectReferenceValue = null;
            serialized.FindProperty("dropItem").intValue = item;
            serialized.FindProperty("dropCount").intValue = 1;
            serialized.FindProperty("harvestKind").enumValueIndex = (int)HarvestKind.Pick;
            serialized.FindProperty("blastProof").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(block);
        }

        static void WriteVeins(TerrainSettings terrain, OreVein[] veins)
        {
            var serialized = new SerializedObject(terrain);
            SerializedProperty list = serialized.FindProperty("oreVeins");
            list.arraySize = veins.Length;
            for (int i = 0; i < veins.Length; i++)
            {
                SerializedProperty entry = list.GetArrayElementAtIndex(i);
                OreVeinInfo info = veins[i].ToVeinInfo();
                entry.FindPropertyRelative("block").objectReferenceValue = veins[i].Block;
                entry.FindPropertyRelative("frequency").floatValue = info.Frequency;
                entry.FindPropertyRelative("threshold").floatValue = info.Threshold;
                entry.FindPropertyRelative("minY").intValue = info.MinY;
                entry.FindPropertyRelative("maxY").intValue = info.MaxY;
                entry.FindPropertyRelative("topFade").intValue = info.TopFade;
                entry.FindPropertyRelative("richness").floatValue = veins[i].RichnessIn(null);
                SerializedProperty biomes = entry.FindPropertyRelative("biomeRichness");
                biomes.arraySize = veins[i].BiomeRichness.Count;
                for (int b = 0; b < veins[i].BiomeRichness.Count; b++)
                {
                    SerializedProperty biome = biomes.GetArrayElementAtIndex(b);
                    biome.FindPropertyRelative("biome").stringValue = veins[i].BiomeRichness[b].biome;
                    biome.FindPropertyRelative("richness").floatValue = veins[i].BiomeRichness[b].richness;
                }
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(terrain);
        }

        static void WriteNodes(SerializedProperty list, IReadOnlyList<GatherNode> nodes)
        {
            list.arraySize = nodes.Count;
            for (int i = 0; i < nodes.Count; i++)
            {
                SerializedProperty entry = list.GetArrayElementAtIndex(i);
                GatherNode node = nodes[i];
                entry.FindPropertyRelative("Profession").intValue = (int)node.Profession;
                entry.FindPropertyRelative("Kind").intValue = (int)node.Kind;
                entry.FindPropertyRelative("Id").intValue = node.Id;
                entry.FindPropertyRelative("RequiredSkill").intValue = node.RequiredSkill;
                entry.FindPropertyRelative("Yield").objectReferenceValue = node.Yield;
                entry.FindPropertyRelative("GatherSeconds").floatValue = node.GatherSeconds;
            }
        }
    }
}
