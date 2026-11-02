using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Voxelforge.Gameplay;
using Voxelforge.Voxels;
using static Voxelforge.Editor.LootBuilder;

namespace Voxelforge.Editor
{
    /// <summary>
    /// <i>Voxelforge → Build Crafting</i> (Roadmap 5.8, D-039): makes the bars, cured leather, crafted gear, potions and
    /// elixirs, and the Blacksmithing, Leatherworking and Alchemy recipes, from the numbers below. This is the one place crafted stats, skills and
    /// costs live; re-running it updates the same assets in place. The stand-in models come from
    /// <c>build_crafted()</c> and <c>build_potions()</c> in <c>Tools/Blender/build_loot.py</c> (5.9 replaces the gear art).
    /// Run Build Loot, Build Professions, Build Props &amp; Items and Build Abilities first.
    /// </summary>
    public static class CraftingBuilder
    {
        const string AtlasPath = "Assets/_Voxelforge/Art/Textures/Loot/Loot_CraftAtlas.png";
        const string GlowPath = "Assets/_Voxelforge/Art/Textures/Loot/Loot_CraftGlow.png";
        const string PotionAtlasPath = "Assets/_Voxelforge/Art/Textures/Loot/Loot_PotionAtlas.png";
        const string PotionGlowPath = "Assets/_Voxelforge/Art/Textures/Loot/Loot_PotionGlow.png";
        const string CraftedDir = "Assets/_Voxelforge/Data/Items/Crafted";
        const string RecipeDir = "Assets/_Voxelforge/Data/Items/Recipes";
        const string BookPath = "Assets/_Voxelforge/Data/Items/RecipeBook.asset";

        const ItemRarity Common = ItemRarity.Common;
        const ItemRarity Rare = ItemRarity.Rare;
        const ItemRarity Epic = ItemRarity.Epic;

        // ---- Everything made (model = its FBX in Art/Models/Loot). Rarity is fixed per item; Legendary stays boss-only. ----
        static readonly Item[] Items =
        {
            Ingredient(ItemIds.CopperBar, "CraftBarCopper", "Copper Bar"),
            Ingredient(ItemIds.IronBar, "CraftBarIron", "Iron Bar"),
            Ingredient(ItemIds.ManaforgedBar, "CraftBarManaforged", "Manaforged Bar", Rare),
            Ingredient(ItemIds.CuredLeather, "CraftLeatherCured", "Cured Leather"),
            Ingredient(ItemIds.WolfhideLeather, "CraftLeatherWolfhide", "Wolfhide Leather"),
            Ingredient(ItemIds.StormhideLeather, "CraftLeatherStormhide", "Stormhide Leather", Rare),

            // Plate: high Armor.                                                           Armor Stamina Power
            Gear(ItemIds.CopperHelm, "CraftCopperHead", "Copper Helm", Common, EquipSlot.Head, 4, 1, 0),
            Gear(ItemIds.CopperBreastplate, "CraftCopperChest", "Copper Breastplate", Common, EquipSlot.Chest, 8, 2, 0),
            Gear(ItemIds.CopperLegplates, "CraftCopperLegs", "Copper Legplates", Common, EquipSlot.Legs, 6, 2, 0),
            Gear(ItemIds.CopperSabatons, "CraftCopperFeet", "Copper Sabatons", Common, EquipSlot.Feet, 4, 1, 0),
            //                                                                       Armor Stamina Power  Damage Swing Reach
            Melee(ItemIds.CopperMace, "CraftCopperWeapon", "Copper Mace", Common, 0, 1, 5, 9f, 0.8f, 2.6f),
            Gear(ItemIds.CopperKiteShield, "CraftCopperShield", "Copper Kite Shield", Common, EquipSlot.OffHand, 6, 1, 0),

            Gear(ItemIds.IronHelm, "CraftIronHead", "Iron Helm", Rare, EquipSlot.Head, 7, 2, 1),
            Gear(ItemIds.IronBreastplate, "CraftIronChest", "Iron Breastplate", Rare, EquipSlot.Chest, 14, 4, 1),
            Gear(ItemIds.IronLegplates, "CraftIronLegs", "Iron Legplates", Rare, EquipSlot.Legs, 11, 3, 1),
            Gear(ItemIds.IronSabatons, "CraftIronFeet", "Iron Sabatons", Rare, EquipSlot.Feet, 7, 2, 1),
            Melee(ItemIds.IronLongsword, "CraftIronWeapon", "Iron Longsword", Rare, 0, 2, 9, 11f, 0.7f, 2.7f, flatBlade: true),
            Gear(ItemIds.IronKiteShield, "CraftIronShield", "Iron Kite Shield", Rare, EquipSlot.OffHand, 13, 3, 0),

            Gear(ItemIds.ManaforgedHelm, "CraftManaforgedHead", "Manaforged Helm", Epic, EquipSlot.Head, 11, 5, 4),
            Gear(ItemIds.ManaforgedBreastplate, "CraftManaforgedChest", "Manaforged Breastplate", Epic, EquipSlot.Chest, 22, 8, 4),
            Gear(ItemIds.ManaforgedLegplates, "CraftManaforgedLegs", "Manaforged Legplates", Epic, EquipSlot.Legs, 17, 7, 3),
            Gear(ItemIds.ManaforgedSabatons, "CraftManaforgedFeet", "Manaforged Sabatons", Epic, EquipSlot.Feet, 11, 5, 3),
            Melee(ItemIds.ManaforgedGreatblade, "CraftManaforgedWeapon", "Manaforged Greatblade", Epic, 2, 4, 15, 13f, 0.6f, 2.9f, flatBlade: true),
            Gear(ItemIds.ManaforgedKiteShield, "CraftManaforgedShield", "Manaforged Kite Shield", Epic, EquipSlot.OffHand, 20, 6, 2),

            // Leather: Stamina and Power.
            Gear(ItemIds.CuredCap, "CraftCuredHead", "Cured Leather Cap", Common, EquipSlot.Head, 1, 3, 2),
            Gear(ItemIds.CuredVest, "CraftCuredChest", "Cured Leather Vest", Common, EquipSlot.Chest, 2, 4, 3),
            Gear(ItemIds.CuredBreeches, "CraftCuredLegs", "Cured Leather Breeches", Common, EquipSlot.Legs, 2, 3, 2),
            Gear(ItemIds.CuredBoots, "CraftCuredFeet", "Cured Leather Boots", Common, EquipSlot.Feet, 1, 2, 2),

            Gear(ItemIds.WolfhideHood, "CraftWolfhideHead", "Wolfhide Hood", Rare, EquipSlot.Head, 3, 5, 4),
            Gear(ItemIds.WolfhideJerkin, "CraftWolfhideChest", "Wolfhide Jerkin", Rare, EquipSlot.Chest, 5, 8, 6),
            Gear(ItemIds.WolfhideTrousers, "CraftWolfhideLegs", "Wolfhide Trousers", Rare, EquipSlot.Legs, 4, 6, 5),
            Gear(ItemIds.WolfhideBoots, "CraftWolfhideFeet", "Wolfhide Boots", Rare, EquipSlot.Feet, 3, 4, 4),

            Gear(ItemIds.StormhideCowl, "CraftStormhideHead", "Stormhide Cowl", Epic, EquipSlot.Head, 5, 9, 8),
            Gear(ItemIds.StormhideHarness, "CraftStormhideChest", "Stormhide Harness", Epic, EquipSlot.Chest, 8, 13, 10),
            Gear(ItemIds.StormhideLegguards, "CraftStormhideLegs", "Stormhide Legguards", Epic, EquipSlot.Legs, 7, 11, 9),
            Gear(ItemIds.StormhideStriders, "CraftStormhideFeet", "Stormhide Striders", Epic, EquipSlot.Feet, 5, 8, 7),
        };

        // What each one does lives in AbilityBuilder; these are on the potion atlas.
        static readonly Item[] Drinks =
        {
            Drink(ItemIds.MinorHealingPotion, "PotionHealth1", "Minor Healing Potion", Common, AbilityBuilder.MinorHealingPotionPath),
            Drink(ItemIds.HealingPotion, "PotionHealth2", "Healing Potion", Common, AbilityBuilder.HealingPotionPath),
            Drink(ItemIds.GreaterHealingPotion, "PotionHealth3", "Greater Healing Potion", Rare, AbilityBuilder.GreaterHealingPotionPath),
            Drink(ItemIds.MinorManaPotion, "PotionMana1", "Minor Mana Potion", Common, AbilityBuilder.MinorManaPotionPath),
            Drink(ItemIds.ManaPotion, "PotionMana2", "Mana Potion", Common, AbilityBuilder.ManaPotionPath),
            Drink(ItemIds.GreaterManaPotion, "PotionMana3", "Greater Mana Potion", Rare, AbilityBuilder.GreaterManaPotionPath),
            Drink(ItemIds.ElixirOfIronhide, "ElixirArmor", "Elixir of Ironhide", Rare, AbilityBuilder.ElixirOfIronhidePath),
            Drink(ItemIds.ElixirOfVigor, "ElixirStamina", "Elixir of Vigor", Rare, AbilityBuilder.ElixirOfVigorPath),
            Drink(ItemIds.ElixirOfEmbers, "ElixirPower", "Elixir of Embers", Rare, AbilityBuilder.ElixirOfEmbersPath),
        };

        readonly struct Recipe
        {
            public readonly string Name;
            public readonly ushort Output;
            public readonly CraftStation Station;
            public readonly int Skill;
            public readonly (ushort id, int count)[] Inputs;

            public Recipe(string name, ushort output, CraftStation station, int skill, params (ushort id, int count)[] inputs)
            {
                Name = name;
                Output = output;
                Station = station;
                Skill = skill;
                Inputs = inputs;
            }
        }

        // The Anvil and the bags hold older places in the book (PropsBuilder), so only what's in them is written here.
        const CraftStation Forge = CraftStation.Forge;
        const CraftStation Anvil = CraftStation.Anvil;
        const CraftStation Rack = CraftStation.TanningRack;
        const CraftStation Table = CraftStation.AlchemyTable;
        const ushort Log = BlockIds.Log;

        static readonly Recipe[] OlderPlaces =
        {
            new Recipe("Anvil", BlockIds.Anvil, CraftStation.Workbench, 1, (ItemIds.CopperBar, 6), (Log, 2)),
            new Recipe("WovenSatchel", ItemIds.WovenSatchel, Rack, 10, (ItemIds.CuredLeather, 6)),
            new Recipe("TravelersPack", ItemIds.TravelersPack, Rack, 30, (ItemIds.WovenSatchel, 1), (ItemIds.WolfhideLeather, 8)),
            new Recipe("DelversRucksack", ItemIds.DelversRucksack, Rack, 60, (ItemIds.TravelersPack, 1), (ItemIds.StormhideLeather, 10), (ItemIds.ManaCrystal, 3)),
        };

        // Add new ones at the end: a recipe's place in the book is sent over the network. The window sorts them by skill.
        // A recipe raises the skill for 40 points above its own (ProfessionBook), so each tier reaches the next.
        static readonly Recipe[] Recipes =
        {
            // Smelted at the Forge.
            new Recipe("CopperBar", ItemIds.CopperBar, Forge, 1, (ItemIds.CopperOre, 2)),
            new Recipe("IronBar", ItemIds.IronBar, Forge, 25, (ItemIds.IronOre, 2)),
            new Recipe("ManaforgedBar", ItemIds.ManaforgedBar, Forge, 50, (ItemIds.IronBar, 2), (ItemIds.ManaCrystal, 1)),

            // Hammered at the Anvil.
            new Recipe("CopperSabatons", ItemIds.CopperSabatons, Anvil, 5, (ItemIds.CopperBar, 3)),
            new Recipe("CopperHelm", ItemIds.CopperHelm, Anvil, 10, (ItemIds.CopperBar, 4)),
            new Recipe("CopperMace", ItemIds.CopperMace, Anvil, 12, (ItemIds.CopperBar, 5), (Log, 1)),
            new Recipe("CopperLegplates", ItemIds.CopperLegplates, Anvil, 15, (ItemIds.CopperBar, 5)),
            new Recipe("CopperKiteShield", ItemIds.CopperKiteShield, Anvil, 18, (ItemIds.CopperBar, 5), (Log, 2)),
            new Recipe("CopperBreastplate", ItemIds.CopperBreastplate, Anvil, 20, (ItemIds.CopperBar, 7)),

            new Recipe("IronSabatons", ItemIds.IronSabatons, Anvil, 30, (ItemIds.IronBar, 3)),
            new Recipe("IronHelm", ItemIds.IronHelm, Anvil, 33, (ItemIds.IronBar, 4)),
            new Recipe("IronLongsword", ItemIds.IronLongsword, Anvil, 36, (ItemIds.IronBar, 5), (Log, 1)),
            new Recipe("IronLegplates", ItemIds.IronLegplates, Anvil, 38, (ItemIds.IronBar, 5)),
            new Recipe("IronKiteShield", ItemIds.IronKiteShield, Anvil, 42, (ItemIds.IronBar, 5), (Log, 2)),
            new Recipe("IronBreastplate", ItemIds.IronBreastplate, Anvil, 45, (ItemIds.IronBar, 7)),

            new Recipe("ManaforgedSabatons", ItemIds.ManaforgedSabatons, Anvil, 55, (ItemIds.ManaforgedBar, 3)),
            new Recipe("ManaforgedHelm", ItemIds.ManaforgedHelm, Anvil, 60, (ItemIds.ManaforgedBar, 4)),
            new Recipe("ManaforgedGreatblade", ItemIds.ManaforgedGreatblade, Anvil, 65, (ItemIds.ManaforgedBar, 6), (ItemIds.ArcaneEssence, 1)),
            new Recipe("ManaforgedLegplates", ItemIds.ManaforgedLegplates, Anvil, 70, (ItemIds.ManaforgedBar, 5)),
            new Recipe("ManaforgedKiteShield", ItemIds.ManaforgedKiteShield, Anvil, 75, (ItemIds.ManaforgedBar, 5), (ItemIds.EarthEssence, 1)),
            new Recipe("ManaforgedBreastplate", ItemIds.ManaforgedBreastplate, Anvil, 80, (ItemIds.ManaforgedBar, 7), (ItemIds.StormEssence, 1)),

            // Cured and stitched at the Tanning Rack. Each hide tier is gated about as Skinning gates its mob.
            new Recipe("CuredLeather", ItemIds.CuredLeather, Rack, 1, (ItemIds.BoarHide, 2)),
            new Recipe("WolfhideLeather", ItemIds.WolfhideLeather, Rack, 25, (ItemIds.WolfPelt, 2)),
            new Recipe("StormhideLeather", ItemIds.StormhideLeather, Rack, 50, (ItemIds.RhinoHide, 2)),

            new Recipe("CuredBoots", ItemIds.CuredBoots, Rack, 5, (ItemIds.CuredLeather, 2)),
            new Recipe("CuredCap", ItemIds.CuredCap, Rack, 8, (ItemIds.CuredLeather, 3)),
            new Recipe("CuredBreeches", ItemIds.CuredBreeches, Rack, 12, (ItemIds.CuredLeather, 4)),
            new Recipe("CuredVest", ItemIds.CuredVest, Rack, 16, (ItemIds.CuredLeather, 5)),

            new Recipe("WolfhideBoots", ItemIds.WolfhideBoots, Rack, 30, (ItemIds.WolfhideLeather, 2)),
            new Recipe("WolfhideHood", ItemIds.WolfhideHood, Rack, 33, (ItemIds.WolfhideLeather, 3), (ItemIds.Frostfang, 1)),
            new Recipe("WolfhideTrousers", ItemIds.WolfhideTrousers, Rack, 38, (ItemIds.WolfhideLeather, 4)),
            new Recipe("WolfhideJerkin", ItemIds.WolfhideJerkin, Rack, 42, (ItemIds.WolfhideLeather, 5)),

            new Recipe("StormhideStriders", ItemIds.StormhideStriders, Rack, 55, (ItemIds.StormhideLeather, 3)),
            new Recipe("StormhideCowl", ItemIds.StormhideCowl, Rack, 60, (ItemIds.StormhideLeather, 4), (ItemIds.ThunderHorn, 1)),
            new Recipe("StormhideLegguards", ItemIds.StormhideLegguards, Rack, 68, (ItemIds.StormhideLeather, 5)),
            new Recipe("StormhideHarness", ItemIds.StormhideHarness, Rack, 75, (ItemIds.StormhideLeather, 7), (ItemIds.StormEssence, 1)),

            // Brewed at the Alchemy Table (steps E and F). Each herb tier is gated as Herbalism gates picking it.
            new Recipe("MinorHealingPotion", ItemIds.MinorHealingPotion, Table, 1, (ItemIds.Sunpetal, 2)),
            new Recipe("MinorManaPotion", ItemIds.MinorManaPotion, Table, 8, (ItemIds.Sunpetal, 2), (ItemIds.CrystalShard, 1)),
            new Recipe("HealingPotion", ItemIds.HealingPotion, Table, 25, (ItemIds.Frostbloom, 2), (ItemIds.Sunpetal, 1)),
            new Recipe("ManaPotion", ItemIds.ManaPotion, Table, 30, (ItemIds.Frostbloom, 2), (ItemIds.CrystalShard, 1)),
            new Recipe("ElixirOfIronhide", ItemIds.ElixirOfIronhide, Table, 35, (ItemIds.Frostbloom, 2), (ItemIds.BoarTusk, 1), (ItemIds.EarthEssence, 1)),
            new Recipe("ElixirOfVigor", ItemIds.ElixirOfVigor, Table, 42, (ItemIds.Frostbloom, 3), (ItemIds.FrostEssence, 1)),
            new Recipe("GreaterHealingPotion", ItemIds.GreaterHealingPotion, Table, 50, (ItemIds.Emberthorn, 2), (ItemIds.Frostbloom, 1)),
            new Recipe("GreaterManaPotion", ItemIds.GreaterManaPotion, Table, 55, (ItemIds.Emberthorn, 2), (ItemIds.ManaCrystal, 1)),
            new Recipe("ElixirOfEmbers", ItemIds.ElixirOfEmbers, Table, 65, (ItemIds.Emberthorn, 3), (ItemIds.ArcaneEssence, 1)),
        };

        [MenuItem("Voxelforge/Build Crafting")]
        public static void Build()
        {
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
            var glow = AssetDatabase.LoadAssetAtPath<Texture2D>(GlowPath);
            var registry = AssetDatabase.LoadAssetAtPath<ItemRegistry>(RegistryPath);
            var blocks = AssetDatabase.LoadAssetAtPath<BlockRegistry>(BlockRegistryPath);
            var book = AssetDatabase.LoadAssetAtPath<RecipeBook>(BookPath);
            if (atlas == null || glow == null || registry == null || blocks == null || book == null)
            {
                Debug.LogError($"[Voxelforge] Missing {AtlasPath} or a registry. Run build_crafted() in Tools/Blender/build_loot.py " +
                               "and Build Props & Items first.");
                return;
            }
            EnsureFolder(CraftedDir);
            Material held = MakeHeldMaterial(atlas, glow, "Loot_CraftHeld");

            var made = new List<Object>();
            foreach (Item item in Items)
            {
                ItemDefinition definition = MakeItem(item, held, CraftedDir);
                if (definition == null)
                    return;
                made.Add(definition);
            }
            var potionAtlas = AssetDatabase.LoadAssetAtPath<Texture2D>(PotionAtlasPath);
            var potionGlow = AssetDatabase.LoadAssetAtPath<Texture2D>(PotionGlowPath);
            if (potionAtlas == null || potionGlow == null)
            {
                Debug.LogError($"[Voxelforge] Missing {PotionAtlasPath}. Run build_potions() in Tools/Blender/build_loot.py first.");
                return;
            }
            Material potionHeld = MakeHeldMaterial(potionAtlas, potionGlow, "Loot_PotionHeld");
            foreach (Item item in Drinks)
            {
                ItemDefinition definition = MakeItem(item, potionHeld, CraftedDir);
                if (definition == null)
                    return;
                made.Add(definition);
            }
            AddToList(registry, "items", made);

            var problems = new List<string>();
            foreach (Recipe recipe in OlderPlaces)
                MakeRecipe(recipe, registry, blocks, problems);
            var recipes = new List<Object>();
            foreach (Recipe recipe in Recipes)
                recipes.Add(MakeRecipe(recipe, registry, blocks, problems));
            AddToList(book, "recipes", recipes);
            AssetDatabase.SaveAssets();

            problems.AddRange(registry.Validate(blocks));
            problems.AddRange(book.Validate());
            if (problems.Count > 0)
                Debug.LogError("[Voxelforge] Build Crafting made assets with problems:\n" + string.Join("\n", problems));
            else
                Debug.Log($"[Voxelforge] Built {Items.Length + Drinks.Length} crafted items and {OlderPlaces.Length + Recipes.Length} recipes.", book);
        }

        static RecipeDefinition MakeRecipe(in Recipe recipe, ItemRegistry items, BlockRegistry blocks, List<string> problems)
        {
            var definition = LoadOrCreate<RecipeDefinition>($"{RecipeDir}/{recipe.Name}.asset");
            var serialized = new SerializedObject(definition);
            WriteAmount(serialized.FindProperty("output"), recipe.Name, recipe.Output, 1, items, blocks, problems);
            serialized.FindProperty("station").enumValueIndex = (int)recipe.Station;
            serialized.FindProperty("requiredSkill").intValue = recipe.Skill;
            SerializedProperty list = serialized.FindProperty("inputs");
            list.arraySize = recipe.Inputs.Length;
            for (int i = 0; i < recipe.Inputs.Length; i++)
                WriteAmount(list.GetArrayElementAtIndex(i), recipe.Name, recipe.Inputs[i].id, recipe.Inputs[i].count, items, blocks, problems);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            return definition;
        }

        static void WriteAmount(SerializedProperty property, string recipe, ushort id, int count, ItemRegistry items, BlockRegistry blocks,
            List<string> problems)
        {
            // Searched in the lists, not with TryGet: the lookups may be from before this build added to them.
            Object block = null;
            Object item = null;
            if (ItemIds.IsBlock(id))
            {
                foreach (BlockDefinition candidate in blocks.Blocks)
                {
                    if (candidate != null && candidate.Id == id)
                        block = candidate;
                }
            }
            else
            {
                foreach (ItemDefinition candidate in items.Items)
                {
                    if (candidate != null && candidate.Id == id)
                        item = candidate;
                }
            }
            if (block == null && item == null)
                problems.Add($"Recipe {recipe}: nothing has the ID {id}.");
            property.FindPropertyRelative("block").objectReferenceValue = block;
            property.FindPropertyRelative("item").objectReferenceValue = item;
            property.FindPropertyRelative("count").intValue = count;
        }
    }
}
