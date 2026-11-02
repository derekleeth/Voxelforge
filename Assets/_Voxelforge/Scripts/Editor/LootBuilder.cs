using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Voxelforge.Gameplay;
using Voxelforge.Voxels;

namespace Voxelforge.Editor
{
    /// <summary>
    /// <i>Voxelforge → Build Loot</i> (Roadmap 5.5, D-036): makes every crafting material and piece of gear, and every
    /// mob's and the ruin cache's <see cref="LootTable"/>, from the numbers below. This is the one place drop chances,
    /// rarities and stats live; re-running it updates the same assets in place. The stand-in models come from
    /// <c>Tools/Blender/build_loot.py</c> (5.9 replaces the gear art).
    /// </summary>
    public static class LootBuilder
    {
        const string ModelDir = "Assets/_Voxelforge/Art/Models/Loot";
        const string AtlasPath = "Assets/_Voxelforge/Art/Textures/Loot/Loot_Atlas.png";
        const string GlowPath = "Assets/_Voxelforge/Art/Textures/Loot/Loot_Glow.png";
        // Roadmap 5.7: the ore items have their own atlas (build_ores() in build_loot.py).
        const string OreAtlasPath = "Assets/_Voxelforge/Art/Textures/Loot/Loot_OreAtlas.png";
        const string OreGlowPath = "Assets/_Voxelforge/Art/Textures/Loot/Loot_OreGlow.png";
        const string MaterialDir = "Assets/_Voxelforge/Art/Materials/Loot";
        internal const string ItemDir = "Assets/_Voxelforge/Data/Items/Loot";
        const string LootDir = "Assets/_Voxelforge/Data/Loot";
        internal const string RegistryPath = "Assets/_Voxelforge/Data/Items/ItemRegistry.asset";
        internal const string BlockRegistryPath = "Assets/_Voxelforge/Data/Blocks/BlockRegistry.asset";
        internal const string BookPath = LootDir + "/LootBook.asset";

        internal enum Hold { Upright, Weapon }

        internal readonly struct Item
        {
            public readonly ushort Id;
            public readonly string Model;
            public readonly string Name;
            public readonly ItemCategory Category;
            public readonly ItemRarity Rarity;
            public readonly EquipSlot Slot;
            public readonly GearStats Stats;
            public readonly Hold Hold;
            public readonly WeaponStats Weapon;
            /// <summary>The ability a ranged weapon shoots (the wand), as its asset path; null for everything else.</summary>
            public readonly string Shoots;

            public Item(ushort id, string model, string name, ItemCategory category, ItemRarity rarity, EquipSlot slot = EquipSlot.None,
                GearStats stats = default, Hold hold = Hold.Upright, WeaponStats weapon = default, string shoots = null)
            {
                Weapon = weapon;
                Shoots = shoots;
                Id = id;
                Model = model;
                Name = name;
                Category = category;
                Rarity = rarity;
                Slot = slot;
                Stats = stats;
                Hold = hold;
            }
        }

        internal static Item Ingredient(ushort id, string model, string name, ItemRarity rarity = ItemRarity.Common) =>
            new Item(id, model, name, ItemCategory.Materials, rarity);

        internal static Item Gear(ushort id, string model, string name, ItemRarity rarity, EquipSlot slot, int armor, int stamina, int power,
            Hold hold = Hold.Upright) =>
            new Item(id, model, name, ItemCategory.Gear, rarity, slot, new GearStats(armor, stamina, power), hold);

        // Main Hand weapons (step G): melee ones swing for damage every swingSeconds within reach; the wand shoots.
        internal static Item Melee(ushort id, string model, string name, ItemRarity rarity, int armor, int stamina, int power,
            float damage, float swingSeconds, float reach, bool flatBlade = false) =>
            new Item(id, model, name, ItemCategory.Gear, rarity, EquipSlot.MainHand, new GearStats(armor, stamina, power), Hold.Weapon,
                new WeaponStats(damage, swingSeconds, reach, flatBlade));

        static Item Ranged(ushort id, string model, string name, ItemRarity rarity, int armor, int stamina, int power, string ability) =>
            new Item(id, model, name, ItemCategory.Gear, rarity, EquipSlot.MainHand, new GearStats(armor, stamina, power), Hold.Weapon,
                shoots: ability);

        // ---- Every material and piece of gear (model = its FBX in Art/Models/Loot) ----
        static readonly Item[] Items =
        {
            // Mob materials (5.8's Leatherworking and Alchemy use them). Essences are rarer, so they're blue.
            Ingredient(ItemIds.BoarHide, "LootHideBoar", "Boar Hide"),
            Ingredient(ItemIds.BoarTusk, "LootTusk", "Boar Tusk"),
            Ingredient(ItemIds.WolfPelt, "LootPelt", "Wolf Pelt"),
            Ingredient(ItemIds.Frostfang, "LootFang", "Frostfang"),
            Ingredient(ItemIds.CrystalShard, "LootShard", "Crystal Shard"),
            Ingredient(ItemIds.RhinoHide, "LootHideRhino", "Rhino Hide"),
            Ingredient(ItemIds.ThunderHorn, "LootHorn", "Thunder Horn"),
            Ingredient(ItemIds.EarthEssence, "LootEssenceEarth", "Earth Essence", ItemRarity.Rare),
            Ingredient(ItemIds.FrostEssence, "LootEssenceFrost", "Frost Essence", ItemRarity.Rare),
            Ingredient(ItemIds.StormEssence, "LootEssenceStorm", "Storm Essence", ItemRarity.Rare),
            Ingredient(ItemIds.ArcaneEssence, "LootEssenceArcane", "Arcane Essence", ItemRarity.Rare),

            // Common: bristly boar leather and plain wood and iron.         Armor Stamina Power
            Gear(ItemIds.BristlehideCap, "LootCap", "Bristlehide Cap", ItemRarity.Common, EquipSlot.Head, 3, 2, 0),
            Gear(ItemIds.BristlehideJerkin, "LootJerkin", "Bristlehide Jerkin", ItemRarity.Common, EquipSlot.Chest, 6, 3, 0),
            Gear(ItemIds.BristlehideLeggings, "LootLeggings", "Bristlehide Leggings", ItemRarity.Common, EquipSlot.Legs, 5, 2, 0),
            Gear(ItemIds.TrailwornBoots, "LootBoots", "Trailworn Boots", ItemRarity.Common, EquipSlot.Feet, 3, 1, 0),
            //                                                                                        Armor Stamina Power  Damage Swing Reach
            Melee(ItemIds.IronboundCudgel, "LootCudgel", "Ironbound Cudgel", ItemRarity.Common, 0, 1, 4, 8f, 0.8f, 2.6f),
            Gear(ItemIds.OakBuckler, "LootBuckler", "Oak Buckler", ItemRarity.Common, EquipSlot.OffHand, 5, 1, 0),

            // Rare: one piece themed on each mob.
            Gear(ItemIds.FrostfangHood, "LootHood", "Frostfang Hood", ItemRarity.Rare, EquipSlot.Head, 5, 3, 3),
            Gear(ItemIds.TuskhideJerkin, "LootJerkinTusk", "Tuskhide Jerkin", ItemRarity.Rare, EquipSlot.Chest, 11, 6, 0),
            Gear(ItemIds.RhinohideGreaves, "LootGreaves", "Rhinohide Greaves", ItemRarity.Rare, EquipSlot.Legs, 9, 5, 0),
            Gear(ItemIds.WindrunnerBoots, "LootBootsWind", "Windrunner Boots", ItemRarity.Rare, EquipSlot.Feet, 5, 3, 2),
            // Shoots Shard Bolts (9 damage every 0.7 s, 22 m; AbilityBuilder).
            Ranged(ItemIds.ShardglassWand, "LootWand", "Shardglass Wand", ItemRarity.Rare, 0, 2, 10, AbilityBuilder.ShardBoltPath),
            Gear(ItemIds.ThunderhornShield, "LootShield", "Thunderhorn Shield", ItemRarity.Rare, EquipSlot.OffHand, 11, 4, 0),

            // Epic.
            Gear(ItemIds.CrystalDeepCrown, "LootCrown", "Crown of the Crystal Deep", ItemRarity.Epic, EquipSlot.Head, 8, 7, 8),
            Gear(ItemIds.StormhideHauberk, "LootHauberk", "Stormhide Hauberk", ItemRarity.Epic, EquipSlot.Chest, 18, 10, 4),
            Melee(ItemIds.GlacierheartBlade, "LootBlade", "Glacierheart Blade", ItemRarity.Epic, 2, 5, 16, 13f, 0.6f, 2.8f, flatBlade: true),

            // Legendary.
            // Slow and huge.
            Melee(ItemIds.Skyrender, "LootMaul", "Skyrender, Maul of the High Mesa", ItemRarity.Legendary, 6, 12, 26, 26f, 1.2f, 3f),
        };

        // Ore (Roadmap 5.7): what the ore blocks drop, on the ore atlas. Mana Crystal is rarer and deeper, so blue.
        static readonly Item[] Ores =
        {
            Ingredient(ItemIds.CopperOre, "LootOreCopper", "Copper Ore"),
            Ingredient(ItemIds.IronOre, "LootOreIron", "Iron Ore"),
            Ingredient(ItemIds.ManaCrystal, "LootManaCrystal", "Mana Crystal", ItemRarity.Rare),
        };

        // Every Common piece, which most tables share at the same weight.
        static readonly ushort[] Commons =
        {
            ItemIds.BristlehideCap, ItemIds.BristlehideJerkin, ItemIds.BristlehideLeggings, ItemIds.TrailwornBoots,
            ItemIds.IronboundCudgel, ItemIds.OakBuckler,
        };

        static LootEntry[] Pool(float commonWeight, params LootEntry[] others)
        {
            var pool = new List<LootEntry>();
            foreach (ushort common in Commons)
                pool.Add(new LootEntry(common, commonWeight));
            pool.AddRange(others);
            return pool.ToArray();
        }

        // ---- Loot tables. Each hero who fought the mob rolls the whole table for themselves. ----
        // A pool roll gives gear with chance (sum of weights) / (sum + nothing). Generous for the first play-test.
        static void WriteTables(List<LootTable> into)
        {
            // Meadows. Gear ~31% a kill.
            into.Add(MakeTable("Boar", LootSource.Mob, MobId.BristlebackBoar,
                new[] { new LootDrop(ItemIds.BoarHide, 0.8f, 1, 2), new LootDrop(ItemIds.BoarTusk, 0.4f), new LootDrop(ItemIds.EarthEssence, 0.08f) },
                1, Pool(4f, new LootEntry(ItemIds.TuskhideJerkin, 2f), new LootEntry(ItemIds.WindrunnerBoots, 1.5f)), 60f));

            // Snowy Peaks. Packs, so a little less each. Gear ~29% a kill.
            into.Add(MakeTable("Wolf", LootSource.Mob, MobId.FrostfangWolf,
                new[] { new LootDrop(ItemIds.WolfPelt, 0.8f, 1, 2), new LootDrop(ItemIds.Frostfang, 0.5f), new LootDrop(ItemIds.FrostEssence, 0.1f) },
                1, Pool(3f, new LootEntry(ItemIds.FrostfangHood, 2.5f), new LootEntry(ItemIds.WindrunnerBoots, 1.5f),
                    new LootEntry(ItemIds.GlacierheartBlade, 0.6f)), 55f));

            // Thunderhoof Plains. The toughest overland mob, and the only one that can drop Skyrender. Gear ~36% a kill.
            into.Add(MakeTable("Rhino", LootSource.Mob, MobId.ThunderRhino,
                new[] { new LootDrop(ItemIds.RhinoHide, 0.9f, 1, 3), new LootDrop(ItemIds.ThunderHorn, 0.5f), new LootDrop(ItemIds.StormEssence, 0.12f) },
                1, Pool(3f, new LootEntry(ItemIds.RhinohideGreaves, 3f), new LootEntry(ItemIds.ThunderhornShield, 3f),
                    new LootEntry(ItemIds.StormhideHauberk, 1f), new LootEntry(ItemIds.Skyrender, 0.25f)), 45f));

            // Caves. A large slime splits into 2 medium and 4 small, so the pieces drop less each.
            into.Add(MakeTable("SlimeLarge", LootSource.Mob, MobId.CrystalSlime,
                new[] { new LootDrop(ItemIds.CrystalShard, 1f, 1, 2), new LootDrop(ItemIds.ArcaneEssence, 0.15f) },
                1, Pool(2f, new LootEntry(ItemIds.ShardglassWand, 2f), new LootEntry(ItemIds.CrystalDeepCrown, 0.6f)), 50f));
            into.Add(MakeTable("SlimeMedium", LootSource.Mob, MobId.CrystalSlimeMedium,
                new[] { new LootDrop(ItemIds.CrystalShard, 0.6f), new LootDrop(ItemIds.ArcaneEssence, 0.05f) },
                1, Pool(1f, new LootEntry(ItemIds.ShardglassWand, 0.5f)), 60f));
            into.Add(MakeTable("SlimeSmall", LootSource.Mob, MobId.CrystalSlimeSmall,
                new[] { new LootDrop(ItemIds.CrystalShard, 0.4f) }, 0, System.Array.Empty<LootEntry>(), 0f));

            // A ruin's chest (5.4), once per hero per chest: two good rolls and a little of every essence.
            into.Add(MakeTable("RuinCache", LootSource.RuinCache, MobId.None,
                new[]
                {
                    new LootDrop(ItemIds.EarthEssence, 0.3f, 1, 2), new LootDrop(ItemIds.FrostEssence, 0.3f, 1, 2),
                    new LootDrop(ItemIds.StormEssence, 0.3f, 1, 2), new LootDrop(ItemIds.ArcaneEssence, 0.3f, 1, 2),
                },
                2, Pool(6f,
                    new LootEntry(ItemIds.FrostfangHood, 3f), new LootEntry(ItemIds.TuskhideJerkin, 3f),
                    new LootEntry(ItemIds.RhinohideGreaves, 3f), new LootEntry(ItemIds.WindrunnerBoots, 3f),
                    new LootEntry(ItemIds.ShardglassWand, 3f), new LootEntry(ItemIds.ThunderhornShield, 3f),
                    new LootEntry(ItemIds.CrystalDeepCrown, 1f), new LootEntry(ItemIds.StormhideHauberk, 1f),
                    new LootEntry(ItemIds.GlacierheartBlade, 1f), new LootEntry(ItemIds.Skyrender, 0.3f)), 10f));
        }

        [MenuItem("Voxelforge/Build Loot")]
        public static void Build()
        {
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
            var glow = AssetDatabase.LoadAssetAtPath<Texture2D>(GlowPath);
            var oreAtlas = AssetDatabase.LoadAssetAtPath<Texture2D>(OreAtlasPath);
            var oreGlow = AssetDatabase.LoadAssetAtPath<Texture2D>(OreGlowPath);
            var registry = AssetDatabase.LoadAssetAtPath<ItemRegistry>(RegistryPath);
            if (atlas == null || glow == null || oreAtlas == null || oreGlow == null || registry == null)
            {
                Debug.LogError($"[Voxelforge] Missing {AtlasPath}, {GlowPath} or {RegistryPath}. Run build_all() in " +
                               "Tools/Blender/build_loot.py and Build Props & Items first.");
                return;
            }
            EnsureFolder(MaterialDir);
            EnsureFolder(ItemDir);
            EnsureFolder(LootDir);
            Material held = MakeHeldMaterial(atlas, glow, "Loot_Held");
            Material oreHeld = MakeHeldMaterial(oreAtlas, oreGlow, "Loot_OreHeld");

            var made = new List<Object>();
            foreach (Item item in Items)
            {
                ItemDefinition definition = MakeItem(item, held);
                if (definition == null)
                    return;
                made.Add(definition);
            }
            foreach (Item item in Ores)
            {
                ItemDefinition definition = MakeItem(item, oreHeld);
                if (definition == null)
                    return;
                made.Add(definition);
            }
            AddToList(registry, "items", made);

            var tables = new List<LootTable>();
            WriteTables(tables);
            var book = LoadOrCreate<LootBook>(BookPath);
            var serialized = new SerializedObject(book);
            SerializedProperty list = serialized.FindProperty("tables");
            list.arraySize = tables.Count;
            for (int i = 0; i < tables.Count; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = tables[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(book);
            AssetDatabase.SaveAssets();

            var problems = new List<string>(registry.Validate(AssetDatabase.LoadAssetAtPath<BlockRegistry>(BlockRegistryPath)));
            problems.AddRange(book.Validate(registry, AssetDatabase.LoadAssetAtPath<BlockRegistry>(BlockRegistryPath)));
            if (problems.Count > 0)
                Debug.LogError("[Voxelforge] Build Loot made assets with problems:\n" + string.Join("\n", problems));
            else
                Debug.Log($"[Voxelforge] Built {Items.Length + Ores.Length} loot items and {tables.Count} loot tables.");
        }

        internal static ItemDefinition MakeItem(Item item, Material material, string dir = ItemDir)
        {
            Mesh mesh = LoadMesh(item.Model);
            if (mesh == null)
                return null;
            var definition = LoadOrCreate<ItemDefinition>($"{dir}/{item.Model}.asset");
            var serialized = new SerializedObject(definition);
            serialized.FindProperty("id").intValue = item.Id;
            serialized.FindProperty("displayName").stringValue = item.Name;
            serialized.FindProperty("category").enumValueIndex = (int)item.Category;
            serialized.FindProperty("maxStack").intValue = 0; // The category's usual: 999 materials, 1 gear.
            serialized.FindProperty("bagSlots").intValue = 0;
            serialized.FindProperty("rarity").enumValueIndex = (int)item.Rarity;
            serialized.FindProperty("equipSlot").enumValueIndex = (int)item.Slot;
            SerializedProperty stats = serialized.FindProperty("stats");
            stats.FindPropertyRelative("armor").intValue = item.Stats.armor;
            stats.FindPropertyRelative("stamina").intValue = item.Stats.stamina;
            stats.FindPropertyRelative("power").intValue = item.Stats.power;
            SerializedProperty weapon = serialized.FindProperty("weapon");
            weapon.FindPropertyRelative("damage").floatValue = item.Weapon.damage;
            weapon.FindPropertyRelative("swingSeconds").floatValue = Mathf.Max(0.1f, item.Weapon.swingSeconds);
            weapon.FindPropertyRelative("reach").floatValue = Mathf.Max(0.5f, item.Weapon.reach);
            weapon.FindPropertyRelative("flatBlade").boolValue = item.Weapon.flatBlade;
            serialized.FindProperty("ability").objectReferenceValue =
                item.Shoots != null ? AssetDatabase.LoadAssetAtPath<AbilityDefinition>(item.Shoots) : null;
            serialized.FindProperty("mesh").objectReferenceValue = mesh;
            // Legs and feet are worn as one piece per leg (step E): build_loot.py makes "<model>Worn".
            serialized.FindProperty("wornMesh").objectReferenceValue =
                item.Slot == EquipSlot.Legs || item.Slot == EquipSlot.Feet ? LoadMesh(item.Model + "Worn") : null;
            serialized.FindProperty("material").objectReferenceValue = material;
            serialized.FindProperty("heldPosition").vector3Value = PropsBuilder.Grip;
            serialized.FindProperty("heldRotation").vector3Value =
                item.Hold == Hold.Weapon ? PropsBuilder.ToolPose : PropsBuilder.UprightPose;
            serialized.FindProperty("heldScale").floatValue = item.Hold == Hold.Weapon ? 1f : 0.6f;
            serialized.FindProperty("dropSize").floatValue = 1.5f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }

        static LootTable MakeTable(string name, LootSource source, MobId mob, LootDrop[] drops, int rolls, LootEntry[] pool,
            float nothingWeight)
        {
            var table = LoadOrCreate<LootTable>($"{LootDir}/{name}.asset");
            var serialized = new SerializedObject(table);
            serialized.FindProperty("source").enumValueIndex = (int)source;
            serialized.FindProperty("mob").intValue = (int)mob;
            SerializedProperty dropList = serialized.FindProperty("drops");
            dropList.arraySize = drops.Length;
            for (int i = 0; i < drops.Length; i++)
            {
                SerializedProperty d = dropList.GetArrayElementAtIndex(i);
                d.FindPropertyRelative("Item").intValue = drops[i].Item;
                d.FindPropertyRelative("Chance").floatValue = drops[i].Chance;
                d.FindPropertyRelative("Min").intValue = drops[i].Min;
                d.FindPropertyRelative("Max").intValue = drops[i].Max;
            }
            serialized.FindProperty("rolls").intValue = rolls;
            SerializedProperty poolList = serialized.FindProperty("pool");
            poolList.arraySize = pool.Length;
            for (int i = 0; i < pool.Length; i++)
            {
                SerializedProperty e = poolList.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("Item").intValue = pool[i].Item;
                e.FindPropertyRelative("Weight").floatValue = pool[i].Weight;
                e.FindPropertyRelative("Min").intValue = pool[i].Min;
                e.FindPropertyRelative("Max").intValue = pool[i].Max;
            }
            serialized.FindProperty("nothingWeight").floatValue = nothingWeight;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(table);
            return table;
        }

        internal static Material MakeHeldMaterial(Texture2D atlas, Texture2D glow, string name)
        {
            string path = $"{MaterialDir}/{name}.mat";
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetTexture("_BaseMap", atlas);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0.35f);
            material.SetTexture("_EmissionMap", glow);
            material.SetColor("_EmissionColor", PropsBuilder.HeldGlow);
            material.EnableKeyword("_EMISSION");
            // As Props_Held: not None, or URP strips _EMISSION and the essences stop glowing.
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(material);
            return material;
        }

        static Mesh LoadMesh(string model)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath($"{ModelDir}/{model}.fbx"))
            {
                if (asset is Mesh mesh && mesh.name == model)
                    return mesh;
            }
            Debug.LogError($"[Voxelforge] {ModelDir}/{model}.fbx needs a mesh called '{model}'. Run build_loot.py first.");
            return null;
        }

        internal static void AddToList(Object owner, string field, List<Object> entries)
        {
            var serialized = new SerializedObject(owner);
            SerializedProperty list = serialized.FindProperty(field);
            foreach (Object entry in entries)
            {
                bool present = false;
                for (int i = 0; i < list.arraySize; i++)
                    present |= list.GetArrayElementAtIndex(i).objectReferenceValue == entry;
                if (present)
                    continue;
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = entry;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(owner);
        }

        internal static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
            }
            return asset;
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            int slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
