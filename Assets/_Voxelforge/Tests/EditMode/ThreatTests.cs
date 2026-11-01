using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using Voxelforge.Core;
using Voxelforge.Gameplay;
using Voxelforge.Voxels;

namespace Voxelforge.Tests
{
    /// <summary>
    /// Roadmap 5.3 step A: the threat table, and mobs and Training Dummies that fight whoever tops it, so a tank can
    /// hold them while the others cast.
    /// </summary>
    public class ThreatTests
    {
        const float Dt = 0.02f;
        static readonly PlayerId Tank = new PlayerId(1);
        static readonly PlayerId Mage = new PlayerId(2);

        static readonly MobAttack Gore = new MobAttack
        {
            Kind = MobAttackKind.Melee, Damage = 8f, MinRange = 0f, Range = 1.8f, Windup = 0.4f, Cooldown = 1.5f, Knockback = 3f,
        };

        MobDefinition _mob;
        MobRules _rules;
        MobBrain _brain;
        readonly List<HeroSense> _heroes = new List<HeroSense>();
        float _now;

        [SetUp]
        public void SetUp()
        {
            // Melee only and lots of health, so nothing charges or dies mid-test.
            _mob = MobDefinition.Create(MobId.BristlebackBoar, "Test Boar", 10000f, MotorSettings.Default,
                MobTemperament.Neutral, aggroRadius: 12f, leashRadius: 30f, Gore);
            _rules = new MobRules(_mob.MaxHealth);
            _brain = new MobBrain(_mob, _rules, float3.zero, seed: 7u);
            _heroes.Clear();
            _now = 0f;
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_mob);

        void Hero(PlayerId id, float3 feet, bool knockedOut = false)
        {
            _heroes.RemoveAll(h => h.Id == id);
            _heroes.Add(new HeroSense { Id = id, Feet = feet, KnockedOut = knockedOut });
        }

        void Tick()
        {
            _now += Dt;
            _brain.Tick(new MobSenses { Now = _now, Dt = Dt, Feet = float3.zero, Heroes = _heroes });
        }

        /// <summary>Long enough for a whole gore (windup, recover) so it's free to change its mind.</summary>
        void RunFor(float seconds)
        {
            float until = _now + seconds;
            while (_now < until)
                Tick();
        }

        static ThreatPick Ranged(float now = 0f) => new ThreatPick { Now = now };

        // ---- The table on its own ----

        [Test]
        public void Table_AddsUp_AndIgnoresNobodyAndNothing()
        {
            var table = new ThreatTable();
            table.Add(Tank, 10f);
            table.Add(Tank, 5f);
            table.Add(PlayerId.None, 50f);
            table.Add(Mage, 0f);
            table.Add(Mage, -3f);
            Assert.AreEqual(15f, table.Of(Tank), 1e-4f);
            Assert.IsFalse(table.Contains(Mage));
            Assert.AreEqual(1, table.Entries.Count);
        }

        [Test]
        public void Table_StaysOnItsTarget_UntilPassedByTheRangedMargin()
        {
            var table = new ThreatTable();
            table.Add(Tank, 100f);
            table.Add(Mage, 125f);
            Assert.AreEqual(Tank, table.PickTarget(Tank, Ranged()), "125% isn't enough at range.");
            table.Add(Mage, 10f);
            Assert.AreEqual(Mage, table.PickTarget(Tank, Ranged()), "135% pulls it.");
            Assert.AreEqual(Mage, table.PickTarget(PlayerId.None, Ranged()), "With no target yet, the top of the table.");
        }

        [Test]
        public void Table_InMelee_TheMarginIsSmaller()
        {
            var table = new ThreatTable();
            table.Add(Tank, 100f);
            table.Add(Mage, 115f);
            var heroes = new List<HeroSense>
            {
                new HeroSense { Id = Tank, Feet = new float3(1f, 0f, 0f) },
                new HeroSense { Id = Mage, Feet = new float3(20f, 0f, 0f) },
            };
            var pick = new ThreatPick { Heroes = heroes, MeleeReach = 1.8f };
            Assert.AreEqual(Tank, table.PickTarget(Tank, pick), "115% from 20 m away isn't enough.");

            heroes[1] = new HeroSense { Id = Mage, Feet = new float3(0f, 0f, 1.5f) };
            Assert.AreEqual(Mage, table.PickTarget(Tank, pick), "115% standing beside it is.");
        }

        [Test]
        public void Table_SkipsHeroesKnockedOut_Gone_OrPastTheLeash()
        {
            var table = new ThreatTable();
            table.Add(Tank, 100f);
            table.Add(Mage, 500f);
            var heroes = new List<HeroSense>
            {
                new HeroSense { Id = Tank, Feet = new float3(1f, 0f, 0f) },
                new HeroSense { Id = Mage, Feet = new float3(35f, 0f, 0f) },
            };
            var pick = new ThreatPick { Heroes = heroes, MeleeReach = 1.8f, Leash = 30f };
            Assert.AreEqual(Tank, table.PickTarget(Tank, pick), "The mage is past its leash.");

            heroes[1] = new HeroSense { Id = Mage, Feet = new float3(5f, 0f, 0f), KnockedOut = true };
            Assert.AreEqual(Tank, table.PickTarget(Tank, pick), "Knocked out.");

            heroes.RemoveAt(0);
            Assert.IsFalse(table.PickTarget(Tank, pick).IsValid, "Nobody on the table is about.");
        }

        [Test]
        public void Taunt_JumpsToTheTop_AndHoldsItForItsTime()
        {
            var table = new ThreatTable();
            table.Add(Tank, 50f);
            table.Add(Mage, 200f);
            table.Taunt(Tank, now: 10f, seconds: 3f);
            Assert.AreEqual(200f, table.Of(Tank), 1e-4f, "The taunter's threat matches the top.");
            Assert.AreEqual(Tank, table.TauntedBy(11f));

            table.Add(Mage, 1000f);
            Assert.AreEqual(Tank, table.PickTarget(Mage, Ranged(12.9f)), "Locked on the taunter, whatever the mage does.");
            Assert.IsFalse(table.TauntedBy(13f).IsValid);
            Assert.AreEqual(Mage, table.PickTarget(Tank, Ranged(13f)), "The taunt is over and the mage is far ahead.");
        }

        [Test]
        public void Taunt_ByANewcomer_PutsThemOnTheTable()
        {
            var table = new ThreatTable();
            table.Taunt(Tank, 0f, 3f);
            Assert.IsTrue(table.Contains(Tank));
            Assert.AreEqual(Tank, table.PickTarget(PlayerId.None, Ranged(1f)));
        }

        [Test]
        public void RemoveAndClear_ForgetHeroesAndTaunts()
        {
            var table = new ThreatTable();
            table.Add(Mage, 10f);
            table.Taunt(Tank, 0f, 3f);
            table.Remove(Tank);
            Assert.IsFalse(table.Contains(Tank));
            Assert.IsFalse(table.TauntedBy(1f).IsValid, "A knocked-out taunter's taunt ends.");

            table.Taunt(Tank, 0f, 3f);
            table.Clear();
            Assert.IsTrue(table.IsEmpty);
            Assert.IsFalse(table.TauntedBy(1f).IsValid);
        }

        [Test]
        public void Rules_DamageAndStuns_BuildThreat_AndGoingHomeForgetsIt()
        {
            _rules.Hit(40f, Tank, threatScale: 2f);
            _rules.Stun(0f, 1f, Mage);
            Assert.AreEqual(80f, _rules.Threat.Of(Tank), 1e-4f);
            Assert.AreEqual(ThreatRules.StunThreat, _rules.Threat.Of(Mage), 1e-4f);
            _rules.Reset();
            Assert.IsTrue(_rules.Threat.IsEmpty);
        }

        [Test]
        public void Rules_StunsScale_LikeDamage()
        {
            _rules.Stun(0f, 1f, Tank, threatScale: 2f);
            Assert.AreEqual(ThreatRules.StunThreat * 2f, _rules.Threat.Of(Tank), 1e-4f, "A Paladin's stun counts double.");
        }

        [Test]
        public void Support_IsHalfTheHeal_SplitAmongTheFoesFightingThem()
        {
            Assert.AreEqual(20f, ThreatRules.Support(40f), 1e-4f);
            Assert.AreEqual(40f, ThreatRules.Support(40f, 2f), 1e-4f);
            Assert.AreEqual(0f, ThreatRules.Support(-5f), 1e-4f);

            var fighting = new ThreatTable();
            var alsoFighting = new ThreatTable();
            var elsewhere = new ThreatTable();
            fighting.Add(Tank, 10f);
            alsoFighting.Add(Tank, 10f);
            elsewhere.Add(Mage, 10f);
            int angered = ThreatRules.ShareSupport(new[] { fighting, alsoFighting, elsewhere, null }, Mage, Tank, 30f);
            Assert.AreEqual(2, angered);
            Assert.AreEqual(15f, fighting.Of(Mage), 1e-4f);
            Assert.AreEqual(15f, alsoFighting.Of(Mage), 1e-4f);
            Assert.AreEqual(10f, elsewhere.Of(Mage), 1e-4f, "A foe that isn't fighting the tank isn't angered.");
            Assert.AreEqual(0, ThreatRules.ShareSupport(new[] { elsewhere }, Mage, Tank, 30f));
        }

        [Test]
        public void Paladin_MakesDoubleThreat_SoAMageNeedsMoreThanTwiceTheDamageToPullIt()
        {
            // What CombatTargets passes in for a Runeforged Paladin's Rune Hammer: the class's 2 times the hammer's 1.5.
            Hero(Tank, new float3(1f, 0f, 0f));
            Hero(Mage, new float3(20f, 0f, 0f));
            _rules.Hit(40f, Tank, threatScale: 2f * 1.5f);
            RunFor(1.5f);
            _rules.Hit(150f, Mage);
            RunFor(1.5f);
            Assert.AreEqual(Tank, _brain.Target, "150 from the mage isn't 130% of the Paladin's 121.");
        }

        // ---- Mobs ----

        [Test]
        public void Mob_KeepsBitingTheTank_UntilTheMageNearlyMatchesThem_InMelee()
        {
            Hero(Tank, new float3(1f, 0f, 0f));
            Hero(Mage, new float3(0f, 0f, 1.5f));
            _rules.Hit(100f, Tank);
            RunFor(1.5f);
            Assert.AreEqual(Tank, _brain.Target);

            _rules.Hit(105f, Mage);
            RunFor(1.5f);
            Assert.AreEqual(Tank, _brain.Target, "105% isn't 110%.");

            _rules.Hit(10f, Mage);
            RunFor(1.5f);
            Assert.AreEqual(Mage, _brain.Target, "115% in melee: the mage pulled it.");
        }

        [Test]
        public void Mob_AMageAtRange_NeedsMore()
        {
            Hero(Tank, new float3(1f, 0f, 0f));
            Hero(Mage, new float3(20f, 0f, 0f));
            _rules.Hit(100f, Tank);
            RunFor(1.5f);
            _rules.Hit(120f, Mage);
            RunFor(1.5f);
            Assert.AreEqual(Tank, _brain.Target, "120% from 20 m isn't 130%.");

            _rules.Hit(15f, Mage);
            RunFor(1.5f);
            Assert.AreEqual(Mage, _brain.Target);
        }

        [Test]
        public void Mob_FinishesItsAttack_OnTheTank_BeforeChangingItsMind()
        {
            Hero(Tank, new float3(1f, 0f, 0f));
            Hero(Mage, new float3(0f, 0f, 1.5f));
            _rules.Hit(100f, Tank);
            for (int i = 0; i < 100 && _brain.State != MobState.Windup; i++)
                Tick();
            Assert.AreEqual(MobState.Windup, _brain.State);

            _rules.Hit(500f, Mage);
            bool struckTank = false;
            for (int i = 0; i < 40 && _brain.State == MobState.Windup; i++)
            {
                Tick();
                Assert.AreEqual(Tank, _brain.Target, "No switching mid-windup.");
                foreach (MobEvent e in _brain.Events)
                    struckTank |= e.Kind == MobEventKind.Strike && e.Target == Tank;
            }
            Assert.IsTrue(struckTank, "The gore it was winding up lands on the tank.");
            RunFor(1f);
            Assert.AreEqual(Mage, _brain.Target);
        }

        [Test]
        public void Mob_TankKnockedOut_LosesTheirThreat_AndTheNextHeroIsTheTarget()
        {
            Hero(Tank, new float3(1f, 0f, 0f));
            Hero(Mage, new float3(0f, 0f, 1.5f));
            _rules.Hit(300f, Tank);
            _rules.Hit(50f, Mage);
            RunFor(1.5f);
            Assert.AreEqual(Tank, _brain.Target);

            Hero(Tank, new float3(1f, 0f, 0f), knockedOut: true);
            RunFor(1.5f);
            Assert.AreEqual(Mage, _brain.Target);
            Assert.IsFalse(_rules.Threat.Contains(Tank), "Waking up, the tank starts again from nothing.");
        }

        [Test]
        public void Mob_IgnoresAHeroPastItsLeash_HoweverAngryItIs()
        {
            Hero(Tank, new float3(1f, 0f, 0f));
            Hero(Mage, new float3(0f, 0f, 35f));
            _rules.Hit(100f, Tank);
            RunFor(1.5f);
            _rules.Hit(1000f, Mage);
            RunFor(1.5f);
            Assert.AreEqual(Tank, _brain.Target);
        }

        [Test]
        public void Mob_ATaunt_TurnsItOnTheTaunter()
        {
            Hero(Tank, new float3(1f, 0f, 0f));
            Hero(Mage, new float3(0f, 0f, 1.5f));
            _rules.Hit(500f, Mage);
            RunFor(1.5f);
            Assert.AreEqual(Mage, _brain.Target);

            _rules.Threat.Taunt(Tank, _now, 3f);
            RunFor(1.5f);
            Assert.AreEqual(Tank, _brain.Target);
        }

        [Test]
        public void Mob_TauntedWhileMindingItsOwnBusiness_ComesForTheTaunter()
        {
            Hero(Tank, new float3(20f, 0f, 0f));
            Tick();
            Assert.AreNotEqual(MobState.Chase, _brain.State, "A neutral mob leaves a hero walking by alone.");

            Assert.IsTrue(_rules.Taunt(_now, 3f, Tank));
            Tick();
            Assert.AreEqual(MobState.Chase, _brain.State);
            Assert.AreEqual(Tank, _brain.Target);
        }

        [Test]
        public void Mob_Defeated_CantBeTaunted()
        {
            _rules.Hit(_mob.MaxHealth, Mage);
            Assert.IsFalse(_rules.Taunt(0f, 3f, Tank));
            Assert.IsFalse(_rules.Threat.Contains(Tank));
        }

        [Test]
        public void Mob_JoiningItsPack_PutsTheHeroOnItsTable()
        {
            Hero(Tank, new float3(5f, 0f, 0f));
            Assert.IsTrue(_brain.Join(Tank, 0f));
            Assert.IsTrue(_rules.Threat.Contains(Tank));
        }

        // ---- Training Dummies ----

        [Test]
        public void Dummy_ThrowsAtTheTopOfItsTable_AndSticksWithThem()
        {
            var dummy = new SparringDummy(0f);
            dummy.Hit(45f, Tank, 1f, out _);
            dummy.Hit(10f, Mage, 2f, out _);
            Assert.IsTrue(dummy.TrySpar(3.1f, out PlayerId target));
            Assert.AreEqual(Tank, target, "The tank hit it hardest, though the mage hit it last.");

            dummy.Hit(45f, Mage, 4f, out _);
            Assert.IsTrue(dummy.TrySpar(6.2f, out target));
            Assert.AreEqual(Tank, target, "55 is only 122% of 45: not enough to pull a dummy, which throws.");

            dummy.Hit(10f, Mage, 7f, out _);
            Assert.IsTrue(dummy.TrySpar(9.3f, out target));
            Assert.AreEqual(Mage, target);
        }

        [Test]
        public void Dummy_Taunted_ThrowsOnlyAtTheTaunter()
        {
            var dummy = new SparringDummy(0f);
            dummy.Hit(200f, Mage, 1f, out _);
            dummy.Taunt(2f, 3f, Tank);
            Assert.IsTrue(dummy.TrySpar(3.1f, out PlayerId target));
            Assert.AreEqual(Tank, target, "Mid-taunt, the tank gets the bale though the mage hit it far harder.");
        }

        [Test]
        public void Dummy_LeftAlone_ForgetsEveryone()
        {
            var dummy = new SparringDummy(0f);
            dummy.Hit(45f, Tank, 1f, out _);
            dummy.Hit(5f, Mage, 1f + SparringDummy.ResetAfter + 1f, out _);
            Assert.IsFalse(dummy.Threat.Contains(Tank));
            Assert.AreEqual(5f, dummy.Threat.Of(Mage), 1e-4f);
        }
    }
}
