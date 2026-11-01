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
    /// Roadmap 5.2 step C: the Frostfang Wolf's pack (howl, take turns, circle, flee), the Thunder Rhino's herd (join in,
    /// calm down) and the Crystal Slime's split rules in the <see cref="MobBook"/>.
    /// </summary>
    public class MobBehaviourTests
    {
        const float Dt = 0.02f;
        const ulong Self = 5;
        static readonly PlayerId P1 = new PlayerId(1);
        static readonly PlayerId P2 = new PlayerId(2);

        static readonly MobAttack Bite = new MobAttack
        {
            Kind = MobAttackKind.Melee, Damage = 6f, MinRange = 0f, Range = 1.6f, Windup = 0.3f, Cooldown = 1.2f, Knockback = 2f,
        };

        MobDefinition _wolf;
        MobDefinition _rhino;
        readonly List<HeroSense> _heroes = new List<HeroSense>();
        readonly List<AllySense> _allies = new List<AllySense>();

        [SetUp]
        public void SetUp()
        {
            _wolf = MobDefinition.Create(MobId.FrostfangWolf, "Frostfang Wolf", 45f, MotorSettings.Default,
                    MobTemperament.Aggressive, aggroRadius: 14f, leashRadius: 35f, Bite)
                .WithPack(callRadius: 25f, callSeconds: 0.9f, maxBiters: 2, circleRadius: 4f, fleeBelowShare: 0.2f);
            _rhino = MobDefinition.Create(MobId.ThunderRhino, "Thunder Rhino", 200f, MotorSettings.Default,
                    MobTemperament.Neutral, aggroRadius: 0f, leashRadius: 30f, Bite)
                .WithHerd(callRadius: 15f, calmSeconds: 12f);
            _heroes.Clear();
            _allies.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_wolf);
            Object.DestroyImmediate(_rhino);
        }

        MobIntent Tick(MobBrain brain, float now, float3 feet) =>
            brain.Tick(new MobSenses { Now = now, Dt = Dt, Feet = feet, Heroes = _heroes, Self = Self, Allies = _allies });

        void Hero(PlayerId id, float3 feet) => _heroes.Add(new HeroSense { Id = id, Feet = feet });

        void Ally(ulong id, float3 feet, bool biting, PlayerId target = default, bool resting = false) =>
            _allies.Add(new AllySense
            {
                Id = id, Feet = feet, Target = target.IsValid ? target : P1, Fighting = true, Biting = biting, Resting = resting,
            });

        static bool Did(MobBrain brain, MobEventKind kind)
        {
            foreach (MobEvent e in brain.Events)
            {
                if (e.Kind == kind)
                    return true;
            }
            return false;
        }

        [Test]
        public void Wolf_HowlsFirst_ThenChases()
        {
            var brain = new MobBrain(_wolf, new MobRules(45f), float3.zero, 7u);
            Hero(P1, new float3(8f, 0f, 0f));

            MobIntent intent = Tick(brain, 0.1f, float3.zero);
            Assert.IsTrue(Did(brain, MobEventKind.Aggro), "The aggro is what calls the pack.");
            Assert.AreEqual(MobState.Howl, brain.State);
            Assert.AreEqual(MobMove.Stand, intent.Move, "It stands still to howl...");
            Assert.IsTrue(intent.Face, "...facing the hero.");
            Assert.IsTrue(brain.IsFighting);

            Tick(brain, 0.9f, float3.zero);
            Assert.AreEqual(MobState.Howl, brain.State);
            intent = Tick(brain, 1.05f, float3.zero);
            Assert.AreEqual(MobState.Chase, brain.State, "Then the chase.");
            Assert.AreEqual(MobMove.PathTo, intent.Move);
        }

        [Test]
        public void Join_BringsIdlePackmatesIn_WithoutCallingAgain()
        {
            var brain = new MobBrain(_wolf, new MobRules(45f), float3.zero, 7u);
            Hero(P1, new float3(20f, 0f, 0f)); // Outside its own aggro radius.

            Assert.IsTrue(brain.Join(P1, 0.1f));
            Assert.AreEqual(P1, brain.Target);
            Tick(brain, 0.12f, float3.zero);
            Assert.AreEqual(MobState.Chase, brain.State, "It doesn't howl: it was called.");
            Assert.IsFalse(Did(brain, MobEventKind.Aggro), "So it doesn't call the pack again.");
            Assert.IsFalse(brain.Join(P2, 0.2f), "Already fighting.");
            Assert.AreEqual(P1, brain.Target);
        }

        [Test]
        public void OnlyTwoAttack_TheRestCircle_AndTheNextMovesInWhenThere_IsRoom()
        {
            var brain = new MobBrain(_wolf, new MobRules(45f), float3.zero, 7u);
            Hero(P1, float3.zero);
            var feet = new float3(3f, 0f, 0f);
            brain.Join(P1, 0f);
            Ally(3, new float3(1f, 0f, 0f), biting: true);
            Ally(4, new float3(-1f, 0f, 0f), biting: true);

            MobIntent intent = Tick(brain, 0.1f, feet);
            Assert.AreEqual(MobState.Circle, brain.State, "Two packmates are already biting.");
            Assert.IsFalse(brain.IsBiting);
            Assert.AreEqual(MobMove.PathTo, intent.Move);
            Assert.AreEqual(_wolf.CircleRadius, math.length(intent.Destination.xz), 1e-3f, "It circles 4 m out...");
            Assert.IsTrue(intent.Face, "...watching the hero.");
            Assert.AreEqual(1f, intent.SpeedScale, "At a walk.");

            float before = math.atan2(intent.Destination.x, intent.Destination.z);
            intent = Tick(brain, 0.12f, feet);
            Assert.AreNotEqual(before, math.atan2(intent.Destination.x, intent.Destination.z), "It keeps going round.");

            // One biter falls back: there's room, so the one circling goes in.
            _allies.RemoveAt(1);
            Ally(4, new float3(-6f, 0f, 0f), biting: false);
            Tick(brain, 0.14f, feet);
            Assert.AreEqual(MobState.Chase, brain.State);
            Assert.IsTrue(brain.IsBiting);
        }

        [Test]
        public void Packmates_WaitingTheirTurn_AgreeWhoGoesNext()
        {
            // MaxBiters 2, one biting already, and two circling at the same distance: only the lower id goes in.
            var brain = new MobBrain(_wolf, new MobRules(45f), float3.zero, 7u);
            Hero(P1, float3.zero);
            var feet = new float3(4f, 0f, 0f);
            brain.Join(P1, 0f);
            Ally(9, new float3(1f, 0f, 0f), biting: true);
            Ally(10, new float3(2f, 0f, 0f), biting: true);
            Tick(brain, 0.1f, feet);
            Assert.AreEqual(MobState.Circle, brain.State);

            _allies.Clear();
            Ally(9, new float3(1f, 0f, 0f), biting: true);
            Ally(2, new float3(0f, 0f, 4f), biting: false);
            Tick(brain, 0.12f, feet);
            Assert.AreEqual(MobState.Circle, brain.State, "Wolf 2 is just as close, and goes first.");

            _allies.RemoveAt(1);
            Ally(8, new float3(0f, 0f, 4f), biting: false);
            Tick(brain, 0.14f, feet);
            Assert.AreEqual(MobState.Chase, brain.State, "It goes before wolf 8.");
        }

        [Test]
        public void Biter_StepsOutAfterItsTurn_OnlyIfAPackmateIsWaiting()
        {
            Assert.AreEqual(MobState.Circle, BiteUntilTurnIsOver(somebodyWaiting: true), "It lets the waiting wolf in.");
            Assert.AreNotEqual(MobState.Circle, BiteUntilTurnIsOver(somebodyWaiting: false), "Nobody's waiting: it keeps biting.");
        }

        MobState BiteUntilTurnIsOver(bool somebodyWaiting)
        {
            _heroes.Clear();
            _allies.Clear();
            var brain = new MobBrain(_wolf, new MobRules(45f), float3.zero, 7u);
            Hero(P1, float3.zero);
            if (somebodyWaiting)
                Ally(3, new float3(0f, 0f, 4f), biting: false);
            var feet = new float3(1f, 0f, 0f);
            brain.Join(P1, 0f);

            int strikes = 0;
            float now = 0f;
            while (strikes < MobBrain.BitesPerTurn && now < 10f)
            {
                now += Dt;
                Tick(brain, now, feet);
                if (Did(brain, MobEventKind.Strike))
                    strikes++;
            }
            Assert.AreEqual(MobBrain.BitesPerTurn, strikes);
            for (float until = now + MobBrain.RecoverSeconds + Dt * 2f; now < until; now += Dt)
                Tick(brain, now, feet);
            return brain.State;
        }

        [Test]
        public void Wolf_HurtAndAlone_RunsAway_ThenGoesHome()
        {
            var rules = new MobRules(45f);
            var brain = new MobBrain(_wolf, rules, float3.zero, 7u);
            Hero(P1, new float3(-2f, 0f, 0f));
            var feet = new float3(0f, 0f, 0f);
            brain.Join(P1, 0f);
            Tick(brain, 0.1f, feet);

            rules.Hit(38f, P1);
            MobIntent intent = Tick(brain, 0.2f, feet);
            Assert.AreEqual(MobState.Flee, brain.State);
            Assert.IsTrue(Did(brain, MobEventKind.Fled));
            Assert.IsFalse(brain.IsFighting);
            Assert.AreEqual(MobMove.PathTo, intent.Move);
            Assert.Greater(intent.Destination.x, 10f, "Away from the hero.");
            Assert.AreEqual(_wolf.RunSpeed, intent.SpeedScale);

            rules.Hit(1f, P1);
            Tick(brain, 0.3f, feet);
            Assert.AreEqual(MobState.Flee, brain.State, "Hitting it again doesn't turn it round.");

            Tick(brain, 0.2f + MobBrain.FleeSeconds + 0.1f, feet);
            Assert.AreEqual(MobState.Return, brain.State, "Then it goes home to heal.");
        }

        [Test]
        public void Wolf_Hurt_KeepsFighting_WithItsPackBesideIt()
        {
            var rules = new MobRules(45f);
            var brain = new MobBrain(_wolf, rules, float3.zero, 7u);
            Hero(P1, new float3(-2f, 0f, 0f));
            brain.Join(P1, 0f);
            Ally(3, new float3(-1f, 0f, 1f), biting: true);
            rules.Hit(38f, P1);
            Tick(brain, 0.1f, float3.zero);
            Assert.AreNotEqual(MobState.Flee, brain.State);
            Assert.IsTrue(brain.IsFighting);
        }

        [Test]
        public void Rhino_JoinsItsHerd_ThenCalmsDown_WhenNobodyHitsIt()
        {
            var rules = new MobRules(200f);
            var brain = new MobBrain(_rhino, rules, float3.zero, 3u);
            Hero(P1, new float3(3f, 0f, 0f));
            Tick(brain, 0.1f, float3.zero);
            Assert.IsFalse(brain.IsFighting, "Neutral: a hero walking by is no reason to fight.");

            Assert.IsTrue(brain.Join(P1, 0.2f), "A herd-mate was hit nearby.");
            Tick(brain, 0.3f, float3.zero);
            Assert.IsTrue(brain.IsFighting);

            rules.Hit(10f, P1);
            Tick(brain, 8f, float3.zero);
            Tick(brain, 8f + _rhino.CalmSeconds - 0.1f, float3.zero);
            Assert.IsTrue(brain.IsFighting, "Being hit at 8 s kept it angry.");
            Tick(brain, 8f + _rhino.CalmSeconds + 0.1f, new float3(6f, 0f, 0f));
            Assert.AreEqual(MobState.Return, brain.State, "Twelve quiet seconds: it calms down and goes home.");
        }

        [Test]
        public void Book_ChecksSplits()
        {
            MobDefinition small = MobDefinition.Create(MobId.CrystalSlimeSmall, "Small", 10f, MotorSettings.Default,
                MobTemperament.Aggressive, 8f, 20f, Bite);
            MobDefinition large = MobDefinition.Create(MobId.CrystalSlime, "Large", 80f, MotorSettings.Default,
                MobTemperament.Aggressive, 8f, 20f, Bite).WithSplit(small, 2);
            MobDefinition loop = MobDefinition.Create(MobId.CrystalSlimeMedium, "Loop", 30f, MotorSettings.Default,
                MobTemperament.Aggressive, 8f, 20f, Bite);
            loop.WithSplit(loop, 2);
            MobBook book = MobBook.Create(new[] { large, loop });
            try
            {
                Assert.IsTrue(book.IsSplitChild(small));
                Assert.IsFalse(book.IsSplitChild(large));
                string errors = string.Join("\n", book.Validate());
                StringAssert.Contains("'Large' splits into 'Small', which isn't in the book", errors);
                StringAssert.Contains("'Loop' splits into itself", errors);
                Assert.AreEqual(2, large.SplitCount);
                Assert.AreEqual(0, small.SplitCount);
            }
            finally
            {
                Object.DestroyImmediate(book);
                Object.DestroyImmediate(small);
                Object.DestroyImmediate(large);
                Object.DestroyImmediate(loop);
            }
        }
    }
}
