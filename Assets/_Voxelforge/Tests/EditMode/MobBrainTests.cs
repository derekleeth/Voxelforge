using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using Voxelforge.Core;
using Voxelforge.Gameplay;
using Voxelforge.Voxels;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 5.2 step B: what a mob decides to do, tick by tick, and the Bristleback Boar's charge.</summary>
    public class MobBrainTests
    {
        const float Dt = 0.02f;
        static readonly PlayerId P1 = new PlayerId(1);
        static readonly PlayerId P2 = new PlayerId(2);

        static readonly MobAttack Gore = new MobAttack
        {
            Kind = MobAttackKind.Melee, Damage = 8f, MinRange = 0f, Range = 1.8f, Windup = 0.4f, Cooldown = 1.5f, Knockback = 3f,
        };

        static readonly MobAttack Rush = new MobAttack
        {
            Kind = MobAttackKind.Charge, Damage = 14f, MinRange = 6f, Range = 14f, Windup = 0.8f, Cooldown = 8f, Knockback = 9f,
            Lift = 4f, ChargeSpeed = 2.2f, ChargeDistance = 18f, DazedSeconds = 1.5f,
        };

        MobDefinition _boar;
        MobRules _rules;
        MobBrain _brain;
        readonly List<HeroSense> _heroes = new List<HeroSense>();

        [SetUp]
        public void SetUp()
        {
            _boar = MobDefinition.Create(MobId.BristlebackBoar, "Bristleback Boar", 60f, MotorSettings.Default,
                MobTemperament.Aggressive, aggroRadius: 12f, leashRadius: 30f, Gore, Rush);
            _rules = new MobRules(_boar.MaxHealth);
            _brain = new MobBrain(_boar, _rules, float3.zero, seed: 7u);
            _heroes.Clear();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_boar);

        MobIntent Tick(float now, float3 feet, float moved = 0f) =>
            _brain.Tick(new MobSenses { Now = now, Dt = Dt, Feet = feet, Moved = moved, Heroes = _heroes });

        void Hero(PlayerId id, float3 feet, bool knockedOut = false)
        {
            _heroes.RemoveAll(h => h.Id == id);
            _heroes.Add(new HeroSense { Id = id, Feet = feet, KnockedOut = knockedOut });
        }

        bool Did(MobEventKind kind, out MobEvent found)
        {
            foreach (MobEvent e in _brain.Events)
            {
                if (e.Kind == kind)
                {
                    found = e;
                    return true;
                }
            }
            found = default;
            return false;
        }

        [Test]
        public void Idle_WandersNearHome_WhenNobodyIsAbout()
        {
            Assert.AreEqual(MobState.Idle, _brain.State);
            MobIntent intent = Tick(10f, float3.zero);
            Assert.AreEqual(MobState.Wander, _brain.State, "After a few seconds of standing about, it goes for a stroll.");
            intent = Tick(10.02f, float3.zero);
            Assert.AreEqual(MobMove.PathTo, intent.Move);
            Assert.AreEqual(1f, intent.SpeedScale, "It walks, not runs.");
            Assert.LessOrEqual(math.length(intent.Destination.xz), _boar.WanderRadius + 1e-3f);
        }

        [Test]
        public void Aggressive_ChasesTheNearestHeroInRange_AtARun()
        {
            Hero(P1, new float3(20f, 0f, 0f));
            Hero(P2, new float3(4f, 0f, 3f));
            MobIntent intent = Tick(0.1f, float3.zero);

            Assert.IsTrue(Did(MobEventKind.Aggro, out MobEvent aggro));
            Assert.AreEqual(P2, aggro.Target, "P1 is outside its aggro radius.");
            Assert.AreEqual(MobState.Chase, _brain.State);
            Assert.AreEqual(MobMove.PathTo, intent.Move);
            Assert.AreEqual(new float3(4f, 0f, 3f), intent.Destination);
            Assert.AreEqual(_boar.RunSpeed, intent.SpeedScale);
        }

        [Test]
        public void Neutral_LeavesHeroesAlone_UntilHit_ThenGoesAfterTheAttacker()
        {
            MobDefinition rhino = MobDefinition.Create(MobId.ThunderRhino, "Rhino", 200f, MotorSettings.Default,
                MobTemperament.Neutral, 12f, 30f, Gore);
            var rules = new MobRules(200f);
            var brain = new MobBrain(rhino, rules, float3.zero, 3u);
            Hero(P1, new float3(3f, 0f, 0f));
            brain.Tick(new MobSenses { Now = 0.1f, Dt = Dt, Heroes = _heroes });
            Assert.AreNotEqual(MobState.Chase, brain.State, "A hero walking by is no reason to fight.");

            rules.Hit(10f, P1);
            brain.Tick(new MobSenses { Now = 0.2f, Dt = Dt, Heroes = _heroes });
            Assert.AreEqual(MobState.Chase, brain.State);
            Assert.AreEqual(P1, brain.Target);
            Object.DestroyImmediate(rhino);
        }

        [Test]
        public void Hit_FromFarAway_StillStartsAFight()
        {
            Hero(P1, new float3(25f, 0f, 0f));
            Tick(0.1f, float3.zero);
            Assert.AreNotEqual(MobState.Chase, _brain.State);
            _rules.Hit(5f, P1);
            Tick(0.2f, float3.zero);
            Assert.AreEqual(P1, _brain.Target, "Shot from 25 m: it comes for the shooter.");
        }

        [Test]
        public void Melee_WindsUp_ThenStrikes_IfTheHeroIsStillInReach()
        {
            Hero(P1, new float3(1.2f, 0f, 0f));
            Tick(0.1f, float3.zero);
            Assert.AreEqual(MobState.Windup, _brain.State, "Aggro and the first gore start together.");
            Assert.IsTrue(Did(MobEventKind.Windup, out MobEvent windup));
            Assert.AreEqual(0, windup.Attack);

            Tick(0.3f, float3.zero);
            Assert.IsFalse(Did(MobEventKind.Strike, out _), "Still winding up.");
            Tick(0.5f, float3.zero);
            Assert.IsTrue(Did(MobEventKind.Strike, out MobEvent strike));
            Assert.AreEqual(P1, strike.Target);
            Assert.AreEqual(new float2(1f, 0f), strike.Direction, "It knocks them away from itself.");
            Assert.AreEqual(MobState.Recover, _brain.State);

            // On cooldown: it stands and faces them, but doesn't wind up again.
            Tick(1.1f, float3.zero);
            MobIntent waiting = Tick(1.2f, float3.zero);
            Assert.AreEqual(MobState.Chase, _brain.State);
            Assert.IsTrue(waiting.Face);
            Assert.AreEqual(MobMove.Stand, waiting.Move);
            Tick(1.61f, float3.zero);
            Assert.AreEqual(MobState.Windup, _brain.State, "Cooled down: another gore.");
        }

        [Test]
        public void Melee_Misses_IfTheHeroSteppedAway()
        {
            Hero(P1, new float3(1.2f, 0f, 0f));
            Tick(0.1f, float3.zero);
            Hero(P1, new float3(1.8f + MobBrain.MeleeGrace + 0.5f, 0f, 0f));
            Tick(0.6f, float3.zero);
            Assert.IsFalse(Did(MobEventKind.Strike, out _));
            Assert.AreEqual(MobState.Recover, _brain.State);
        }

        [Test]
        public void Charge_IsTelegraphed_ThenRunsStraight_AndBowlsOverWhoeverItReaches()
        {
            Hero(P1, new float3(0f, 0f, 10f));
            MobIntent intent = Tick(0.1f, float3.zero);
            Assert.AreEqual(MobState.Windup, _brain.State);
            Assert.AreEqual(1, _brain.CurrentAttack, "10 m away: the charge, not the gore.");
            Assert.IsTrue(intent.Face);
            Assert.AreEqual(MobMove.Stand, intent.Move, "It paws the ground first.");

            // The hero steps aside during the telegraph; the line was already chosen.
            Hero(P1, new float3(4f, 0f, 10f));
            Tick(0.5f, float3.zero);
            intent = Tick(0.91f, float3.zero);
            Assert.AreEqual(MobState.Charge, _brain.State);
            Assert.AreEqual(MobMove.Straight, intent.Move);
            Assert.AreEqual(new float2(0f, 1f), intent.Direction);
            Assert.AreEqual(Rush.ChargeSpeed, intent.SpeedScale);

            // It thunders past where the hero used to stand and hits P2, who walked into its path.
            Hero(P2, new float3(0.5f, 0f, 6f));
            float3 feet = float3.zero;
            float speed = MotorSettings.Default.WalkSpeed * Rush.ChargeSpeed;
            float now = 0.91f;
            for (int i = 0; i < 100 && _brain.State == MobState.Charge; i++)
            {
                feet.z += speed * Dt;
                now += Dt;
                Tick(now, feet, speed * Dt);
                if (Did(MobEventKind.Strike, out MobEvent hit))
                {
                    Assert.AreEqual(P2, hit.Target);
                    Assert.AreEqual(new float2(0f, 1f), hit.Direction, "Thrown along the charge's line.");
                }
            }
            Assert.AreEqual(MobState.Recover, _brain.State);
            Assert.Less(feet.z, 6f, "It hit P2 as it reached them.");
        }

        [Test]
        public void Charge_OnlyAtHeroesOnTheLevel()
        {
            // Up on a ledge 6 blocks higher: it runs round to them instead of charging the cliff.
            Hero(P1, new float3(0f, 6f, 6f));
            MobIntent intent = Tick(0.1f, float3.zero);
            Assert.AreEqual(MobState.Chase, _brain.State);
            Assert.AreEqual(MobMove.PathTo, intent.Move);
        }

        [Test]
        public void Charge_IntoAWall_LeavesItDazed()
        {
            Hero(P1, new float3(0f, 0f, 10f));
            Tick(0.1f, float3.zero);
            Tick(0.91f, float3.zero);
            Assert.AreEqual(MobState.Charge, _brain.State);
            float now = 0.91f;
            for (int i = 0; i < 20 && _brain.State == MobState.Charge; i++)
            {
                now += Dt;
                Tick(now, float3.zero, moved: 0f);
            }
            Assert.AreEqual(MobState.Dazed, _brain.State);
            Tick(now + Rush.DazedSeconds - 0.05f, float3.zero);
            Assert.AreEqual(MobState.Dazed, _brain.State);
            Tick(now + Rush.DazedSeconds + 0.01f, float3.zero);
            Assert.AreEqual(MobState.Chase, _brain.State, "It shakes it off and goes after them again.");
        }

        [Test]
        public void Charge_GivesUp_AfterItsDistance()
        {
            Hero(P1, new float3(0f, 0f, 10f));
            Tick(0.1f, float3.zero);
            Tick(0.91f, float3.zero);
            Hero(P1, new float3(20f, 0f, 0f)); // Well out of the way.
            float3 feet = float3.zero;
            float step = MotorSettings.Default.WalkSpeed * Rush.ChargeSpeed * Dt;
            float now = 0.91f;
            int ticks = 0;
            while (_brain.State == MobState.Charge && ticks++ < 500)
            {
                feet.z += step;
                now += Dt;
                Tick(now, feet, step);
            }
            Assert.AreEqual(MobState.Recover, _brain.State);
            Assert.AreEqual(Rush.ChargeDistance, feet.z, step * 1.5f);
        }

        [Test]
        public void Leash_RunsHome_ThenHealsAndForgets()
        {
            Hero(P1, new float3(5f, 0f, 0f));
            _rules.Hit(30f, P1);
            Tick(0.1f, new float3(3f, 0f, 0f));
            Assert.AreEqual(P1, _brain.Target);

            MobIntent intent = Tick(0.2f, new float3(31f, 0f, 0f));
            Assert.AreEqual(MobState.Return, _brain.State, "Dragged past its leash: it gives up.");
            Assert.IsFalse(_brain.Target.IsValid);
            Assert.AreEqual(MobMove.PathTo, intent.Move);
            Assert.AreEqual(float3.zero, intent.Destination);

            Tick(0.3f, new float3(20f, 0f, 0f));
            Assert.AreEqual(MobState.Return, _brain.State, "It ignores heroes on the way home.");

            Hero(P1, new float3(40f, 0f, 0f));
            Tick(5f, new float3(0.5f, 0f, 0f));
            Assert.IsTrue(Did(MobEventKind.Returned, out _));
            Assert.AreEqual(MobState.Idle, _brain.State);
            Assert.AreEqual(_rules.Max, _rules.Health, "Home again: back to full health.");
        }

        [Test]
        public void KnockedOutHeroes_AreLeftAlone()
        {
            Hero(P1, new float3(1f, 0f, 0f), knockedOut: true);
            Tick(0.1f, float3.zero);
            Assert.AreNotEqual(MobState.Chase, _brain.State);
            Assert.AreNotEqual(MobState.Windup, _brain.State);

            Hero(P1, new float3(1f, 0f, 0f));
            Hero(P2, new float3(8f, 0f, 0f));
            Tick(0.2f, float3.zero);
            Assert.AreEqual(P1, _brain.Target);

            // P1 goes down mid-gore: the gore finds nobody, then it turns on P2, who's also in range.
            Hero(P1, new float3(1f, 0f, 0f), knockedOut: true);
            Tick(1f, float3.zero);
            Assert.IsFalse(Did(MobEventKind.Strike, out _));
            Tick(1.6f, float3.zero);
            Assert.AreEqual(P2, _brain.Target);

            // P2 goes down too: once it's done with its charge, nobody is left, so it goes home.
            Hero(P2, new float3(8f, 0f, 0f), knockedOut: true);
            for (float now = 1.7f; now < 8f && _brain.State != MobState.Return; now += Dt)
                Tick(now, new float3(2f, 0f, 0f));
            Assert.AreEqual(MobState.Return, _brain.State);
        }

        [Test]
        public void Stun_FreezesIt_ThenItCarriesOn()
        {
            Hero(P1, new float3(5f, 0f, 0f));
            Tick(0.1f, float3.zero);
            Assert.AreEqual(MobState.Chase, _brain.State);
            _rules.Stun(0.2f, 2f, P1);
            MobIntent intent = Tick(0.3f, float3.zero);
            Assert.AreEqual(MobState.Stunned, _brain.State);
            Assert.AreEqual(MobMove.Stand, intent.Move);
            Tick(2.3f, float3.zero);
            Assert.AreEqual(MobState.Chase, _brain.State);
        }

        [Test]
        public void Defeated_StaysDefeated()
        {
            Hero(P1, new float3(1f, 0f, 0f));
            TargetDamage hit = _rules.Hit(100f, P1);
            Assert.IsTrue(hit.Toppled);
            Assert.AreEqual(60f, hit.Dealt);
            MobIntent intent = Tick(0.1f, float3.zero);
            Assert.AreEqual(MobState.Defeated, _brain.State);
            Assert.AreEqual(MobMove.Stand, intent.Move);
            Assert.IsFalse(_rules.Stun(0.2f, 5f, P1), "No stunning what's already down.");
            Assert.AreEqual(default(TargetDamage).Dealt, _rules.Hit(5f, P1).Dealt);
        }

        [Test]
        public void Book_FindsProblems()
        {
            MobDefinition noPrefab = MobDefinition.Create(MobId.BristlebackBoar, "Boar", 60f, MotorSettings.Default,
                MobTemperament.Aggressive, 12f, 30f, Gore);
            MotorSettings huge = MotorSettings.Default;
            huge.HalfExtents = new float3(0.8f, 1.5f, 0.8f);
            MobDefinition giant = MobDefinition.Create(MobId.BristlebackBoar, "Giant", 60f, huge, MobTemperament.Aggressive,
                12f, 30f);
            MobBook book = MobBook.Create(new[] { noPrefab, giant, null });
            List<string> errors = book.Validate();
            Assert.That(errors, Has.Some.Contains("no prefab"));
            Assert.That(errors, Has.Some.Contains("already uses"));
            Assert.That(errors, Has.Some.Contains("nav grid"));
            Assert.That(errors, Has.Some.Contains("no attacks"));
            Assert.That(errors, Has.Some.Contains("empty"));
            Object.DestroyImmediate(noPrefab);
            Object.DestroyImmediate(giant);
            Object.DestroyImmediate(book);
        }
    }
}
