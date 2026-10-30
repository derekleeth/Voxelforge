using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using Voxelforge.Gameplay;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 4.6: who a shield goes on, who stands in a zone, and when a zone pulses.</summary>
    public class SupportTests
    {
        static readonly float3 Eyes = new float3(0f, 1.6f, 0f);
        static readonly float3 Forward = new float3(0f, 0f, 1f);

        [Test]
        public void Shield_GoesOnThePartnerYouAimAt()
        {
            var partners = new List<float3> { new float3(6f, 0f, 6f), new float3(0.5f, 0.6f, 10f) };
            Assert.AreEqual(1, SupportRules.PickPartner(Eyes, Forward, partners, 20f, 12f));
        }

        [Test]
        public void Shield_GoesOnYou_WhenNoPartnerIsInTheCone()
        {
            var partners = new List<float3> { new float3(6f, 0f, 6f) }; // 45 degrees off the aim.
            Assert.AreEqual(-1, SupportRules.PickPartner(Eyes, Forward, partners, 20f, 12f));
        }

        [Test]
        public void Shield_IgnoresPartnersOutOfRange()
        {
            var partners = new List<float3> { new float3(0f, 0.7f, 25f) };
            Assert.AreEqual(-1, SupportRules.PickPartner(Eyes, Forward, partners, 20f, 12f));
        }

        [Test]
        public void Shield_PicksTheOneClosestToTheAim()
        {
            var partners = new List<float3> { new float3(1.5f, 0.7f, 10f), new float3(0.2f, 0.7f, 15f) };
            Assert.AreEqual(1, SupportRules.PickPartner(Eyes, Forward, partners, 20f, 12f));
        }

        [Test]
        public void Zone_CountsHeroesInsideItsCircle_NotOnALedgeAbove()
        {
            var centre = new float3(0f, 10f, 0f);
            Assert.IsTrue(SupportRules.InZone(centre, 6f, new float3(4f, 10f, 4f)));
            Assert.IsFalse(SupportRules.InZone(centre, 6f, new float3(5f, 10f, 5f)), "7.1 m out: outside.");
            Assert.IsTrue(SupportRules.InZone(centre, 6f, new float3(0f, 12f, 0f)), "Mid-jump still counts.");
            Assert.IsFalse(SupportRules.InZone(centre, 6f, new float3(0f, 14f, 0f)), "On a ledge 4 m up.");
        }

        [Test]
        public void Zone_PulsesAtOnce_ThenEveryInterval_UntilItEnds()
        {
            var rules = new ZoneRules(10f, 6f, 1f);
            int pulses = 0;
            for (float t = 10f; t < 17f; t += 0.02f)
            {
                if (rules.TryPulse(t))
                    pulses++;
            }
            Assert.AreEqual(6, pulses, "A 6 s zone pulsing every second pulses 6 times.");
            Assert.IsTrue(rules.IsOver(16f));
            Assert.IsFalse(rules.IsOver(15.9f));
        }

        [Test]
        public void Zone_GivesEachPulseOnlyOnce()
        {
            var rules = new ZoneRules(0f, 6f, 1f);
            Assert.IsTrue(rules.TryPulse(0f));
            Assert.IsFalse(rules.TryPulse(0.5f));
            Assert.IsTrue(rules.TryPulse(1.01f));
            Assert.IsFalse(rules.TryPulse(1.02f));
        }
    }
}
