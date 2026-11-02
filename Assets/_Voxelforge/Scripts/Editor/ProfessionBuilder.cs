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
    /// live. It also makes the ore blocks and writes the ore veins into the terrain settings (step B), makes the herb
    /// items and what each herb gives (step C), and what skinning or salvaging each mob's body gives (step D). Re-running it updates the same assets in place. The ore items themselves
    /// come from <see cref="LootBuilder"/>, the Mana Crystal recipes from <see cref="PropsBuilder"/>, and where herbs grow
    /// from <see cref="FoliageDecorationBuilder"/> (run it first, after build_herbs() in build_foliage.py).
    /// </summary>
    public static class ProfessionBuilder
    {
        internal const string BookPath = "Assets/_Voxelforge/Data/Professions/ProfessionBook.asset";
        const string BlockDir = "Assets/_Voxelforge/Data/Blocks";
        const string TerrainPath = "Assets/_Voxelforge/Data/Terrain/TerrainSettings.asset";
        const string YieldDir = "Assets/_Voxelforge/Data/Professions/Yields";
        const string HerbItemDir = "Assets/_Voxelforge/Data/Items/Herbs";
        const string ItemRegistryPath = "Assets/_Voxelforge/Data/Items/ItemRegistry.asset";
        const string FoliageModelDir = "Assets/_Voxelforge/Art/Models/Foliage";
        const string HerbMaterialPath = "Assets/_Voxelforge/Art/Materials/Foliage/Herb_Held.mat";
        const string MobBookPath = "Assets/_Voxelforge/Data/Mobs/MobBook.asset";

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

        // ---- Herbs (step C) ----
        // A picked herb grows back after this long (kept by the host, saved with the world).
        const float HerbRegrowSeconds = 300f;
        // The gather cast: moving, jumping or mining cancels it.
        const float HerbGatherSeconds = 1.5f;

        readonly struct Herb
        {
            public readonly ushort Item;
            public readonly string Model;
            public readonly string Name;
            public readonly ItemRarity Rarity;
            public readonly int Skill;
            public readonly LootDrop[] Yield;

            public Herb(ushort item, string model, string name, ItemRarity rarity, int skill, params LootDrop[] yield)
            {
                Item = item;
                Model = model;
                Name = name;
                Rarity = rarity;
                Skill = skill;
                Yield = yield;
            }
        }

        // Each pick gives 1-2 of the herb; the rarer ones sometimes a little of their biome's essence too.
        static readonly Herb[] Herbs =
        {
            new Herb(ItemIds.Sunpetal, "HerbSunpetal", "Sunpetal", ItemRarity.Common, 1, new LootDrop(ItemIds.Sunpetal, 1f, 1, 2)),
            new Herb(ItemIds.Frostbloom, "HerbFrostbloom", "Frostbloom", ItemRarity.Common, 25,
                new LootDrop(ItemIds.Frostbloom, 1f, 1, 2), new LootDrop(ItemIds.FrostEssence, 0.05f)),
            new Herb(ItemIds.Emberthorn, "HerbEmberthorn", "Emberthorn", ItemRarity.Rare, 50,
                new LootDrop(ItemIds.Emberthorn, 1f, 1, 2), new LootDrop(ItemIds.EarthEssence, 0.08f)),
        };

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

        // ---- Skinning and salvaging (step D) ----
        const float SkinSeconds = 1.5f;

        readonly struct Body
        {
            public readonly MobId Mob;
            public readonly string Name;
            public readonly int Skill;
            public readonly bool Salvaged;
            public readonly LootDrop[] Yield;

            public Body(MobId mob, string name, int skill, bool salvaged, params LootDrop[] yield)
            {
                Mob = mob;
                Name = name;
                Skill = skill;
                Salvaged = salvaged;
                Yield = yield;
            }
        }

        // Each hero who fought the mob skins it once, on top of their loot sack: more of its hide (the first drop is what
        // the cast bar shows), sometimes a little extra. A slime only gives anything once it's down to its smallest pieces.
        static readonly Body[] Bodies =
        {
            new Body(MobId.BristlebackBoar, "SkinBoar", 1, false, new LootDrop(ItemIds.BoarHide, 1f, 1, 2), new LootDrop(ItemIds.BoarTusk, 0.25f)),
            new Body(MobId.FrostfangWolf, "SkinWolf", 15, false, new LootDrop(ItemIds.WolfPelt, 1f, 1, 2), new LootDrop(ItemIds.Frostfang, 0.25f)),
            new Body(MobId.CrystalSlimeSmall, "SalvageSlime", 25, true, new LootDrop(ItemIds.CrystalShard, 1f),
                new LootDrop(ItemIds.ArcaneEssence, 0.05f)),
            new Body(MobId.ThunderRhino, "SkinRhino", 40, false, new LootDrop(ItemIds.RhinoHide, 1f, 1, 2),
                new LootDrop(ItemIds.ThunderHorn, 0.2f), new LootDrop(ItemIds.StormEssence, 0.05f)),
        };

        static readonly GatherNode[] OreNodes =
        {
            new GatherNode(ProfessionId.Mining, GatherNodeKind.Ore, BlockIds.CopperOre, 1),
            new GatherNode(ProfessionId.Mining, GatherNodeKind.Ore, BlockIds.IronOre, 25),
            new GatherNode(ProfessionId.Mining, GatherNodeKind.Ore, BlockIds.ManaCrystalOre, 50),
        };

        [MenuItem("Voxelforge/Build Professions")]
        public static void Build()
        {
            BuildOres();
            var nodes = new List<GatherNode>(OreNodes);
            if (!BuildHerbs(nodes) || !BuildSkinning(nodes))
                return;

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
            serialized.FindProperty("herbRegrowSeconds").floatValue = HerbRegrowSeconds;
            WriteNodes(serialized.FindProperty("nodes"), nodes);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(book);
            AssetDatabase.SaveAssets();

            List<string> problems = book.Validate();
            if (problems.Count > 0)
                Debug.LogError("[Voxelforge] Build Professions made a book with problems:\n" + string.Join("\n", problems));
            else
                Debug.Log($"[Voxelforge] Built the Profession Book: skills 1-{MaxSkill}, {nodes.Count} gatherable nodes, " +
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

        /// <summary>
        /// The herbs (step C): an item for each (its decoration's model, held and dropped with a cut-out lit material),
        /// a yield table, and a gather node in <paramref name="nodes"/>. False if the art is missing.
        /// </summary>
        static bool BuildHerbs(List<GatherNode> nodes)
        {
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(FoliageDecorationBuilder.HerbAtlasPath);
            var glow = AssetDatabase.LoadAssetAtPath<Texture2D>(FoliageDecorationBuilder.HerbGlowPath);
            var registry = AssetDatabase.LoadAssetAtPath<ItemRegistry>(ItemRegistryPath);
            if (atlas == null || glow == null || registry == null)
            {
                Debug.LogError("[Voxelforge] Build Professions needs the herb art (build_herbs() in build_foliage.py) and the item registry.");
                return false;
            }
            EnsureFolder(YieldDir);
            EnsureFolder(HerbItemDir);
            Material held = MakeHerbMaterial(atlas, glow);

            var serialized = new SerializedObject(registry);
            SerializedProperty list = serialized.FindProperty("items");
            foreach (Herb herb in Herbs)
            {
                ItemDefinition item = MakeHerbItem(herb, held);
                if (item == null)
                    return false;
                bool listed = false;
                for (int i = 0; i < list.arraySize; i++)
                    listed |= list.GetArrayElementAtIndex(i).objectReferenceValue == item;
                if (!listed)
                {
                    list.arraySize++;
                    list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = item;
                }
                LootTable yield = MakeYield(herb.Model, herb.Yield);
                nodes.Add(new GatherNode(ProfessionId.Herbalism, GatherNodeKind.Herb, herb.Item, herb.Skill, yield, HerbGatherSeconds));
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();
            return true;
        }

        /// <summary>
        /// Skinning and salvaging (step D): a yield table and a gather node per mob, and the mob's "salvaged" word. False
        /// if the mob book is missing.
        /// </summary>
        static bool BuildSkinning(List<GatherNode> nodes)
        {
            var mobs = AssetDatabase.LoadAssetAtPath<MobBook>(MobBookPath);
            if (mobs == null)
            {
                Debug.LogError($"[Voxelforge] Build Professions needs {MobBookPath}.");
                return false;
            }
            foreach (MobDefinition mob in mobs.Mobs)
            {
                if (mob == null)
                    continue;
                // Every slime size is salvaged, even the ones that only split, so the word is right everywhere.
                bool salvaged = IsSalvaged(mob);
                var serialized = new SerializedObject(mob);
                serialized.FindProperty("salvaged").boolValue = salvaged;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(mob);
            }
            foreach (Body body in Bodies)
            {
                LootTable yield = MakeYield(body.Name, body.Yield);
                nodes.Add(new GatherNode(ProfessionId.Skinning, GatherNodeKind.Mob, (ushort)body.Mob, body.Skill, yield, SkinSeconds));
            }
            AssetDatabase.SaveAssets();
            return true;
        }

        /// <summary>True if <paramref name="mob"/>, or what it splits into, is salvaged rather than skinned.</summary>
        static bool IsSalvaged(MobDefinition mob)
        {
            for (MobDefinition piece = mob; piece != null; piece = piece.SplitInto)
            {
                foreach (Body body in Bodies)
                {
                    if (body.Mob == piece.Id && body.Salvaged)
                        return true;
                }
            }
            return false;
        }

        static ItemDefinition MakeHerbItem(Herb herb, Material material)
        {
            Mesh mesh = null;
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath($"{FoliageModelDir}/{herb.Model}.fbx"))
            {
                if (asset is Mesh m && m.name == herb.Model + "_LOD0")
                    mesh = m;
            }
            if (mesh == null)
            {
                Debug.LogError($"[Voxelforge] {FoliageModelDir}/{herb.Model}.fbx needs a mesh '{herb.Model}_LOD0'.");
                return null;
            }
            string path = $"{HerbItemDir}/{herb.Model}.asset";
            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<ItemDefinition>();
                AssetDatabase.CreateAsset(item, path);
            }
            var serialized = new SerializedObject(item);
            serialized.FindProperty("id").intValue = herb.Item;
            serialized.FindProperty("displayName").stringValue = herb.Name;
            serialized.FindProperty("category").enumValueIndex = (int)ItemCategory.Materials;
            serialized.FindProperty("maxStack").intValue = 0; // Materials stack to 999.
            serialized.FindProperty("rarity").enumValueIndex = (int)herb.Rarity;
            serialized.FindProperty("mesh").objectReferenceValue = mesh;
            serialized.FindProperty("material").objectReferenceValue = material;
            serialized.FindProperty("heldPosition").vector3Value = PropsBuilder.Grip;
            serialized.FindProperty("heldRotation").vector3Value = PropsBuilder.UprightPose;
            serialized.FindProperty("heldScale").floatValue = 0.6f;
            serialized.FindProperty("dropSize").floatValue = 1.4f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(item);
            return item;
        }

        /// <summary>The herbs' look in a hand or lying on the ground: their atlas, cut out, both sides, glowing a little.</summary>
        static Material MakeHerbMaterial(Texture2D atlas, Texture2D glow)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            var material = AssetDatabase.LoadAssetAtPath<Material>(HerbMaterialPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, HerbMaterialPath);
            }
            material.shader = shader;
            material.SetTexture("_BaseMap", atlas);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", 0.5f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.SetFloat("_Cull", 0f);
            material.SetFloat("_Smoothness", 0.1f);
            material.SetTexture("_EmissionMap", glow);
            material.SetColor("_EmissionColor", new Color(0.9f, 0.9f, 0.9f));
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            EditorUtility.SetDirty(material);
            return material;
        }

        static LootTable MakeYield(string name, LootDrop[] drops)
        {
            string path = $"{YieldDir}/{name}.asset";
            var table = AssetDatabase.LoadAssetAtPath<LootTable>(path);
            if (table == null)
            {
                table = ScriptableObject.CreateInstance<LootTable>();
                AssetDatabase.CreateAsset(table, path);
            }
            var serialized = new SerializedObject(table);
            serialized.FindProperty("source").enumValueIndex = (int)LootSource.Gathering;
            serialized.FindProperty("mob").intValue = (int)MobId.None;
            SerializedProperty list = serialized.FindProperty("drops");
            list.arraySize = drops.Length;
            for (int i = 0; i < drops.Length; i++)
            {
                SerializedProperty d = list.GetArrayElementAtIndex(i);
                d.FindPropertyRelative("Item").intValue = drops[i].Item;
                d.FindPropertyRelative("Chance").floatValue = drops[i].Chance;
                d.FindPropertyRelative("Min").intValue = drops[i].Min;
                d.FindPropertyRelative("Max").intValue = drops[i].Max;
            }
            serialized.FindProperty("rolls").intValue = 0;
            serialized.FindProperty("pool").arraySize = 0;
            serialized.FindProperty("nothingWeight").floatValue = 0f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(table);
            return table;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            int slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
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
