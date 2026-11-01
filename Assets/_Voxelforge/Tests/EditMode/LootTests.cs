using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Voxelforge.Gameplay;
using Voxelforge.Voxels;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 5.5 step A: rarity, gear data, loot tables and the loot roller, without any networking.</summary>
    public class LootTests
    {
        const string RegistryAssetPath = "Assets/_Voxelforge/Data/Items/ItemRegistry.asset";
        const string BlockRegistryAssetPath = "Assets/_Voxelforge/Data/Blocks/BlockRegistry.asset";
        const string LootBookAssetPath = "Assets/_Voxelforge/Data/Loot/LootBook.asset";
        const string MobBookAssetPath = "Assets/_Voxelforge/Data/Mobs/MobBook.asset";

        const ushort Hide = 1100;
        const ushort Fang = 1101;
        const ushort Helm = 1200;
        const ushort Sword = 1201;

        readonly List<Object> _made = new List<Object>();
        readonly List<ItemStack> _loot = new List<ItemStack>();

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

        LootTable Table(IEnumerable<LootDrop> drops, int rolls = 0, IEnumerable<LootEntry> pool = null, float nothing = 0f,
            LootSource source = LootSource.Mob, MobId mob = MobId.BristlebackBoar) =>
            Keep(LootTable.Create(source, mob, drops, rolls, pool, nothing));

        ItemRegistry Registry() => Keep(ItemRegistry.Create(new[]
        {
            Keep(ItemDefinition.Create(Hide, "Hide", category: ItemCategory.Materials)),
            Keep(ItemDefinition.Create(Fang, "Fang", category: ItemCategory.Materials, rarity: ItemRarity.Rare)),
            Keep(ItemDefinition.Create(Helm, "Helm", rarity: ItemRarity.Epic, equipSlot: EquipSlot.Head, stats: new GearStats(5, 3, 0))),
            Keep(ItemDefinition.Create(Sword, "Sword", rarity: ItemRarity.Legendary, equipSlot: EquipSlot.MainHand,
                stats: new GearStats(0, 2, 10))),
        }));

        // ---- The roller ----

        [Test]
        public void Roll_DropsCertainDropsEveryTime_AndNeverImpossibleOnes()
        {
            LootTable table = Table(new[] { new LootDrop(Hide, 1f), new LootDrop(Fang, 0f) });
            var random = new System.Random(1);
            for (int i = 0; i < 200; i++)
            {
                LootRoller.Roll(table, random, _loot);
                Assert.AreEqual(1, _loot.Count);
                Assert.AreEqual(new ItemStack(Hide, 1), _loot[0]);
            }
        }

        [Test]
        public void Roll_GivesBetweenMinAndMax_AndReachesBothEnds()
        {
            LootTable table = Table(new[] { new LootDrop(Hide, 1f, 2, 4) });
            var random = new System.Random(2);
            var seen = new HashSet<int>();
            for (int i = 0; i < 300; i++)
            {
                LootRoller.Roll(table, random, _loot);
                int count = _loot[0].Count;
                Assert.That(count, Is.InRange(2, 4));
                seen.Add(count);
            }
            CollectionAssert.AreEquivalent(new[] { 2, 3, 4 }, seen);
        }

        [Test]
        public void Roll_DropChanceMatchesItsOdds()
        {
            LootTable table = Table(new[] { new LootDrop(Fang, 0.25f) });
            var random = new System.Random(3);
            int dropped = 0;
            const int Rolls = 20000;
            for (int i = 0; i < Rolls; i++)
            {
                LootRoller.Roll(table, random, _loot);
                dropped += _loot.Count;
            }
            Assert.AreEqual(0.25, dropped / (double)Rolls, 0.015);
        }

        [Test]
        public void Roll_PicksFromThePoolByWeight_IncludingNothing()
        {
            // Helm 1 : Sword 3 : nothing 6.
            LootTable table = Table(null, 1, new[] { new LootEntry(Helm, 1f), new LootEntry(Sword, 3f) }, nothing: 6f);
            var random = new System.Random(4);
            int helms = 0, swords = 0, nothing = 0;
            const int Rolls = 20000;
            for (int i = 0; i < Rolls; i++)
            {
                LootRoller.Roll(table, random, _loot);
                if (_loot.Count == 0)
                    nothing++;
                else if (_loot[0].ItemId == Helm)
                    helms++;
                else
                    swords++;
            }
            Assert.AreEqual(0.1, helms / (double)Rolls, 0.015);
            Assert.AreEqual(0.3, swords / (double)Rolls, 0.015);
            Assert.AreEqual(0.6, nothing / (double)Rolls, 0.015);
        }

        [Test]
        public void Roll_MergesTheSameItemIntoOneStack()
        {
            LootTable table = Table(new[] { new LootDrop(Hide, 1f, 2, 2) }, 3, new[] { new LootEntry(Hide, 1f, 1, 1) });
            LootRoller.Roll(table, new System.Random(5), _loot);
            Assert.AreEqual(1, _loot.Count);
            Assert.AreEqual(new ItemStack(Hide, 5), _loot[0]);
        }

        [Test]
        public void Roll_IsTheSameForTheSameSeed()
        {
            LootTable table = Table(new[] { new LootDrop(Hide, 0.5f, 1, 3), new LootDrop(Fang, 0.5f) }, 2,
                new[] { new LootEntry(Helm, 1f), new LootEntry(Sword, 1f) }, nothing: 1f);
            var first = new List<ItemStack>();
            var second = new List<ItemStack>();
            var a = new System.Random(77);
            var b = new System.Random(77);
            for (int i = 0; i < 50; i++)
            {
                LootRoller.Roll(table, a, first);
                LootRoller.Roll(table, b, second);
                CollectionAssert.AreEqual(first, second);
            }
        }

        [Test]
        public void Roll_ClearsWhatWasThere_AndCopesWithNoTable()
        {
            _loot.Add(new ItemStack(Hide, 9));
            LootRoller.Roll(null, new System.Random(6), _loot);
            Assert.IsEmpty(_loot);
        }

        // ---- The book ----

        [Test]
        public void Book_FindsEachMobsTable_AndTheRuinCache()
        {
            LootTable boar = Table(new[] { new LootDrop(Hide, 1f) });
            LootTable wolf = Table(new[] { new LootDrop(Fang, 1f) }, mob: MobId.FrostfangWolf);
            LootTable cache = Table(new[] { new LootDrop(Hide, 1f) }, source: LootSource.RuinCache, mob: MobId.None);
            LootBook book = Keep(LootBook.Create(new[] { boar, wolf, cache }));

            Assert.IsTrue(book.TryGetForMob(MobId.FrostfangWolf, out LootTable found));
            Assert.AreSame(wolf, found);
            Assert.IsTrue(book.TryGetRuinCache(out found));
            Assert.AreSame(cache, found);
            Assert.IsFalse(book.TryGetForMob(MobId.ThunderRhino, out _));
        }

        [Test]
        public void Book_Validate_CatchesDuplicatesUnknownItemsAndEmptyTables()
        {
            ItemRegistry registry = Registry();
            LootBook book = Keep(LootBook.Create(new[]
            {
                Table(new[] { new LootDrop(Hide, 1f) }),
                Table(new[] { new LootDrop(Fang, 1f) }), // A second boar table.
                Table(new[] { new LootDrop(4321, 1f) }, mob: MobId.FrostfangWolf), // No such item.
                Table(null, 1, new[] { new LootEntry(Helm, 0f) }, mob: MobId.ThunderRhino), // Weight 0, so nothing ever drops.
                Table(new[] { new LootDrop(Hide, 1f, 3, 2) }, mob: MobId.CrystalSlime), // Max under min.
            }));
            List<string> errors = book.Validate(registry, null);
            Assert.That(errors, Has.Some.Contains("more than one table"));
            Assert.That(errors, Has.Some.Contains("doesn't exist"));
            Assert.That(errors, Has.Some.Contains("never drops anything"));
            Assert.That(errors, Has.Some.Contains("smallest first"));
        }

        [Test]
        public void RealLootBook_IsValid_AndEveryMobHasATable()
        {
            var book = AssetDatabase.LoadAssetAtPath<LootBook>(LootBookAssetPath);
            var items = AssetDatabase.LoadAssetAtPath<ItemRegistry>(RegistryAssetPath);
            var blocks = AssetDatabase.LoadAssetAtPath<BlockRegistry>(BlockRegistryAssetPath);
            var mobs = AssetDatabase.LoadAssetAtPath<MobBook>(MobBookAssetPath);
            Assert.IsNotNull(book, "Run Voxelforge > Build Loot.");
            CollectionAssert.IsEmpty(book.Validate(items, blocks));
            foreach (MobDefinition mob in mobs.Mobs)
                Assert.IsTrue(book.TryGetForMob(mob.Id, out _), $"{mob.DisplayName} has no loot table.");
            Assert.IsTrue(book.TryGetRuinCache(out _));
        }

        [Test]
        public void RealItemRegistry_IsValid_AndHasGearForEverySlotAndRarity()
        {
            var items = AssetDatabase.LoadAssetAtPath<ItemRegistry>(RegistryAssetPath);
            CollectionAssert.IsEmpty(items.Validate(AssetDatabase.LoadAssetAtPath<BlockRegistry>(BlockRegistryAssetPath)));
            var slots = new HashSet<EquipSlot>();
            var rarities = new HashSet<ItemRarity>();
            foreach (ItemDefinition item in items.Items)
            {
                if (item.IsWornGear)
                {
                    slots.Add(item.EquipSlot);
                    rarities.Add(item.Rarity);
                }
            }
            Assert.AreEqual(EquippedGear.SocketCount, slots.Count);
            Assert.AreEqual(4, rarities.Count);
            Assert.AreEqual(ItemCategory.Materials, items.CategoryOf(ItemIds.BoarHide));
            Assert.AreEqual(ItemRules.BigStack, items.MaxStackOf(ItemIds.BoarHide));
            Assert.AreEqual(1, items.MaxStackOf(ItemIds.Skyrender));
            Assert.AreEqual(ItemRarity.Legendary, items.RarityOf(ItemIds.Skyrender));
        }

        // ---- Gear data ----

        [Test]
        public void Registry_Validate_RejectsStacksOrStatlessGear()
        {
            ItemRegistry registry = Keep(ItemRegistry.Create(new[]
            {
                Keep(ItemDefinition.Create(1300, "Stacking Helm", category: ItemCategory.Materials, equipSlot: EquipSlot.Head,
                    stats: new GearStats(1, 0, 0))),
                Keep(ItemDefinition.Create(1301, "Empty Boots", equipSlot: EquipSlot.Feet)),
            }));
            List<string> errors = registry.Validate(null);
            Assert.That(errors, Has.Some.Contains("'Stacking Helm' is worn gear, so it must be a one-per-slot Gear item"));
            Assert.That(errors, Has.Some.Contains("'Empty Boots' is worn gear with no stats"));
        }

        [Test]
        public void GearStats_Sum_AddsEveryWornPiece_AndOnlyWornGearHasStats()
        {
            ItemRegistry registry = Registry();
            var gear = new EquippedGear { Head = Helm, MainHand = Sword };
            Assert.AreEqual(new GearStats(5, 5, 10), GearStats.Sum(gear, registry));
            Assert.AreEqual(default(GearStats), GearStats.Sum(default, registry));
            Assert.AreEqual(default(GearStats), registry.StatsOf(Hide));
            Assert.AreEqual(EquipSlot.None, registry.EquipSlotOf(Hide));
            Assert.AreEqual(ItemRarity.Common, registry.RarityOf(9999));
        }

        [Test]
        public void EquippedGear_SocketsMatchTheSlots()
        {
            var gear = new EquippedGear();
            for (int socket = 0; socket < EquippedGear.SocketCount; socket++)
            {
                EquipSlot slot = EquippedGear.SlotOf(socket);
                Assert.AreNotEqual(EquipSlot.None, slot);
                Assert.AreEqual(socket, EquippedGear.SocketOf(slot));
                gear[socket] = (ushort)(2000 + socket);
            }
            Assert.AreEqual(2000, gear.Head);
            Assert.AreEqual(2005, gear.OffHand);
            Assert.AreEqual(-1, EquippedGear.SocketOf(EquipSlot.None));
        }

        [Test]
        public void GearText_DescribesRaritySlotAndStats()
        {
            ItemRegistry registry = Registry();
            Assert.AreEqual("Epic Head: Armor 5, Stamina 3", GearText.Kind(registry, Helm));
            Assert.AreEqual("Legendary Main Hand: Stamina 2, Power 10", GearText.Kind(registry, Sword));
            Assert.AreEqual("Materials", GearText.Kind(registry, Hide));
            Assert.AreEqual("Rare Materials", GearText.Kind(registry, Fang));
            Assert.AreEqual("", GearText.Stats(default));
        }
    }
}
