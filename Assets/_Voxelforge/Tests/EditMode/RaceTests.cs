using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using Voxelforge.Core;
using Voxelforge.Gameplay;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 4.7: the races, what they add to a hero, and their racials.</summary>
    public class RaceTests
    {
        const string RaceBookPath = "Assets/_Voxelforge/Data/Races/RaceBook.asset";
        const string AbilityBookPath = "Assets/_Voxelforge/Data/Abilities/AbilityBook.asset";

        readonly List<UnityEngine.Object> _made = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object made in _made)
                UnityEngine.Object.DestroyImmediate(made);
            _made.Clear();
        }

        T Made<T>(T thing) where T : UnityEngine.Object
        {
            _made.Add(thing);
            return thing;
        }

        static RaceBook RealBook()
        {
            var book = AssetDatabase.LoadAssetAtPath<RaceBook>(RaceBookPath);
            Assert.IsNotNull(book, "Run Voxelforge > Build Abilities to make the race book.");
            return book;
        }

        [Test]
        public void RealRaceBook_IsValid_AndInWindowOrder()
        {
            RaceBook book = RealBook();
            CollectionAssert.IsEmpty(book.Validate());
            var order = new List<RaceId>();
            foreach (RaceDefinition definition in book.Races)
                order.Add(definition.Id);
            CollectionAssert.AreEqual(new[] { RaceId.Wayfarer, RaceId.Thunderhoof }, order);
            Assert.AreEqual(1, book.IndexOf(RaceId.Thunderhoof));
        }

        [Test]
        public void Thunderhoof_HasHoofquake_HerbSense_AndMoreHealth()
        {
            Assert.IsTrue(RealBook().TryGet(RaceId.Thunderhoof, out RaceDefinition thunderhoof));
            Assert.AreEqual(AbilityIds.Hoofquake, thunderhoof.RacialAbility.Id);
            Assert.AreEqual(AbilityDelivery.Quake, thunderhoof.RacialAbility.Delivery);
            Assert.Greater(thunderhoof.RacialAbility.Duration, 0f, "Hoofquake stuns.");
            Assert.AreEqual(0f, thunderhoof.RacialAbility.BlockDamage, "Hoofquake doesn't dig.");
            Assert.AreEqual(RacialPassive.HerbSense, thunderhoof.Passive);
            Assert.AreEqual(20f, thunderhoof.BonusHealth);
        }

        [Test]
        public void Wayfarer_HasSecondWind_AndHearthbound()
        {
            Assert.IsTrue(RealBook().TryGet(RaceId.Wayfarer, out RaceDefinition wayfarer));
            Assert.AreEqual(AbilityIds.SecondWind, wayfarer.RacialAbility.Id);
            Assert.AreEqual(AbilityDelivery.SelfHeal, wayfarer.RacialAbility.Delivery);
            Assert.AreEqual(RacialPassive.Hearthbound, wayfarer.Passive);
        }

        [Test]
        public void Racials_AreInTheAbilityBook_AndValid()
        {
            var abilities = AssetDatabase.LoadAssetAtPath<AbilityBook>(AbilityBookPath);
            CollectionAssert.IsEmpty(abilities.Validate());
            Assert.IsTrue(abilities.TryGet(AbilityIds.Hoofquake, out _));
            Assert.IsTrue(abilities.TryGet(AbilityIds.SecondWind, out _));
        }

        [Test]
        public void Book_WithoutAWayfarer_OrARacial_IsInvalid()
        {
            RaceDefinition bare = Made(RaceDefinition.Create(RaceId.Thunderhoof, "Thunderhoof"));
            RaceBook book = Made(RaceBook.Create(new[] { bare }));
            List<string> errors = book.Validate();
            Assert.AreEqual(2, errors.Count, string.Join("\n", errors));
        }

        [Test]
        public void Pools_AddTheRaceToTheClass()
        {
            ClassDefinition shaman = Made(ClassDefinition.Create(HeroClassId.Shaman, "Shaman", 110f, 110f, 10f));
            RaceDefinition race = Made(RaceDefinition.Create(RaceId.Thunderhoof, "Thunderhoof", bonusHealth: 20f, bonusMana: 5f,
                bonusManaRegen: 1f));
            HeroPools pools = RaceRules.Pools(shaman, race);
            Assert.AreEqual(130f, pools.MaxHealth);
            Assert.AreEqual(115f, pools.MaxMana);
            Assert.AreEqual(11f, pools.ManaRegen);

            HeroPools plain = RaceRules.Pools(shaman, null);
            Assert.AreEqual(110f, plain.MaxHealth, "No race adds nothing.");
        }

        [Test]
        public void Bar_PutsTheRacialInSlotNine_AndKeepsTheClassSpells()
        {
            AbilityDefinition spear = Made(AbilityDefinition.Create(AbilityIds.StoneSpear, "Stone Spear"));
            AbilityDefinition quake = Made(AbilityDefinition.Create(AbilityIds.Hoofquake, "Hoofquake"));
            ActionBarLoadout loadout = Made(ActionBarLoadout.Create(spear));
            RaceDefinition race = Made(RaceDefinition.Create(RaceId.Thunderhoof, "Thunderhoof", racialAbility: quake));

            ActionBarSlots slots = RaceRules.Bar(loadout, race);
            Assert.AreEqual(AbilityIds.StoneSpear, slots[0]);
            Assert.AreEqual(AbilityIds.Hoofquake, slots[8]);
            Assert.AreEqual(8, RaceDefinition.RacialSlot);
            Assert.AreEqual(AbilityIds.None, RaceRules.Bar(loadout, null)[8]);
        }

        [Test]
        public void Hearthbound_ShortensTheHearth_OthersDont()
        {
            RaceDefinition hearthbound = Made(RaceDefinition.Create(RaceId.Wayfarer, "Wayfarer",
                passive: RacialPassive.Hearthbound, passiveValue: 0.5f));
            RaceDefinition herbs = Made(RaceDefinition.Create(RaceId.Thunderhoof, "Thunderhoof",
                passive: RacialPassive.HerbSense, passiveValue: 0.5f));
            Assert.AreEqual(1f, RaceRules.HearthSeconds(2f, hearthbound), 1e-5f);
            Assert.AreEqual(2f, RaceRules.HearthSeconds(2f, herbs), 1e-5f);
            Assert.AreEqual(2f, RaceRules.HearthSeconds(2f, null), 1e-5f);
        }

        [TestCase(2, 0.99f, 3)]
        [TestCase(1, 0.4f, 2)]
        [TestCase(1, 0.6f, 1)]
        public void HerbSense_GivesHalfAsManyAgain_RollingTheFraction(int baseYield, float roll, int expected)
        {
            RaceDefinition herbs = Made(RaceDefinition.Create(RaceId.Thunderhoof, "Thunderhoof",
                passive: RacialPassive.HerbSense, passiveValue: 0.5f));
            Assert.AreEqual(expected, RaceRules.HerbYield(baseYield, herbs, roll));
        }

        [Test]
        public void HerbYield_WithoutHerbSense_IsUnchanged()
        {
            RaceDefinition wayfarer = Made(RaceDefinition.Create(RaceId.Wayfarer, "Wayfarer",
                passive: RacialPassive.Hearthbound, passiveValue: 0.5f));
            Assert.AreEqual(3, RaceRules.HerbYield(3, wayfarer, 0f));
        }

        [Test]
        public void StunnedDummy_StopsSparring_UntilItWearsOff()
        {
            var dummy = new SparringDummy(0f);
            var attacker = new PlayerId(1);
            dummy.Hit(10f, attacker, 0f, out _);
            dummy.Stun(1f, 2f, attacker);
            Assert.IsTrue(dummy.IsStunned(2.9f));
            Assert.IsFalse(dummy.TrySpar(2.9f, out _), "No bales while stunned.");
            Assert.IsFalse(dummy.IsStunned(3.1f));
            Assert.IsFalse(dummy.TrySpar(3.2f, out _), "A moment's pause after the stun.");
            Assert.IsTrue(dummy.TrySpar(3.6f, out PlayerId target));
            Assert.AreEqual(attacker, target);
        }

        [Test]
        public void Stun_NeverShortensALongerStun()
        {
            var dummy = new SparringDummy(0f);
            dummy.Stun(0f, 5f, new PlayerId(1));
            dummy.Stun(1f, 1f, new PlayerId(1));
            Assert.AreEqual(5f, dummy.StunnedUntil, 1e-5f);
        }

        [Test]
        public void StunCue_SaysWhereTheTargetWas()
        {
            AbilityCue cue = AbilityCue.StunTarget(AbilityIds.Hoofquake, new UnityEngine.Vector3(1f, 2f, 3f));
            Assert.AreEqual(AbilityCueKind.TargetStunned, cue.Kind);
            Assert.AreEqual(AbilityIds.Hoofquake, cue.AbilityId);
        }
    }
}
