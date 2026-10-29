using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Voxelforge.Gameplay;
using Voxelforge.Voxels;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 3.2 and 4.5: the bag's slots, stacking, moving, sorting and worn bags, without any networking.</summary>
    public class ItemStacksTests
    {
        const ushort Satchel = 1100;
        const ushort Potion = 1101;

        readonly List<int> _changed = new List<int>();
        ItemDefinition[] _definitions;
        ItemRegistry _rules;

        [SetUp]
        public void SetUp()
        {
            _definitions = new[]
            {
                ItemDefinition.Create(ItemIds.StonePickaxe, "Stone Pickaxe"),
                ItemDefinition.Create(Satchel, "Satchel", bagSlots: 8),
                ItemDefinition.Create(Potion, "Potion", category: ItemCategory.Consumables),
                ItemDefinition.Create(BlockIds.ManaCrystalOre, "Mana Crystal Ore", category: ItemCategory.Materials),
            };
            _rules = ItemRegistry.Create(_definitions);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (ItemDefinition definition in _definitions)
                Object.DestroyImmediate(definition);
            Object.DestroyImmediate(_rules);
        }

        [Test]
        public void Rules_GiveEachCategoryItsStackSize()
        {
            Assert.AreEqual(ItemCategory.Blocks, _rules.CategoryOf(BlockIds.Dirt));
            Assert.AreEqual(999, _rules.MaxStackOf(BlockIds.Dirt));
            Assert.AreEqual(ItemCategory.Gear, _rules.CategoryOf(ItemIds.StonePickaxe));
            Assert.AreEqual(1, _rules.MaxStackOf(ItemIds.StonePickaxe));
            Assert.AreEqual(20, _rules.MaxStackOf(Potion));
            Assert.AreEqual(ItemCategory.Materials, _rules.CategoryOf(BlockIds.ManaCrystalOre));
            Assert.AreEqual(999, _rules.MaxStackOf(BlockIds.ManaCrystalOre));
            Assert.AreEqual(8, _rules.BagSlotsOf(Satchel));
            Assert.AreEqual(0, _rules.BagSlotsOf(ItemIds.StonePickaxe));
        }

        [Test]
        public void Add_TopsUpStacksFirst_ThenTheFirstEmptySlot()
        {
            var stacks = new ItemStacks(4, _rules);
            Assert.AreEqual(3, stacks.Add(BlockIds.Dirt, 3, _changed));
            CollectionAssert.AreEqual(new[] { 0 }, _changed);
            Assert.AreEqual(1, stacks.Add(BlockIds.Log, 1));
            Assert.AreEqual(2, stacks.Add(BlockIds.Dirt, 2, _changed));
            CollectionAssert.AreEqual(new[] { 0 }, _changed, "Dirt keeps its slot.");

            Assert.AreEqual(5, stacks.CountOf(BlockIds.Dirt));
            Assert.AreEqual(BlockIds.Log, stacks.Stacks[1].ItemId);
            Assert.AreEqual(2, stacks.UsedSlots);
        }

        [Test]
        public void Add_SpillsPastAFullStack_AndStopsWhenTheBagIsFull()
        {
            var stacks = new ItemStacks(2, _rules);
            stacks.Add(BlockIds.Stone, 997);
            Assert.AreEqual(5, stacks.Add(BlockIds.Stone, 5, _changed));
            CollectionAssert.AreEqual(new[] { 0, 1 }, _changed, "2 topped up slot 0, 3 started slot 1.");
            Assert.AreEqual(new ItemStack(BlockIds.Stone, 3), stacks.Stacks[1]);

            Assert.AreEqual(996, stacks.RoomFor(BlockIds.Stone));
            Assert.AreEqual(0, stacks.RoomFor(BlockIds.Dirt), "No empty slot for something new.");
            Assert.AreEqual(0, stacks.Add(BlockIds.Dirt, 1, _changed));
            Assert.AreEqual(0, _changed.Count);
        }

        [Test]
        public void Gear_TakesOneSlotEach()
        {
            var stacks = new ItemStacks(3, _rules);
            Assert.AreEqual(2, stacks.Add(ItemIds.StonePickaxe, 2));
            Assert.AreEqual(new ItemStack(ItemIds.StonePickaxe, 1), stacks.Stacks[0]);
            Assert.AreEqual(new ItemStack(ItemIds.StonePickaxe, 1), stacks.Stacks[1]);
            Assert.AreEqual(1, stacks.RoomFor(ItemIds.StonePickaxe));
        }

        [Test]
        public void TryTake_IsAllOrNothing_AndTakesFromTheLastStackFirst()
        {
            var stacks = new ItemStacks(4, _rules);
            stacks.Add(BlockIds.Dirt, 1000); // 999 in slot 0, 1 in slot 1.
            stacks.Add(BlockIds.Log, 1);

            Assert.IsFalse(stacks.TryTake(BlockIds.Dirt, 1001, _changed));
            Assert.AreEqual(0, _changed.Count);
            Assert.AreEqual(1000, stacks.CountOf(BlockIds.Dirt));

            Assert.IsTrue(stacks.TryTake(BlockIds.Dirt, 3, _changed));
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, _changed);
            Assert.IsTrue(stacks.Stacks[1].IsEmpty, "The 1 in the last stack went first.");
            Assert.AreEqual(997, stacks.Stacks[0].Count);
            Assert.AreEqual(BlockIds.Log, stacks.Stacks[2].ItemId, "Nothing else moved.");

            Assert.IsFalse(stacks.TryTake(BlockIds.Stone, 1));
            Assert.IsFalse(stacks.TryTake(BlockIds.Log, 0));
        }

        [Test]
        public void Move_IntoEmpty_Merges_AndSwaps()
        {
            var stacks = new ItemStacks(4, _rules);
            stacks.Add(BlockIds.Dirt, 990);
            stacks.Add(BlockIds.Log, 3);
            stacks.Add(BlockIds.Stone, 2);
            // Dirt 990 | Log 3 | Stone 2 | empty

            Assert.IsTrue(stacks.Move(1, 3, _changed), "Into an empty slot.");
            Assert.IsTrue(stacks.Stacks[1].IsEmpty);
            Assert.AreEqual(new ItemStack(BlockIds.Log, 3), stacks.Stacks[3]);

            Assert.IsTrue(stacks.Move(0, 2, _changed), "Different items swap.");
            Assert.AreEqual(BlockIds.Stone, stacks.Stacks[0].ItemId);
            Assert.AreEqual(BlockIds.Dirt, stacks.Stacks[2].ItemId);

            stacks.Add(BlockIds.Dirt, 20); // Tops up slot 2 to 999, the other 11 go to slot 1.
            Assert.AreEqual(new ItemStack(BlockIds.Dirt, 11), stacks.Stacks[1]);
            Assert.IsFalse(stacks.Move(1, 2, _changed), "Slot 2 is a full stack already.");
            stacks.TryTake(BlockIds.Dirt, 5); // From the last stack: 994 left in slot 2.
            Assert.IsTrue(stacks.Move(1, 2, _changed), "Same item: merges as much as fits.");
            Assert.AreEqual(999, stacks.Stacks[2].Count);
            Assert.AreEqual(new ItemStack(BlockIds.Dirt, 6), stacks.Stacks[1], "The rest stays behind.");

            Assert.IsFalse(stacks.Move(0, 0, _changed));
            Assert.IsFalse(stacks.Move(0, 9, _changed));
        }

        [Test]
        public void Sort_GroupsByTabThenId_MergesAndPacksToTheFront()
        {
            var stacks = new ItemStacks(8, _rules);
            stacks.Add(ItemIds.StonePickaxe, 1);
            stacks.Add(BlockIds.ManaCrystalOre, 4);
            stacks.Add(BlockIds.Log, 5);
            stacks.Add(Potion, 3);
            stacks.Add(BlockIds.Dirt, 7);
            stacks.Move(4, 7, _changed); // Dirt to the end, leaving a gap.
            stacks.Add(BlockIds.Log, 1); // Still merges onto Log.

            stacks.Sort(_changed);

            var expected = new[]
            {
                new ItemStack(BlockIds.Dirt, 7), new ItemStack(BlockIds.Log, 6), // Blocks
                new ItemStack(ItemIds.StonePickaxe, 1), // Gear
                new ItemStack(Potion, 3), // Consumables
                new ItemStack(BlockIds.ManaCrystalOre, 4), // Materials
                ItemStack.Empty, ItemStack.Empty, ItemStack.Empty,
            };
            CollectionAssert.AreEqual(expected, stacks.Stacks);
            Assert.Greater(_changed.Count, 0);
        }

        [Test]
        public void Sort_MergesSplitStacks()
        {
            var stacks = new ItemStacks(4, _rules);
            stacks.Add(BlockIds.Dirt, 999);
            stacks.Add(BlockIds.Dirt, 999);
            stacks.TryTake(BlockIds.Dirt, 998); // Slot 1 now has 1; slot 0 999.
            stacks.Add(BlockIds.Log, 1);
            stacks.TryTake(BlockIds.Dirt, 500); // Slot 1 emptied, slot 0 has 500.
            stacks.Add(BlockIds.Dirt, 600); // 499 on slot 0, 101 into slot 1.

            stacks.Sort(_changed);
            Assert.AreEqual(new ItemStack(BlockIds.Dirt, 999), stacks.Stacks[0]);
            Assert.AreEqual(new ItemStack(BlockIds.Dirt, 101), stacks.Stacks[1]);
            Assert.AreEqual(new ItemStack(BlockIds.Log, 1), stacks.Stacks[2]);
        }

        [Test]
        public void TryResize_Grows_AndShrinksOnlyIfTheItemsFitInFront()
        {
            var stacks = new ItemStacks(4, _rules);
            stacks.Add(BlockIds.Dirt, 1);
            stacks.Add(BlockIds.Log, 1);
            stacks.Move(1, 3, _changed); // Dirt | - | - | Log

            Assert.IsTrue(stacks.TryResize(6, _changed));
            Assert.AreEqual(6, stacks.Capacity);

            Assert.IsTrue(stacks.TryResize(2, _changed), "Log moves into the empty slot 1.");
            Assert.AreEqual(2, stacks.Capacity);
            Assert.AreEqual(new ItemStack(BlockIds.Log, 1), stacks.Stacks[1]);
            CollectionAssert.AreEqual(new[] { 1 }, _changed);

            Assert.IsFalse(stacks.TryResize(1, _changed), "Two items can't fit in one slot.");
            Assert.AreEqual(2, stacks.Capacity, "Nothing changed.");
        }

        [Test]
        public void Restore_KeepsSlotPlaces_SpreadsOversizedStacks_AndReturnsWhatDoesntFit()
        {
            var stacks = new ItemStacks(3, _rules);
            stacks.Add(BlockIds.Stone, 50);
            List<ItemStack> lost = stacks.Restore(new[]
            {
                new ItemStack(BlockIds.Log, 4),
                new ItemStack(BlockIds.Dirt, 0), // A v2 save's empty slot.
                new ItemStack(ItemIds.StonePickaxe, 2), // A v2 save stacked tools.
                new ItemStack(BlockIds.Snow, 7), // Past the capacity.
            }, 3);

            Assert.AreEqual(new ItemStack(BlockIds.Log, 4), stacks.Stacks[0]);
            Assert.AreEqual(new ItemStack(ItemIds.StonePickaxe, 1), stacks.Stacks[1], "The spare pickaxe took the empty slot.");
            Assert.AreEqual(new ItemStack(ItemIds.StonePickaxe, 1), stacks.Stacks[2]);
            Assert.AreEqual(0, stacks.CountOf(BlockIds.Stone), "The old contents are gone.");
            CollectionAssert.AreEqual(new[] { new ItemStack(BlockIds.Snow, 7) }, lost);
        }

        [Test]
        public void ItemSelection_StepsThroughEachItemOnce()
        {
            var stacks = new ItemStacks(5, _rules);
            stacks.Add(BlockIds.Dirt, 1500); // Two slots.
            stacks.Add(ItemIds.StonePickaxe, 2); // Two slots.
            stacks.Add(BlockIds.Log, 1);

            Assert.AreEqual(ItemIds.StonePickaxe, ItemSelection.Next(stacks.Stacks, BlockIds.Dirt));
            Assert.AreEqual(BlockIds.Log, ItemSelection.Next(stacks.Stacks, ItemIds.StonePickaxe));
            Assert.AreEqual(BlockIds.Dirt, ItemSelection.Next(stacks.Stacks, BlockIds.Log));
            Assert.AreEqual(ItemIds.StonePickaxe, ItemSelection.Previous(stacks.Stacks, BlockIds.Log));
        }

        [Test]
        public void ItemSelection_RunningOut_MovesToTheNextItemAlong()
        {
            var stacks = new ItemStacks(4, _rules);
            stacks.Add(BlockIds.Dirt, 1);
            stacks.Add(BlockIds.Stone, 1);
            stacks.Add(BlockIds.Log, 1);
            int slot = -1;
            Assert.AreEqual(BlockIds.Stone, ItemSelection.Fix(stacks.Stacks, BlockIds.Stone, ref slot));
            Assert.AreEqual(1, slot);

            stacks.TryTake(BlockIds.Stone, 1); // The slot is now empty and no longer says Stone.
            Assert.AreEqual(BlockIds.Log, ItemSelection.Fix(stacks.Stacks, BlockIds.Stone, ref slot));
            Assert.AreEqual(2, slot);
        }

        [Test]
        public void EquippedBags_AddUpTheirSlots()
        {
            var bags = new EquippedBags();
            Assert.AreEqual(0, bags.ExtraSlots(_rules));
            bags[0] = Satchel;
            bags[3] = Satchel;
            Assert.AreEqual(16, bags.ExtraSlots(_rules));
        }
    }
}
