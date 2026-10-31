using NUnit.Framework;
using Voxelforge.Gameplay;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 4.6: hero health, shields and regeneration.</summary>
    public class HeroHealthTests
    {
        [Test]
        public void Damage_TakesHealth_AndHealing_NeverGoesOverMax()
        {
            var health = new HeroHealth(100f);
            DamageResult hit = health.Damage(30f, 1f);
            Assert.AreEqual(0f, hit.Absorbed);
            Assert.AreEqual(30f, hit.Taken, 1e-4f);
            Assert.AreEqual(70f, health.Current, 1e-4f);

            Assert.AreEqual(30f, health.Heal(50f), 1e-4f, "Only the missing 30 can be healed.");
            Assert.AreEqual(100f, health.Current, 1e-4f);
            Assert.IsTrue(health.IsFull);
        }

        [Test]
        public void Shield_SoaksUpDamageFirst()
        {
            var health = new HeroHealth(100f);
            health.SetShield(40f, 15f);

            DamageResult small = health.Damage(25f, 1f);
            Assert.AreEqual(25f, small.Absorbed, 1e-4f);
            Assert.AreEqual(0f, small.Taken, 1e-4f);
            Assert.AreEqual(15f, health.Shield, 1e-4f);
            Assert.AreEqual(100f, health.Current, 1e-4f);

            DamageResult big = health.Damage(25f, 2f);
            Assert.AreEqual(15f, big.Absorbed, 1e-4f, "The rest of the shield goes first...");
            Assert.AreEqual(10f, big.Taken, 1e-4f, "...and only what's left comes off health.");
            Assert.AreEqual(0f, health.Shield, 1e-4f);
            Assert.AreEqual(90f, health.Current, 1e-4f);
        }

        [Test]
        public void Shield_FadesWhenItsTimeIsUp()
        {
            var health = new HeroHealth(100f);
            health.SetShield(40f, 15f);
            health.Tick(14.9f, 0.1f);
            Assert.AreEqual(40f, health.Shield, 1e-4f);
            health.Tick(15f, 0.1f);
            Assert.AreEqual(0f, health.Shield, 1e-4f);
        }

        [Test]
        public void Floor_KeepsHealthFromGoingUnderIt()
        {
            var health = new HeroHealth(100f);
            DamageResult hit = health.Damage(500f, 1f, floor: 10f);
            Assert.AreEqual(90f, hit.Taken, 1e-4f);
            Assert.AreEqual(10f, health.Current, 1e-4f);

            Assert.AreEqual(0f, health.Damage(8f, 2f, floor: 10f).Taken, "Already at the floor: nothing more comes off.");
            Assert.AreEqual(10f, health.Current, 1e-4f);
        }

        [Test]
        public void Regeneration_WaitsUntilOutOfCombat()
        {
            var health = new HeroHealth(100f);
            health.Damage(50f, 10f);

            health.Tick(14f, 1f);
            Assert.AreEqual(50f, health.Current, 1e-4f, "4 s after a hit: still in combat.");

            health.Tick(15f, 1f);
            Assert.AreEqual(50f + HeroHealth.RegenPerSecond, health.Current, 1e-4f, "5 s after: health comes back.");
        }

        [Test]
        public void SetMax_KeepsTheSameShareOfHealth()
        {
            var health = new HeroHealth(100f);
            health.Damage(50f, 1f);
            health.SetMax(150f);
            Assert.AreEqual(150f, health.Max, 1e-4f);
            Assert.AreEqual(75f, health.Current, 1e-4f, "Half health stays half health.");
        }

        [Test]
        public void KnockOut_AtZero_ThenNothingHurtsOrHealsUntilTheyWake()
        {
            // Roadmap 5.2: a creature's hit has no floor.
            var health = new HeroHealth(100f);
            health.SetShield(20f, 99f);
            DamageResult hit = health.Damage(150f, 10f);
            Assert.IsTrue(hit.KnockedOut);
            Assert.AreEqual(20f, hit.Absorbed, 1e-4f);
            Assert.AreEqual(100f, hit.Taken, 1e-4f);
            Assert.IsTrue(health.IsKnockedOut);
            Assert.AreEqual(0f, health.Current);
            Assert.AreEqual(0f, health.Shield);

            Assert.AreEqual(0f, health.Damage(10f, 11f).Taken, "No kicking them while they're down.");
            Assert.AreEqual(0f, health.Heal(50f), "No healing either: they wake at home.");
            health.Tick(30f, 10f);
            Assert.AreEqual(0f, health.Current, "And no regeneration.");

            Assert.IsFalse(health.ReadyToWake(10f + HeroHealth.KnockOutSeconds - 0.01f));
            Assert.IsTrue(health.ReadyToWake(10f + HeroHealth.KnockOutSeconds));
            health.Wake();
            Assert.IsFalse(health.IsKnockedOut);
            Assert.AreEqual(100f * HeroHealth.WakeShare, health.Current, 1e-4f);
            Assert.IsFalse(health.ReadyToWake(99f));
        }

        [Test]
        public void KnockOut_NeverHappensWithAFloor_OrWhenTheShieldTakesItAll()
        {
            var health = new HeroHealth(100f);
            Assert.IsFalse(health.Damage(500f, 1f, floor: 10f).KnockedOut, "Sparring can't knock anyone out.");
            Assert.IsFalse(health.IsKnockedOut);

            var shielded = new HeroHealth(100f);
            shielded.SetShield(50f, 99f);
            Assert.IsFalse(shielded.Damage(40f, 1f).KnockedOut);
            Assert.AreEqual(100f, shielded.Current);

            health.Wake();
            Assert.AreEqual(10f, health.Current, 1e-4f, "Waking someone who isn't knocked out does nothing.");
        }
    }
}
