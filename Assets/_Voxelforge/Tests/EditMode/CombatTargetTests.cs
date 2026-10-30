using NUnit.Framework;
using Unity.Mathematics;
using Voxelforge.Gameplay;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 4.6: spells hit a target's whole body, and the bars over targets remember their health.</summary>
    public class CombatTargetTests
    {
        static readonly int3 Dummy = new int3(10, 5, 10);

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
        public void Book_KeepsOneEntryPerTarget_WithItsLatestHealth()
        {
            var book = new TargetHealthBook();
            var middle = new float3(10.5f, 5.9f, 10.5f);
            book.Record(middle, 455, 500, false, 1f);
            book.Record(middle, 395, 500, false, 2f);
            book.Record(middle + new float3(3f, 0f, 0f), 480, 500, false, 2f);

            Assert.AreEqual(2, book.Entries.Count);
            Assert.AreEqual(395, book.Entries[0].Health);
            Assert.AreEqual(395f / 500f, book.Entries[0].Share, 1e-4f);
        }

        [Test]
        public void Book_ForgetsATarget_WhenTheHostWould()
        {
            var book = new TargetHealthBook();
            book.Record(float3.zero, 400, 500, toppled: true, 1f);
            Assert.AreEqual(1f, book.Entries[0].ToppledAt);

            book.Expire(1f + TargetHealthBook.ShowSeconds - 0.1f);
            Assert.AreEqual(1, book.Entries.Count);
            book.Expire(1f + TargetHealthBook.ShowSeconds);
            Assert.AreEqual(0, book.Entries.Count);
        }
    }
}
