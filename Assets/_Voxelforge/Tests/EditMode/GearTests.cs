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

        // ---- The bag window's worn-gear row (step D) ----

        static List<ItemStack> Slots(int count)
        {
            var slots = new List<ItemStack>();
            for (int i = 0; i < count; i++)
                slots.Add(new ItemStack(1, 1));
            return slots;
        }

        [Test]
        public void Cursor_GoesDownFromTheBagSocketsIntoTheGearRow_AndBackUp()
        {
            var model = new InventoryModel();
            model.Rebuild(Slots(8), ItemRules.Default);
            model.Move(3, 0);
            model.Move(0, 1); // Down out of the one row: the bag sockets.
            Assert.AreEqual(1, model.CursorSocket);
            model.Move(0, 1);
            Assert.AreEqual(-1, model.CursorSocket);
            Assert.AreEqual(1, model.CursorGear, "Straight down from bag socket 1.");
            Assert.AreEqual(-1, model.CursorSlot, "Nothing in the bag is chosen while on the gear.");

            model.Move(9, 0);
            Assert.AreEqual(EquippedGear.SocketCount - 1, model.CursorGear, "The Off Hand, no wrap.");
            model.Move(0, 1);
            Assert.AreEqual(EquippedGear.SocketCount - 1, model.CursorGear, "Nothing below the gear row.");
            model.Move(0, -1);
            Assert.AreEqual(-1, model.CursorGear);
            Assert.AreEqual(EquippedBags.SocketCount - 1, model.CursorSocket, "Up to the nearest bag socket.");
        }

        [Test]
        public void Cursor_PointingAtGear_LeavesEverythingElse_AndATabChangeLeavesTheGear()
        {
            var model = new InventoryModel();
            model.Rebuild(Slots(8), ItemRules.Default);
            model.PointAtGear(2);
            model.Rebuild(Slots(8), ItemRules.Default);
            Assert.AreEqual(2, model.CursorGear, "Rebuilding every frame keeps it there.");
            Assert.AreEqual(-1, model.CursorCell);
            model.PointAtCell(3);
            Assert.AreEqual(-1, model.CursorGear);
            model.PointAtGear(4);
            model.SetTab(InventoryTab.Materials);
            Assert.AreEqual(-1, model.CursorGear);
            model.PointAtGear(99);
            Assert.AreEqual(-1, model.CursorGear, "No such socket.");
        }

        [Test]
        public void Compare_ShowsGainsAndLosses_AgainstWhatsWornInThatSlot()
        {
            string Name(ushort id) => id == Cap ? "Cap" : id == Crown ? "Crown" : "?";
            var gear = new EquippedGear { Head = Cap };
            string text = GearText.Compare(_rules, Crown, gear, Name);
            StringAssert.StartsWith("vs Cap: ", text);
            StringAssert.Contains("+5 Armor", text);
            StringAssert.Contains("+5 Stamina", text);
            StringAssert.Contains("+8 Power", text);

            string worse = GearText.Compare(_rules, Cap, new EquippedGear { Head = Crown }, Name);
            StringAssert.Contains("-5 Armor", worse);
            StringAssert.Contains("#B3261E", worse, "Losses in red.");

            StringAssert.StartsWith("nothing worn there: ", GearText.Compare(_rules, Sword, gear, Name));
            Assert.AreEqual("same as worn", GearText.Compare(_rules, Cap, gear, Name));
            Assert.AreEqual("", GearText.Compare(_rules, Hide, gear, Name), "Not gear: nothing to compare.");
        }

        [Test]
        public void Summary_ShowsTheTotalsAndTheDamageCut()
        {
            Assert.AreEqual("Armor 100 (-50% dmg)\nStamina 3  Power 4", GearText.Summary(new GearStats(100, 3, 4)));
            Assert.AreEqual("Armor 0 (-0% dmg)\nStamina 0  Power 0", GearText.Summary(default));
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
