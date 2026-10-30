using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using Voxelforge.Gameplay;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 4.6: the classes, their pools and action bars, and the class window.</summary>
    public class ClassTests
    {
        const string ClassBookPath = "Assets/_Voxelforge/Data/Classes/ClassBook.asset";
        const string DefaultLoadoutPath = "Assets/_Voxelforge/Data/Abilities/DefaultLoadout.asset";

        static ClassBook RealBook()
        {
            var book = AssetDatabase.LoadAssetAtPath<ClassBook>(ClassBookPath);
            Assert.IsNotNull(book, "Run Voxelforge > Build Abilities to make the class book.");
            return book;
        }

        [Test]
        public void RealClassBook_IsValid_AndInWindowOrder()
        {
            ClassBook book = RealBook();
            CollectionAssert.IsEmpty(book.Validate());
            var order = new List<HeroClassId>();
            foreach (ClassDefinition definition in book.Classes)
                order.Add(definition.Id);
            CollectionAssert.AreEqual(new[]
            {
                HeroClassId.Adventurer, HeroClassId.RuneforgedPaladin, HeroClassId.GeomancerMage, HeroClassId.Shaman,
            }, order);
        }

        [TestCase(HeroClassId.Adventurer, 100f, 100f, 10f)]
        [TestCase(HeroClassId.RuneforgedPaladin, 150f, 80f, 8f)]
        [TestCase(HeroClassId.GeomancerMage, 90f, 130f, 12f)]
        [TestCase(HeroClassId.Shaman, 110f, 110f, 10f)]
        public void Classes_HaveTheirPools(HeroClassId id, float health, float mana, float regen)
        {
            Assert.IsTrue(RealBook().TryGet(id, out ClassDefinition definition));
            Assert.AreEqual(health, definition.MaxHealth);
            Assert.AreEqual(mana, definition.MaxMana);
            Assert.AreEqual(regen, definition.ManaRegen);
        }

        [Test]
        public void Adventurer_KeepsTheStarterBar()
        {
            Assert.IsTrue(RealBook().TryGet(HeroClassId.Adventurer, out ClassDefinition adventurer));
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<ActionBarLoadout>(DefaultLoadoutPath), adventurer.Loadout);
        }

        [Test]
        public void Book_WithoutAnAdventurer_IsInvalid()
        {
            ClassDefinition shaman = ClassDefinition.Create(HeroClassId.Shaman, "Shaman", loadout: ActionBarLoadout.Create());
            ClassBook book = ClassBook.Create(new[] { shaman });
            try
            {
                CollectionAssert.IsNotEmpty(book.Validate());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(shaman.Loadout);
                UnityEngine.Object.DestroyImmediate(shaman);
                UnityEngine.Object.DestroyImmediate(book);
            }
        }

        [Test]
        public void ClassMenuButton_HasItsOwnBit()
        {
            PlayerButtons all = PlayerButtons.None;
            foreach (PlayerButtons button in System.Enum.GetValues(typeof(PlayerButtons)))
            {
                if (button == PlayerButtons.None || button == PlayerButtons.ClassMenu)
                    continue;
                all |= button;
            }
            Assert.AreEqual(PlayerButtons.None, all & PlayerButtons.ClassMenu);
        }

        [Test]
        public void Caster_SetMana_StaysInsideThePool()
        {
            var caster = new AbilityCaster(100f, 0f);
            caster.SetMaxMana(130f);
            caster.SetMana(65f);
            Assert.AreEqual(65f, caster.Mana, 1e-4f);
            caster.SetMana(500f);
            Assert.AreEqual(130f, caster.Mana, 1e-4f);
            caster.SetMana(-5f);
            Assert.AreEqual(0f, caster.Mana, 1e-4f);
        }

        [Test]
        public void Picker_OpensOnTheHerosClass_AndStopsAtTheEnds()
        {
            var model = new ClassPickerModel();
            model.Open(4, 2);
            Assert.AreEqual(2, model.Cursor);
            model.Move(+1);
            model.Move(+1);
            Assert.AreEqual(3, model.Cursor, "Stops at the last card.");
            model.Move(-5);
            Assert.AreEqual(0, model.Cursor, "Stops at the first card.");
        }

        [Test]
        public void Picker_PointAt_IgnoresCardsThatAreNotThere()
        {
            var model = new ClassPickerModel();
            model.Open(4, 0);
            model.PointAt(3);
            Assert.AreEqual(3, model.Cursor);
            model.PointAt(7);
            Assert.AreEqual(3, model.Cursor);
            model.Open(4, 9);
            Assert.AreEqual(3, model.Cursor, "A class out of range opens on the last card.");
        }
    }
}
