using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Voxelforge.Gameplay;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 4.1: ability templates, mana, cooldowns and cast times.</summary>
    public class AbilityTests
    {
        const string BookAssetPath = "Assets/_Voxelforge/Data/Abilities/AbilityBook.asset";
        const string MgrsAssetPath = "Assets/_Voxelforge/Data/Items/Mgrs.asset";

        readonly List<Object> _made = new List<Object>();
        readonly List<AbilityDefinition> _finished = new List<AbilityDefinition>();

        AbilityDefinition Make(ushort id, string name, float manaCost = 0f, float cooldown = 0f, float castTime = 0f,
            bool castWhileMoving = false)
        {
            AbilityDefinition ability = AbilityDefinition.Create(id, name, manaCost, cooldown, castTime, castWhileMoving: castWhileMoving);
            _made.Add(ability);
            return ability;
        }

        [SetUp]
        public void SetUp() => _finished.Clear();

        [TearDown]
        public void TearDown()
        {
            foreach (Object made in _made)
                Object.DestroyImmediate(made);
            _made.Clear();
        }

        [Test]
        public void Instant_SpendsManaAndStartsTheCooldown_RightAway()
        {
            AbilityDefinition bolt = Make(1, "Bolt", manaCost: 8f, cooldown: 0.5f);
            var caster = new AbilityCaster(100f, 0f);

            Assert.AreEqual(CastCheck.Ok, caster.TryBegin(bolt, 10f, _finished));
            CollectionAssert.AreEqual(new[] { bolt }, _finished, "An instant ability goes off at once.");
            Assert.IsFalse(caster.IsCasting);
            Assert.AreEqual(92f, caster.Mana, 1e-4f);

            Assert.AreEqual(CastCheck.OnCooldown, caster.TryBegin(bolt, 10.3f, _finished), "0.3 s later: still recharging.");
            Assert.AreEqual(0.2f, caster.CooldownRemaining(bolt.Id, 10.3f), 1e-4f);
            Assert.AreEqual(CastCheck.Ok, caster.TryBegin(bolt, 10.5f, _finished), "0.5 s later: ready.");
            Assert.AreEqual(84f, caster.Mana, 1e-4f);
        }

        [Test]
        public void Mana_RunsDry_ThenRegeneratesUpToFull()
        {
            AbilityDefinition bolt = Make(1, "Bolt", manaCost: 40f);
            var caster = new AbilityCaster(100f, 10f);
            Assert.AreEqual(CastCheck.Ok, caster.TryBegin(bolt, 0f, _finished));
            Assert.AreEqual(CastCheck.Ok, caster.TryBegin(bolt, 0f, _finished));
            Assert.AreEqual(CastCheck.NotEnoughMana, caster.TryBegin(bolt, 0f, _finished), "20 left, needs 40.");
            Assert.AreEqual(20f, caster.Mana, 1e-4f, "A refused cast spends nothing.");

            caster.Tick(2f, 2f, false, _finished, out _);
            Assert.AreEqual(40f, caster.Mana, 1e-4f, "10 a second for 2 s.");
            Assert.AreEqual(CastCheck.Ok, caster.TryBegin(bolt, 2f, _finished));

            caster.Tick(60f, 58f, false, _finished, out _);
            Assert.AreEqual(100f, caster.Mana, 1e-4f, "Never more than full.");

            caster.SetMaxMana(50f);
            Assert.AreEqual(50f, caster.Mana, 1e-4f, "A smaller pool clips what's over the top.");
        }

        [Test]
        public void CastTime_GoesOffWhenItEnds_AndSpendsOnlyThen()
        {
            AbilityDefinition orb = Make(2, "Orb", manaCost: 30f, cooldown: 6f, castTime: 1.5f);
            AbilityDefinition bolt = Make(1, "Bolt");
            var caster = new AbilityCaster(100f, 0f);

            Assert.AreEqual(CastCheck.Ok, caster.TryBegin(orb, 0f, _finished));
            Assert.IsTrue(caster.IsCasting);
            Assert.AreEqual(orb, caster.Casting);
            Assert.IsEmpty(_finished);
            Assert.AreEqual(100f, caster.Mana, "Nothing is spent until the cast finishes.");
            Assert.AreEqual(CastCheck.Busy, caster.TryBegin(bolt, 0.5f, _finished), "One cast at a time.");

            Assert.IsFalse(caster.Tick(1.4f, 0.02f, false, _finished, out _), "Not done yet.");
            Assert.IsTrue(caster.Tick(1.5f, 0.02f, false, _finished, out CastCheck result));
            Assert.AreEqual(CastCheck.Ok, result);
            CollectionAssert.AreEqual(new[] { orb }, _finished);
            Assert.IsFalse(caster.IsCasting);
            Assert.AreEqual(70f, caster.Mana, 1e-4f);
            Assert.AreEqual(6f, caster.CooldownRemaining(orb.Id, 1.5f), 1e-4f, "The cooldown counts from the end of the cast.");
            Assert.AreEqual(0f, caster.CooldownRemaining(bolt.Id, 1.5f), "Cooldowns are per ability.");
        }

        [Test]
        public void Moving_CancelsACast_UnlessItCanBeCastOnTheMove()
        {
            AbilityDefinition orb = Make(2, "Orb", manaCost: 30f, cooldown: 6f, castTime: 1.5f);
            AbilityDefinition runner = Make(3, "Runner", manaCost: 10f, castTime: 1f, castWhileMoving: true);
            var caster = new AbilityCaster(100f, 0f);

            caster.TryBegin(orb, 0f, _finished);
            Assert.IsTrue(caster.Tick(0.5f, 0.02f, true, _finished, out CastCheck result));
            Assert.AreEqual(CastCheck.Interrupted, result);
            Assert.IsFalse(caster.IsCasting);
            Assert.IsEmpty(_finished);
            Assert.AreEqual(100f, caster.Mana, "An interrupted cast costs nothing.");
            Assert.AreEqual(CastCheck.Ok, caster.Check(orb, 0.6f), "...and starts no cooldown.");

            caster.TryBegin(runner, 1f, _finished);
            Assert.IsFalse(caster.Tick(1.5f, 0.02f, true, _finished, out _), "Moving is fine for this one.");
            Assert.IsTrue(caster.Tick(2f, 0.02f, true, _finished, out result));
            Assert.AreEqual(CastCheck.Ok, result);
            CollectionAssert.AreEqual(new[] { runner }, _finished);
        }

        [Test]
        public void Cast_FailsAtTheEnd_IfTheManaIsGoneByThen()
        {
            AbilityDefinition orb = Make(2, "Orb", manaCost: 30f, castTime: 1.5f);
            AbilityDefinition drain = Make(1, "Drain", manaCost: 0f);
            var caster = new AbilityCaster(30f, 0f);
            caster.TryBegin(orb, 0f, _finished);
            caster.SetMaxMana(20f); // Something took mana mid-cast (a smaller pool stands in for a mana drain).
            Assert.IsTrue(caster.Tick(1.5f, 0.02f, false, _finished, out CastCheck result));
            Assert.AreEqual(CastCheck.NotEnoughMana, result);
            Assert.IsEmpty(_finished);
            Assert.AreEqual(CastCheck.Ok, caster.TryBegin(drain, 1.6f, _finished), "Free to cast again afterwards.");
        }

        [Test]
        public void Check_RefusesNoAbility()
        {
            Assert.AreEqual(CastCheck.UnknownAbility, new AbilityCaster(100f, 0f).TryBegin(null, 0f, _finished));
        }

        [Test]
        public void Book_FindsById_AndFlagsBadTemplates()
        {
            AbilityDefinition bolt = Make(AbilityIds.ManaBolt, "Bolt");
            AbilityDefinition twin = Make(AbilityIds.ManaBolt, "Twin");
            AbilityDefinition noId = Make(AbilityIds.None, "Nameless");
            AbilityDefinition stuck = AbilityDefinition.Create(AbilityIds.ArcaneOrb, "Stuck", projectileSpeed: 0f);
            _made.Add(stuck);
            AbilityBook book = AbilityBook.Create(new[] { bolt, twin, noId, stuck, null });
            _made.Add(book);

            Assert.IsTrue(book.TryGet(AbilityIds.ManaBolt, out AbilityDefinition found));
            Assert.AreEqual(bolt, found, "The first one with an ID wins.");
            Assert.IsFalse(book.TryGet(AbilityIds.None, out _));

            List<string> errors = book.Validate();
            Assert.IsTrue(errors.Exists(e => e.Contains("Twin") && e.Contains("already")));
            Assert.IsTrue(errors.Exists(e => e.Contains("Nameless") && e.Contains("no ID")));
            Assert.IsTrue(errors.Exists(e => e.Contains("Stuck") && e.Contains("speed")));
            Assert.IsTrue(errors.Exists(e => e.Contains("Entry 4")));
        }

        [Test]
        public void RealAbilityBook_IsValid_AndTheMgrsShootsManaBolts()
        {
            var book = AssetDatabase.LoadAssetAtPath<AbilityBook>(BookAssetPath);
            Assert.IsNotNull(book, $"Missing {BookAssetPath}: run Voxelforge > Build Abilities.");
            CollectionAssert.IsEmpty(book.Validate());

            Assert.IsTrue(book.TryGet(AbilityIds.ManaBolt, out AbilityDefinition bolt));
            Assert.IsTrue(bolt.IsInstant);
            Assert.AreEqual(45f, bolt.BlockDamage, "Stone (60) still breaks in 2 shots.");
            Assert.AreEqual(0.5f, bolt.Cooldown);
            Assert.AreEqual(24f, bolt.Range);
            Assert.Greater(bolt.ManaCost, 0f);

            Assert.IsTrue(book.TryGet(AbilityIds.ArcaneOrb, out AbilityDefinition orb));
            Assert.Greater(orb.CastTime, 0f, "The test spell has a cast time, for the cast bar.");

            var mgrs = AssetDatabase.LoadAssetAtPath<ItemDefinition>(MgrsAssetPath);
            Assert.IsNotNull(mgrs);
            Assert.AreEqual(bolt, mgrs.Ability);
            Assert.AreNotEqual(Vector3.zero, mgrs.Muzzle, "Bolts leave from the top of the barrel.");
        }
    }
}
