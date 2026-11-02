using System.Collections.Generic;
using NUnit.Framework;
using Voxelforge.Gameplay;
using Voxelforge.Voxels;
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
    }
}
