using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Voxelforge.Gameplay;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 5.5 step C: wearing gear from the bag and what its stats do, without any networking.</summary>
    public class GearTests
    {
        const ushort Cap = 1200;
        const ushort Crown = 1201;
        const ushort Sword = 1202;
        const ushort Hide = 1100;

        readonly List<int> _changed = new List<int>();
        ItemDefinition[] _definitions;
        ItemRegistry _rules;

        [SetUp]
        public void SetUp()
        {
            _definitions = new[]
            {
                ItemDefinition.Create(Cap, "Cap", equipSlot: EquipSlot.Head, stats: new GearStats(3, 2, 0)),
                ItemDefinition.Create(Crown, "Crown", rarity: ItemRarity.Epic, equipSlot: EquipSlot.Head, stats: new GearStats(8, 7, 8)),
                ItemDefinition.Create(Sword, "Sword", equipSlot: EquipSlot.MainHand, stats: new GearStats(0, 1, 10)),
                ItemDefinition.Create(Hide, "Hide", category: ItemCategory.Materials),
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

        ItemStacks Bag(int capacity, params ushort[] items)
        {
            var bag = new ItemStacks(capacity, _rules);
            foreach (ushort item in items)
                bag.Add(item, item == Hide ? 5 : 1, _changed);
            return bag;
        }

        // ---- Equipping ----

        [Test]
        public void Equip_MovesTheItemFromTheBagIntoItsSlotsSocket()
        {
            ItemStacks bag = Bag(4, Hide, Cap, Sword);
            var gear = new EquippedGear();
            Assert.IsTrue(EquippedGear.TryEquip(bag, ref gear, 1, _changed));
            Assert.AreEqual(Cap, gear.Head);
            Assert.AreEqual(0, bag.CountOf(Cap));
            CollectionAssert.Contains(_changed, 1);
            Assert.IsTrue(EquippedGear.TryEquip(bag, ref gear, 2, _changed));
            Assert.AreEqual(Sword, gear.MainHand);
        }

        [Test]
        public void Equip_SwapsWhatWasWorn_IntoTheSameBagSlot()
        {
            ItemStacks bag = Bag(4, Cap, Crown);
            var gear = new EquippedGear();
            EquippedGear.TryEquip(bag, ref gear, 0, _changed);
            Assert.IsTrue(EquippedGear.TryEquip(bag, ref gear, 1, _changed));
            Assert.AreEqual(Crown, gear.Head);
            Assert.AreEqual(new ItemStack(Cap, 1), bag.Stacks[1], "The cap goes where the crown was.");
        }

        [Test]
        public void Equip_SwapsEvenWhenTheBagIsFull()
        {
            ItemStacks bag = Bag(2, Crown, Hide);
            var gear = new EquippedGear { Head = Cap };
            Assert.IsTrue(EquippedGear.TryEquip(bag, ref gear, 0, _changed));
            Assert.AreEqual(Crown, gear.Head);
            Assert.AreEqual(new ItemStack(Cap, 1), bag.Stacks[0]);
        }

        [Test]
        public void Equip_RefusesThingsThatArentWornGear_AndChangesNothing()
        {
            ItemStacks bag = Bag(3, Hide);
            var gear = new EquippedGear();
            Assert.IsFalse(EquippedGear.TryEquip(bag, ref gear, 0, _changed));
            Assert.IsFalse(EquippedGear.TryEquip(bag, ref gear, 2, _changed), "An empty slot.");
            Assert.IsFalse(EquippedGear.TryEquip(bag, ref gear, 9, _changed), "No such slot.");
            Assert.AreEqual(default(EquippedGear), gear);
            Assert.AreEqual(5, bag.CountOf(Hide));
        }

        [Test]
        public void Unequip_PutsItBackInTheBag_UnlessTheBagIsFull()
        {
            ItemStacks full = Bag(1, Hide);
            var gear = new EquippedGear { MainHand = Sword };
            Assert.IsFalse(EquippedGear.TryUnequip(full, ref gear, 4, _changed));
            Assert.AreEqual(Sword, gear.MainHand, "Nowhere to put it, so it stays on.");

            ItemStacks bag = Bag(2, Hide);
            Assert.IsTrue(EquippedGear.TryUnequip(bag, ref gear, 4, _changed));
            Assert.AreEqual(0, gear.MainHand);
            Assert.AreEqual(1, bag.CountOf(Sword));
            Assert.IsFalse(EquippedGear.TryUnequip(bag, ref gear, 4, _changed), "Nothing left to take off.");
        }

        // ---- What stats do ----

        [Test]
        public void Armor_CutsDamageWithDiminishingReturns_UpToTheCap()
        {
            Assert.AreEqual(0f, GearRules.DamageCut(0));
            Assert.AreEqual(0.5f, GearRules.DamageCut((int)GearRules.ArmorForHalf), 1e-5f);
            Assert.Less(GearRules.DamageCut(20), GearRules.DamageCut(40));
            Assert.Less(GearRules.DamageCut(40) - GearRules.DamageCut(20), GearRules.DamageCut(20) - GearRules.DamageCut(0),
                "Each extra point is worth a little less.");
            Assert.AreEqual(GearRules.MaxDamageCut, GearRules.DamageCut(100000), 1e-5f);
            Assert.AreEqual(10f, GearRules.AfterArmor(20f, (int)GearRules.ArmorForHalf), 1e-4f);
            Assert.AreEqual(20f, GearRules.AfterArmor(20f, 0));
        }

        [Test]
        public void Stamina_AddsHealth_AndPower_ScalesSpells()
        {
            Assert.AreEqual(0f, GearRules.BonusHealth(0));
            Assert.AreEqual(10 * GearRules.HealthPerStamina, GearRules.BonusHealth(10));
            Assert.AreEqual(1f, GearRules.PowerScale(0));
            Assert.AreEqual(1.25f, GearRules.PowerScale(25), 1e-5f);
            Assert.AreEqual(1f, GearRules.PowerScale(-5), "Never weaker than bare.");
        }

        [Test]
        public void TheBestKitToday_StaysWithinSensibleBounds()
        {
            // Crown + Hauberk + Greaves + Windrunner + Skyrender + Thunderhorn Shield (LootBuilder's numbers).
            var best = new GearStats(8 + 18 + 9 + 5 + 6 + 11, 7 + 10 + 5 + 3 + 12 + 4, 8 + 4 + 0 + 2 + 26 + 0);
            Assert.That(GearRules.DamageCut(best.armor), Is.InRange(0.3f, 0.45f));
            Assert.That(GearRules.BonusHealth(best.stamina), Is.InRange(50f, 100f));
            Assert.That(GearRules.PowerScale(best.power), Is.InRange(1.3f, 1.5f));
        }
    }
}
