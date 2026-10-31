using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using Voxelforge.Core;
using Voxelforge.Gameplay;

namespace Voxelforge.Tests
{
    /// <summary>
    /// Roadmap 4.6: spells hit a target's whole body, and the bars over targets remember their health. Roadmap 5.2:
    /// creatures are targets too, found by a <see cref="TargetKey"/> that's the same on every PC.
    /// </summary>
    public class CombatTargetTests
    {
        static readonly int3 Dummy = new int3(10, 5, 10);

        /// <summary>A creature that stands still, for checking how targets find and hurt creatures.</summary>
        sealed class StandingCreature : ICombatCreature
        {
            public ulong CreatureId { get; set; }
            public bool IsTargetable { get; set; } = true;
            public float3 Feet { get; set; }
            public float3 HalfExtents { get; set; } = new float3(0.4f, 0.5f, 0.4f);
            public float Health = 30f;
            public float StunnedFor;

            public TargetDamage ServerDamage(float amount, PlayerId instigator)
            {
                float dealt = math.min(amount, Health);
                Health -= dealt;
                return new TargetDamage(dealt, Health, 30f, Health <= 0f);
            }

            public bool ServerStun(float seconds, PlayerId instigator)
            {
                StunnedFor = seconds;
                return true;
            }
        }

        GameObject _host;
        CombatTargets _targets;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("Targets");
            _targets = _host.AddComponent<CombatTargets>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_host);

        [Test]
        public void Body_IsHitAboveItsBlock()
        {
            // Aimed at the chest, 1.5 m up: over the dummy's block, but inside its body (Roadmap 4.6 play-test).
            var origin = new float3(10.5f, 6.5f, 2f);
            Assert.IsTrue(CombatTarget.RayHitsBody(Dummy, origin, new float3(0f, 0f, 1f), 20f, out float distance, out int3 normal));
            Assert.AreEqual(8f, distance, 1e-4f, "It enters the front face at z = 10.");
            Assert.AreEqual(new int3(0, 0, -1), normal, "The face it entered looks back at the shooter.");
        }

        [Test]
        public void Body_IsMissed_OverItsHead_AndBesideIt()
        {
            float3 forward = new float3(0f, 0f, 1f);
            Assert.IsFalse(CombatTarget.RayHitsBody(Dummy, new float3(10.5f, 5f + CombatTarget.BodyHeight + 0.1f, 2f), forward, 20f,
                out _, out _), "Over its head.");
            Assert.IsFalse(CombatTarget.RayHitsBody(Dummy, new float3(11.2f, 6f, 2f), forward, 20f, out _, out _), "Beside it.");
            Assert.IsFalse(CombatTarget.RayHitsBody(Dummy, new float3(10.5f, 6f, 2f), forward, 7.5f, out _, out _), "Out of reach.");
        }

        [Test]
        public void Body_AStartInsideIt_HitsAtOnce()
        {
            Assert.IsTrue(CombatTarget.RayHitsBody(Dummy, new float3(10.5f, 6f, 10.5f), new float3(1f, 0f, 0f), 5f,
                out float distance, out int3 normal));
            Assert.AreEqual(0f, distance, 1e-4f);
            Assert.AreEqual(int3.zero, normal);
        }

        [Test]
        public void Keys_TellDummiesAndCreaturesApart()
        {
            TargetKey dummy = TargetKey.Dummy(new int3(-300, 70, 4096));
            TargetKey other = TargetKey.Dummy(new int3(-300, 71, 4096));
            TargetKey creature = TargetKey.Creature(42);

            Assert.IsTrue(dummy.IsDummy && !dummy.IsCreature);
            Assert.IsTrue(creature.IsCreature && !creature.IsDummy);
            Assert.AreEqual(42UL, creature.CreatureId);
            Assert.AreNotEqual(dummy, other, "Each block has its own key.");
            Assert.AreEqual(dummy, new CombatTarget(new int3(-300, 70, 4096)).Key);
            Assert.IsFalse(TargetKey.None.IsValid);
            Assert.IsFalse(default(CombatTarget).IsValid);
        }

        [Test]
        public void Creatures_JoinAndLeave_ByTheirId()
        {
            // In play, CombatCreatures' spawn events call Add and Remove; edit-mode tests don't run OnEnable.
            var wolf = new StandingCreature { CreatureId = 7, Feet = new float3(3f, 1f, 3f) };
            _targets.Add(wolf);
            _targets.Add(wolf);
            Assert.IsTrue(_targets.TryGetCreature(7, out ICombatCreature found));
            Assert.AreSame(wolf, found);
            Assert.AreEqual(1, _targets.Creatures.Count, "Listed once.");

            _targets.Remove(new StandingCreature { CreatureId = 7 });
            Assert.IsTrue(_targets.TryGetCreature(7, out _), "Only the creature itself can take itself off the list.");
            _targets.Remove(wolf);
            Assert.IsFalse(_targets.TryGetCreature(7, out _));
            Assert.AreEqual(0, _targets.Creatures.Count);
        }

        [Test]
        public void Creatures_AreFound_Hurt_AndStunned_LikeDummies()
        {
            var near = new StandingCreature { CreatureId = 1, Feet = new float3(2f, 0f, 0f) };
            var far = new StandingCreature { CreatureId = 2, Feet = new float3(9f, 0f, 0f) };
            var gone = new StandingCreature { CreatureId = 3, Feet = new float3(1f, 0f, 0f), IsTargetable = false };
            _targets.Add(near);
            _targets.Add(far);
            _targets.Add(gone);

            Assert.IsTrue(_targets.TryFindNearest(float3.zero, 5f, out CombatTarget nearest));
            Assert.AreEqual(CombatTargetKind.Creature, nearest.Kind);
            Assert.AreEqual(1UL, nearest.CreatureId, "A defeated creature isn't a target any more.");
            Assert.AreEqual(new float3(2f, 0.5f, 0f), nearest.Position, "Its middle is half its height over its feet.");

            var found = new System.Collections.Generic.List<CombatTarget>();
            _targets.FindAll(float3.zero, 20f, found);
            Assert.AreEqual(2, found.Count);

            TargetDamage hit = _targets.ServerDamage(nearest, 50f, new PlayerId(1));
            Assert.AreEqual(30f, hit.Dealt, 1e-4f, "It can't take more than it has.");
            Assert.IsTrue(hit.Toppled, "Emptied: defeated.");
            Assert.IsTrue(_targets.ServerStun(CombatTarget.Of(far), 2f, new PlayerId(1)));
            Assert.AreEqual(2f, far.StunnedFor);
        }

        [Test]
        public void Raycast_HitsACreaturesBody_AndSaysWhichOne()
        {
            var boar = new StandingCreature { CreatureId = 5, Feet = new float3(10f, 0f, 0f) };
            _targets.Add(boar);

            Assert.IsTrue(_targets.Raycast(new float3(0f, 0.5f, 0f), new float3(1f, 0f, 0f), 20f, out var hit, out CombatTarget struck));
            Assert.AreEqual(9.6f, hit.Distance, 1e-4f, "It enters the near side of its 0.8 m wide body.");
            Assert.AreEqual(5UL, struck.CreatureId);
            Assert.IsFalse(_targets.Raycast(new float3(0f, 1.5f, 0f), new float3(1f, 0f, 0f), 20f, out _, out _), "Over its head.");
            Assert.IsFalse(_targets.Raycast(new float3(0f, 0.5f, 0f), new float3(1f, 0f, 0f), 9f, out _, out _), "Out of reach.");
        }

        [Test]
        public void Book_KeepsOneEntryPerTarget_WithItsLatestHealth()
        {
            var book = new TargetHealthBook();
            var middle = new float3(10.5f, 5.9f, 10.5f);
            TargetKey key = TargetKey.Dummy(Dummy);
            book.Record(key, middle, 455, 500, false, 1f);
            book.Record(key, middle, 395, 500, false, 2f);
            book.Record(TargetKey.Dummy(Dummy + new int3(3, 0, 0)), middle + new float3(3f, 0f, 0f), 480, 500, false, 2f);

            Assert.AreEqual(2, book.Entries.Count);
            Assert.AreEqual(395, book.Entries[0].Health);
            Assert.AreEqual(395f / 500f, book.Entries[0].Share, 1e-4f);
        }

        [Test]
        public void Book_ForgetsATarget_WhenTheHostWould()
        {
            var book = new TargetHealthBook();
            book.Record(TargetKey.Dummy(Dummy), float3.zero, 400, 500, toppled: true, 1f);
            Assert.AreEqual(1f, book.Entries[0].ToppledAt);

            book.Expire(1f + TargetHealthBook.ShowSeconds - 0.1f);
            Assert.AreEqual(1, book.Entries.Count);
            book.Expire(1f + TargetHealthBook.ShowSeconds);
            Assert.AreEqual(0, book.Entries.Count);
        }

        [Test]
        public void Book_FollowsAWalkingCreature_AndDropsItWhenItLeaves()
        {
            var wolf = new StandingCreature { CreatureId = 9, Feet = new float3(0f, 0f, 0f) };
            _targets.Add(wolf);
            var book = new TargetHealthBook();
            book.Record(TargetKey.Creature(9), new float3(0f, 0.5f, 0f), 20, 30, false, 1f);
            book.Record(TargetKey.Dummy(Dummy), new CombatTarget(Dummy).Position, 450, 500, false, 1f);

            // Two different keys, even though the wolf was hit in one place and the dummy in another.
            Assert.AreEqual(2, book.Entries.Count);

            wolf.Feet = new float3(5f, 2f, 0f);
            book.Follow(_targets);
            Assert.AreEqual(new float3(5f, 2.5f, 0f), book.Entries[0].Middle, "The bar walks with the wolf.");
            Assert.AreEqual(CombatTarget.Of(wolf).Top, book.Entries[0].Top);
            Assert.AreEqual(0f, math.distance(new CombatTarget(Dummy).Top, book.Entries[1].Top), 1e-4f,
                "A dummy's bar floats where it always did.");

            _targets.Remove(wolf);
            book.Follow(_targets);
            Assert.AreEqual(1, book.Entries.Count, "The wolf left the world, so its bar goes; the dummy's stays.");
            Assert.IsTrue(book.Entries[0].Key.IsDummy);
        }
    }
}
