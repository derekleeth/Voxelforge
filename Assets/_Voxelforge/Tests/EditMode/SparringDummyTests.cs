using NUnit.Framework;
using Voxelforge.Core;
using Voxelforge.Gameplay;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 4.6: Training Dummies take hits, spar back at whoever hit them, and calm down when left alone.</summary>
    public class SparringDummyTests
    {
        static readonly PlayerId Kian = new PlayerId(1);
        static readonly PlayerId Derek = new PlayerId(2);

        [Test]
        public void Hit_TakesDamage_AndRemembersWhoDidIt()
        {
            var dummy = new SparringDummy(0f);
            Assert.AreEqual(45f, dummy.Hit(45f, Kian, 1f, out bool toppled), 1e-4f);
            Assert.IsFalse(toppled);
            Assert.AreEqual(SparringDummy.MaxHealth - 45f, dummy.Health, 1e-4f);
            Assert.AreEqual(Kian, dummy.LastAttacker);

            dummy.Hit(10f, Derek, 2f, out _);
            Assert.AreEqual(Derek, dummy.LastAttacker, "The next hay sack is for whoever hit it last.");
        }

        [Test]
        public void Emptying_ItTopplesIt_AndItSpringsBackToFull()
        {
            var dummy = new SparringDummy(0f);
            dummy.Hit(SparringDummy.MaxHealth - 20f, Kian, 1f, out _);
            Assert.AreEqual(20f, dummy.Hit(60f, Kian, 2f, out bool toppled), 1e-4f, "It can only lose what it has left.");
            Assert.IsTrue(toppled);
            Assert.AreEqual(SparringDummy.MaxHealth, dummy.Health, 1e-4f);
        }

        [Test]
        public void Spar_WaitsAMomentAfterTheFirstHit_ThenEverySparEvery()
        {
            var dummy = new SparringDummy(0f);
            Assert.IsFalse(dummy.TrySpar(0f, out _), "Nobody has hit it: it doesn't throw.");

            dummy.Hit(10f, Kian, 20f, out _);
            Assert.IsFalse(dummy.TrySpar(20f + SparringDummy.SparEvery - 0.1f, out _), "A moment's grace after the first hit.");
            Assert.IsTrue(dummy.TrySpar(20f + SparringDummy.SparEvery, out PlayerId target));
            Assert.AreEqual(Kian, target);
            Assert.IsFalse(dummy.TrySpar(20f + SparringDummy.SparEvery + 1f, out _), "Then it waits again.");
        }

        [Test]
        public void LeftAlone_ItStopsSparring()
        {
            var dummy = new SparringDummy(0f);
            dummy.Hit(10f, Kian, 1f, out _);
            float later = 1f + SparringDummy.ResetAfter;
            Assert.IsTrue(dummy.IsIdle(later));
            Assert.IsFalse(dummy.TrySpar(later, out _));
        }
    }
}
