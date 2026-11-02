using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using Voxelforge.Gameplay;
using Voxelforge.Voxels;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 5.8 step A: stations, the skill a recipe needs and how crafting raises it, without any networking.</summary>
    public class CraftingProfessionTests
    {
        readonly List<Object> _made = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object made in _made)
                Object.DestroyImmediate(made);
            _made.Clear();
        }

        T Keep<T>(T made) where T : Object
        {
            _made.Add(made);
            return made;
        }

        RecipeBook OneRecipeBook(CraftStation station, int skill)
        {
            ItemDefinition ore = Keep(ItemDefinition.Create(ItemIds.CopperOre, "Copper Ore", category: ItemCategory.Materials));
            ItemDefinition bar = Keep(ItemDefinition.Create(1320, "Copper Bar", category: ItemCategory.Materials));
            RecipeDefinition recipe = Keep(RecipeDefinition.Create(new ItemAmount(bar, 1), station, skill, new ItemAmount(ore, 2)));
            return Keep(RecipeBook.Create(new[] { recipe }));
        }

        static List<ItemStack> TwoOre() => new List<ItemStack> { new ItemStack(ItemIds.CopperOre, 2) };

        // ---- Skills ----

        [Test]
        public void Skills_CraftingStartsAtOne_AndAZeroedSkillCountsAsOne()
        {
            for (int i = 0; i < ProfessionSkills.Count; i++)
            {
                Assert.AreEqual(1, ProfessionSkills.Start.Get((ProfessionId)i));
                Assert.AreEqual(1, default(ProfessionSkills).Get((ProfessionId)i));
            }
        }

        [Test]
        public void Skills_With_ChangesOnlyThatProfession_AndEqualsSeesCraftingSkills()
        {
            ProfessionSkills skills = ProfessionSkills.Start.With(ProfessionId.Blacksmithing, 40);
            Assert.AreEqual(40, skills.Get(ProfessionId.Blacksmithing));
            Assert.AreEqual(1, skills.Get(ProfessionId.Alchemy));
            Assert.AreEqual(1, skills.Get(ProfessionId.Leatherworking));
            Assert.AreEqual(1, skills.Get(ProfessionId.Mining));
            Assert.AreNotEqual(ProfessionSkills.Start, skills);
        }

        // ---- Stations ----

        [TestCase(CraftStation.AlchemyTable, ProfessionId.Alchemy)]
        [TestCase(CraftStation.Forge, ProfessionId.Blacksmithing)]
        [TestCase(CraftStation.Anvil, ProfessionId.Blacksmithing)]
        [TestCase(CraftStation.TanningRack, ProfessionId.Leatherworking)]
        public void Stations_PickTheirProfession(CraftStation station, ProfessionId profession)
        {
            Assert.IsTrue(CraftStations.TryGetProfession(station, out ProfessionId got));
            Assert.AreEqual(profession, got);
        }

        [TestCase(CraftStation.Anywhere)]
        [TestCase(CraftStation.Workbench)]
        public void GeneralStations_BelongToNoProfession(CraftStation station) =>
            Assert.IsFalse(CraftStations.TryGetProfession(station, out _));

        [Test]
        public void Stations_HaveTheirOwnBlocks_AndAnywhereIsAlwaysNear()
        {
            Assert.AreEqual(BlockIds.Air, CraftStations.BlockOf(CraftStation.Anywhere));
            Assert.AreEqual(BlockIds.Forge, CraftStations.BlockOf(CraftStation.Forge));
            Assert.AreEqual(BlockIds.TanningRack, CraftStations.BlockOf(CraftStation.TanningRack));
            Assert.IsTrue(CraftStations.IsNear(CraftStationMask.None, CraftStation.Anywhere));
            Assert.IsTrue(CraftStations.IsNear(CraftStationMask.Forge | CraftStationMask.Anvil, CraftStation.Anvil));
            Assert.IsFalse(CraftStations.IsNear(CraftStationMask.Forge, CraftStation.Anvil), "The Forge smelts, the Anvil smiths.");
        }

        [TestCase(CraftStation.Anvil, "an Anvil")]
        [TestCase(CraftStation.AlchemyTable, "an Alchemy Table")]
        [TestCase(CraftStation.Forge, "a Forge")]
        public void Stations_ReadWell(CraftStation station, string expected) =>
            Assert.AreEqual(expected, CraftStations.WithArticle(station));

        // ---- Checking a recipe ----

        [Test]
        public void Check_NeedsTheProfessionsSkill()
        {
            RecipeBook book = OneRecipeBook(CraftStation.Forge, 25);
            ProfessionSkills skills = ProfessionSkills.Start.With(ProfessionId.Blacksmithing, 24);
            Assert.AreEqual(CraftCheck.NeedsSkill, book.Check(0, TwoOre(), CraftStationMask.Forge, skills));
            Assert.AreEqual(CraftCheck.Ok, book.Check(0, TwoOre(), CraftStationMask.Forge, skills.With(ProfessionId.Blacksmithing, 25)));
            Assert.AreEqual(CraftCheck.NeedsSkill, book.Check(0, TwoOre(), CraftStationMask.Forge,
                ProfessionSkills.Start.With(ProfessionId.Alchemy, 100)), "Another profession's skill doesn't help.");
        }

        [Test]
        public void Check_NeedsTheRightStationInReach()
        {
            RecipeBook book = OneRecipeBook(CraftStation.Anvil, 1);
            Assert.AreEqual(CraftCheck.NeedsStation, book.Check(0, TwoOre(), CraftStationMask.None, ProfessionSkills.Start));
            Assert.AreEqual(CraftCheck.NeedsStation, book.Check(0, TwoOre(), CraftStationMask.Forge, ProfessionSkills.Start));
            Assert.AreEqual(CraftCheck.Ok, book.Check(0, TwoOre(), CraftStationMask.Anvil, ProfessionSkills.Start));
        }

        [Test]
        public void Check_SaysSkillBeforeStationBeforeInputs()
        {
            RecipeBook book = OneRecipeBook(CraftStation.Forge, 25);
            var empty = new List<ItemStack>();
            ProfessionSkills skilled = ProfessionSkills.Start.With(ProfessionId.Blacksmithing, 25);
            Assert.AreEqual(CraftCheck.NeedsSkill, book.Check(0, empty, CraftStationMask.None, ProfessionSkills.Start));
            Assert.AreEqual(CraftCheck.NeedsStation, book.Check(0, empty, CraftStationMask.None, skilled));
            Assert.AreEqual(CraftCheck.MissingInputs, book.Check(0, empty, CraftStationMask.Forge, skilled));
        }

        [Test]
        public void Validate_RefusesASkillOnARecipeWithNoProfession()
        {
            CollectionAssert.IsEmpty(OneRecipeBook(CraftStation.Forge, 25).Validate());
            CollectionAssert.IsEmpty(OneRecipeBook(CraftStation.Workbench, 1).Validate());
            Assert.AreEqual(1, OneRecipeBook(CraftStation.Workbench, 25).Validate().Count);
        }

        // ---- Crafting raises the skill the way gathering does ----

        [Test]
        public void Crafting_RaisesTheSkillOnTheSameCurveAsGathering()
        {
            ProfessionBook book = Keep(ProfessionBook.Create(maxSkill: 100, orangeRange: 10, yellowRange: 25, greenRange: 40,
                yellowChance: 0.6f, greenChance: 0.25f, bonusYieldRange: 50, maxBonusYieldChance: 0.5f, heatMapSkill: 10));
            ProfessionSkills skills = ProfessionSkills.Start.With(ProfessionId.Leatherworking, 20);

            Assert.IsTrue(ProfessionRules.TryGain(skills, ProfessionId.Leatherworking, 15, book, 0.99, out ProfessionSkills after),
                "Orange always goes up.");
            Assert.AreEqual(21, after.Get(ProfessionId.Leatherworking));
            Assert.AreEqual(1, after.Get(ProfessionId.Alchemy));
            Assert.IsFalse(ProfessionRules.TryGain(skills.With(ProfessionId.Leatherworking, 60), ProfessionId.Leatherworking, 15, book,
                0.0, out _), "Grey never does.");
        }

        // ---- The window's list (step C) ----

        RecipeDefinition Recipe(CraftStation station, int skill = 1)
        {
            ItemDefinition item = Keep(ItemDefinition.Create(ItemIds.CopperOre, "Copper Ore", category: ItemCategory.Materials));
            return Keep(RecipeDefinition.Create(new ItemAmount(item, 1), station, skill, new ItemAmount(item, 1)));
        }

        List<RecipeDefinition> MixedBook() => new List<RecipeDefinition>
        {
            Recipe(CraftStation.Workbench), // 0
            Recipe(CraftStation.Anvil, 30), // 1
            Recipe(CraftStation.Forge, 25), // 2
            Recipe(CraftStation.Anywhere), // 3
            Recipe(CraftStation.Forge), // 4
            Recipe(CraftStation.TanningRack, 10), // 5
            Recipe(CraftStation.Anvil, 25), // 6
        };

        [TestCase(CraftStation.Anywhere, CraftingTab.General)]
        [TestCase(CraftStation.Workbench, CraftingTab.General)]
        [TestCase(CraftStation.AlchemyTable, CraftingTab.Alchemy)]
        [TestCase(CraftStation.Forge, CraftingTab.Blacksmithing)]
        [TestCase(CraftStation.Anvil, CraftingTab.Blacksmithing)]
        [TestCase(CraftStation.TanningRack, CraftingTab.Leatherworking)]
        public void List_PutsEachStationInItsTab(CraftStation station, CraftingTab tab) =>
            Assert.AreEqual(tab, CraftingList.TabOf(station));

        [Test]
        public void List_ShowsOnlyTheTabsRecipes_EasiestFirst_ThenInBookOrder()
        {
            var list = new CraftingList(MixedBook());
            CollectionAssert.AreEqual(new[] { 0, 3 }, list.Rows);
            list.SetTab(CraftingTab.Blacksmithing);
            CollectionAssert.AreEqual(new[] { 4, 2, 6, 1 }, list.Rows, "Both the Forge's and the Anvil's, by skill, then book order.");
            list.SetTab(CraftingTab.Leatherworking);
            CollectionAssert.AreEqual(new[] { 5 }, list.Rows);
        }

        [Test]
        public void List_OpensOnTheNearestStationsTab_AndKeepsTheLastTabWithNone()
        {
            var list = new CraftingList(MixedBook());
            list.OpenAt(CraftStation.Anvil);
            Assert.AreEqual(CraftingTab.Blacksmithing, list.Tab);
            list.OpenAt(CraftStation.Anywhere);
            Assert.AreEqual(CraftingTab.Blacksmithing, list.Tab);
            list.OpenAt(CraftStation.Workbench);
            Assert.AreEqual(CraftingTab.General, list.Tab);
        }

        [Test]
        public void List_StepsTabsAndRowsRoundTheEnds_AndEachTabKeepsItsCursor()
        {
            var list = new CraftingList(MixedBook());
            list.StepTab(-1);
            Assert.AreEqual(CraftingTab.Leatherworking, list.Tab);
            list.StepTab(1);
            Assert.AreEqual(CraftingTab.General, list.Tab);

            list.SetTab(CraftingTab.Blacksmithing);
            list.Move(-1);
            Assert.AreEqual(1, list.CursorRecipe, "Up from the top wraps to the bottom.");
            list.Move(1);
            Assert.AreEqual(4, list.CursorRecipe);
            list.Move(2);
            list.SetTab(CraftingTab.General);
            Assert.AreEqual(0, list.CursorRecipe);
            list.SetTab(CraftingTab.Blacksmithing);
            Assert.AreEqual(6, list.CursorRecipe);
        }

        [Test]
        public void List_EmptyTabHasNoCursorRecipe()
        {
            var list = new CraftingList(MixedBook());
            list.SetTab(CraftingTab.Alchemy);
            Assert.AreEqual(0, list.Rows.Count);
            Assert.AreEqual(-1, list.CursorRecipe);
            list.Move(1);
            Assert.AreEqual(-1, list.CursorRecipe);
        }

        [Test]
        public void List_ScrollsJustEnoughToKeepTheCursorInSight()
        {
            var list = new CraftingList(MixedBook());
            list.SetTab(CraftingTab.Blacksmithing);
            Assert.AreEqual(0, list.FirstRow(2));
            list.Move(2);
            Assert.AreEqual(1, list.FirstRow(2));
            list.Move(1);
            Assert.AreEqual(2, list.FirstRow(2));
            list.Move(1); // Wraps to the top.
            Assert.AreEqual(0, list.FirstRow(2));
            Assert.AreEqual(0, list.FirstRow(10), "Everything fits.");
        }

        // ---- The station blocks in the project (step B) ----

        const string DataDir = "Assets/_Voxelforge/Data";

        static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>($"{DataDir}/{path}");
            Assert.IsNotNull(asset, path);
            return asset;
        }

        static BlockDefinition BlockOf(CraftStation station)
        {
            foreach (BlockDefinition block in Load<BlockRegistry>("Blocks/BlockRegistry.asset").Blocks)
            {
                if (block != null && block.Id == CraftStations.BlockOf(station))
                    return block;
            }
            Assert.Fail($"No block for the {station}.");
            return null;
        }

        static DecorationDefinition PropOf(BlockDefinition block)
        {
            foreach (DecorationDefinition decoration in Load<DecorationSet>("Decorations/DecorationSet.asset").Decorations)
            {
                if (decoration != null && decoration.PropBlock == block)
                    return decoration;
            }
            Assert.Fail($"No prop for {block.DisplayName}.");
            return null;
        }

        [TestCase(CraftStation.AlchemyTable)]
        [TestCase(CraftStation.Forge)]
        [TestCase(CraftStation.Anvil)]
        [TestCase(CraftStation.TanningRack)]
        public void StationBlocks_AreBlastProofSolidProps_WithAModelAndAName(CraftStation station)
        {
            BlockDefinition block = BlockOf(station);
            Assert.IsTrue(block.IsSolid);
            Assert.IsTrue(block.IsProp);
            Assert.IsTrue(block.IsBlastProof, "A partner's spell shouldn't break a station.");
            Assert.AreEqual(CraftStations.DisplayName(station), block.DisplayName);
            Assert.IsNotNull(PropOf(block).Mesh);
        }

        [TestCase(CraftStation.AlchemyTable, 0)]
        [TestCase(CraftStation.Forge, 1)]
        [TestCase(CraftStation.Anvil, 2)]
        [TestCase(CraftStation.TanningRack, 3)]
        public void StationItems_AreMadeAtAWorkbench_AfterTheOlderRecipes(CraftStation station, int order)
        {
            var book = Load<RecipeBook>("Items/RecipeBook.asset");
            RecipeDefinition recipe = book.Recipes[13 + order];
            Assert.AreEqual(CraftStations.BlockOf(station), recipe.Output.ItemId);
            Assert.AreEqual(CraftStation.Workbench, recipe.Station);
            Assert.AreEqual(1, recipe.RequiredSkill, "A workbench recipe has no profession to need a skill in.");
            CollectionAssert.IsEmpty(book.Validate());
            Assert.IsTrue(Load<ItemRegistry>("Items/ItemRegistry.asset").TryGet(recipe.Output.ItemId, out _), "It needs an item to hold and place.");
        }

        [Test]
        public void OlderRecipes_KeepTheirPlaces_BecauseAPlaceIsSentOverTheNetwork()
        {
            var expected = new ushort[]
            {
                BlockIds.Workbench, BlockIds.Torch, ItemIds.WoodenPickaxe, ItemIds.StonePickaxe, ItemIds.StoneAxe, ItemIds.PickOfDestiny,
                ItemIds.Mgrs, BlockIds.HomeTotem, ItemIds.WovenSatchel, ItemIds.TravelersPack, ItemIds.DelversRucksack, BlockIds.Chest,
                BlockIds.TrainingDummy,
            };
            var book = Load<RecipeBook>("Items/RecipeBook.asset");
            for (int i = 0; i < expected.Length; i++)
                Assert.AreEqual(expected[i], book.Recipes[i].Output.ItemId, $"Recipe {i}");
        }

        // ---- The crafting content in the project (step D) ----

        [TestCase(ProfessionId.Blacksmithing)]
        [TestCase(ProfessionId.Leatherworking)]
        public void EverySkill_HasARecipeThatCanStillRaiseIt_AllTheWayToTheMax(ProfessionId profession)
        {
            var book = Load<RecipeBook>("Items/RecipeBook.asset");
            var professions = Load<ProfessionBook>("Professions/ProfessionBook.asset");
            for (int skill = 1; skill < professions.MaxSkill; skill++)
            {
                bool canGain = false;
                foreach (RecipeDefinition recipe in book.Recipes)
                {
                    canGain |= CraftStations.TryGetProfession(recipe.Station, out ProfessionId its) && its == profession &&
                               recipe.RequiredSkill <= skill && ProfessionRules.GainChance(skill, recipe.RequiredSkill, professions) > 0f;
                }
                Assert.IsTrue(canGain, $"{profession} gets stuck at {skill}.");
            }
        }

        [Test]
        public void EveryRecipeInput_IsGatheredOrMadeWithNoMoreSkill()
        {
            var book = Load<RecipeBook>("Items/RecipeBook.asset");
            foreach (RecipeDefinition recipe in book.Recipes)
            {
                foreach (ItemAmount input in recipe.Inputs)
                {
                    foreach (RecipeDefinition maker in book.Recipes)
                    {
                        if (maker.Output.ItemId != input.ItemId)
                            continue;
                        bool sameProfession = CraftStations.TryGetProfession(maker.Station, out ProfessionId a) &&
                                              CraftStations.TryGetProfession(recipe.Station, out ProfessionId b) && a == b;
                        Assert.IsTrue(!sameProfession || maker.RequiredSkill <= recipe.RequiredSkill,
                            $"{recipe.name} needs {maker.name}, which takes more skill to make.");
                    }
                }
            }
        }

        [Test]
        public void TheAnvil_TakesBars_SoTheForgeComesFirst()
        {
            var book = Load<RecipeBook>("Items/RecipeBook.asset");
            RecipeDefinition anvil = book.Recipes[15];
            Assert.AreEqual(BlockIds.Anvil, anvil.Output.ItemId);
            bool bars = false;
            foreach (ItemAmount input in anvil.Inputs)
                bars |= input.ItemId == ItemIds.CopperBar;
            Assert.IsTrue(bars);
        }

        [TestCase(ItemIds.WovenSatchel, 1, ItemIds.CuredLeather, 0)]
        [TestCase(ItemIds.TravelersPack, 1, ItemIds.WovenSatchel, ItemIds.WolfhideLeather)]
        [TestCase(ItemIds.DelversRucksack, 1, ItemIds.TravelersPack, ItemIds.StormhideLeather)]
        public void Bags_AreTannedUpgrades_InTheirOldPlaces(ushort bag, int count, ushort input, int secondInput)
        {
            var book = Load<RecipeBook>("Items/RecipeBook.asset");
            int place = bag - ItemIds.WovenSatchel + 8;
            RecipeDefinition recipe = book.Recipes[place];
            Assert.AreEqual(bag, recipe.Output.ItemId);
            Assert.AreEqual(count, recipe.Output.count);
            Assert.AreEqual(CraftStation.TanningRack, recipe.Station);
            Assert.AreEqual(input, recipe.Inputs[0].ItemId);
            if (secondInput != 0)
                Assert.AreEqual(secondInput, recipe.Inputs[1].ItemId);
        }

        [Test]
        public void CraftedGear_IsWearable_WithAWornMeshForLegsAndFeet_AndNeverLegendary()
        {
            var items = Load<ItemRegistry>("Items/ItemRegistry.asset");
            int found = 0;
            foreach (ItemDefinition item in items.Items)
            {
                if (item == null || item.Id < 1240 || item.Id > 1299)
                    continue;
                found++;
                Assert.AreNotEqual(EquipSlot.None, item.EquipSlot, item.DisplayName);
                Assert.AreNotEqual(ItemRarity.Legendary, item.Rarity, $"{item.DisplayName}: Legendary stays boss-only.");
                Assert.IsNotNull(item.Mesh, item.DisplayName);
                if (item.EquipSlot == EquipSlot.Legs || item.EquipSlot == EquipSlot.Feet)
                    Assert.IsNotNull(item.WornMesh, item.DisplayName);
            }
            Assert.AreEqual(30, found);
        }

        [Test]
        public void CraftedGear_IsMadeFromARecipe_AndNeverDrops()
        {
            var book = Load<RecipeBook>("Items/RecipeBook.asset");
            var made = new HashSet<ushort>();
            foreach (RecipeDefinition recipe in book.Recipes)
                made.Add(recipe.Output.ItemId);
            for (ushort id = ItemIds.CopperHelm; id <= ItemIds.ManaforgedKiteShield; id++)
                Assert.IsTrue(made.Contains(id), $"Nothing makes item {id}.");
            for (ushort id = ItemIds.CuredCap; id <= ItemIds.StormhideStriders; id++)
                Assert.IsTrue(made.Contains(id), $"Nothing makes item {id}.");
            CollectionAssert.IsEmpty(book.Validate());
        }

        [Test]
        public void OnlyTheForge_GivesOffLight()
        {
            Assert.IsTrue(PropOf(BlockOf(CraftStation.Forge)).Light.enabled);
            Assert.IsFalse(PropOf(BlockOf(CraftStation.Anvil)).Light.enabled);
        }
    }
}
