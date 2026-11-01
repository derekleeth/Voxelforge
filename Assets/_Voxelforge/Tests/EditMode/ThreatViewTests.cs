using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Netcode;
using Voxelforge.Core;
using Voxelforge.Gameplay;

namespace Voxelforge.Tests
{
    /// <summary>
    /// Roadmap 5.3 step D: what each PC learns about a mob's threat (<see cref="MobThreatView"/>), and when a player is
    /// warned they're about to pull it (<see cref="AggroWarnings"/>).
    /// </summary>
    public class ThreatViewTests
    {
        static readonly PlayerId Tank = new PlayerId(1);
        static readonly PlayerId Mage = new PlayerId(2);
        static readonly PlayerId Shaman = new PlayerId(3);

        static ThreatPick Pick(params HeroSense[] heroes) =>
            new ThreatPick { Heroes = heroes, Feet = float3.zero, MeleeReach = 2f };

        static HeroSense At(PlayerId id, float x) => new HeroSense { Id = id, Feet = new float3(x, 0f, 0f) };

        [Test]
        public void View_TheTankHasIt_AndOthersShowHowCloseTheyAreToPullingIt()
        {
            var table = new ThreatTable();
            table.Add(Tank, 100f);
            table.Add(Mage, 65f);
            table.Add(Shaman, 99f);
            MobThreatView view = MobThreatView.From(table, Tank, Pick(At(Tank, 1f), At(Mage, 20f), At(Shaman, 1.5f)));

            Assert.AreEqual(Tank, view.AggroHero);
            Assert.AreEqual(MobThreatView.Pull, view.ShareOf(Tank));
            // The mage, at range, pulls it at 130: 65 is half way there.
            Assert.AreEqual(128, view.ShareOf(Mage), 1);
            // The shaman, in melee, pulls it at 110: 99 is 90% of the way.
            Assert.AreEqual(230, view.ShareOf(Shaman), 1);
            Assert.AreEqual(0, view.ShareOf(new PlayerId(4)), "Not on its table.");
            Assert.AreEqual(0, view.ShareOf(PlayerId.None));
        }

        [Test]
        public void View_NotFighting_ShowsNothing()
        {
            var table = new ThreatTable();
            table.Add(Tank, 100f);
            MobThreatView view = MobThreatView.From(table, PlayerId.None, Pick());
            Assert.AreEqual(0, view.Aggro);
            Assert.AreEqual(0, view.ShareOf(Tank));
            Assert.AreEqual(default(MobThreatView), MobThreatView.From(null, Tank, Pick()));
        }

        [Test]
        public void View_KeepsOnlyTheAngriestFour()
        {
            var table = new ThreatTable();
            for (byte i = 1; i <= 6; i++)
                table.Add(new PlayerId(i), i * 10f);
            MobThreatView view = MobThreatView.From(table, new PlayerId(6), Pick());
            Assert.AreEqual(0, view.ShareOf(new PlayerId(1)), "Fifth angriest: left out.");
            Assert.AreEqual(0, view.ShareOf(new PlayerId(2)));
            Assert.Greater(view.ShareOf(new PlayerId(3)), 0);
            Assert.Greater(view.ShareOf(new PlayerId(5)), view.ShareOf(new PlayerId(4)));
        }

        [Test]
        public void View_IsOnlySentAgain_WhenSomethingReallyChanged()
        {
            var table = new ThreatTable();
            table.Add(Tank, 100f);
            table.Add(Mage, 50f);
            MobThreatView before = MobThreatView.From(table, Tank, Pick());
            table.Add(Mage, 1f);
            MobThreatView nudged = MobThreatView.From(table, Tank, Pick());
            Assert.IsFalse(nudged.Differs(before, 5), "A point of threat isn't worth sending.");
            table.Add(Mage, 20f);
            Assert.IsTrue(MobThreatView.From(table, Tank, Pick()).Differs(before, 5));
            Assert.IsTrue(MobThreatView.From(table, Mage, Pick()).Differs(before, 5), "Someone else has aggro.");
        }

        [Test]
        public void View_SurvivesTheWire()
        {
            var table = new ThreatTable();
            table.Add(Tank, 100f);
            table.Add(Mage, 80f);
            MobThreatView view = MobThreatView.From(table, Tank, Pick());
            using var writer = new FastBufferWriter(32, Allocator.Temp);
            writer.WriteNetworkSerializable(view);
            using var reader = new FastBufferReader(writer, Allocator.Temp);
            reader.ReadNetworkSerializable(out MobThreatView read);
            Assert.AreEqual(view, read);
            Assert.AreEqual(view.ShareOf(Mage), read.ShareOf(Mage));
        }

        [Test]
        public void Warning_OnlyNearPullingIt_NeverForTheTank_AndNotTooOften()
        {
            var table = new ThreatTable();
            table.Add(Tank, 100f);
            table.Add(Mage, 100f);
            MobThreatView close = MobThreatView.From(table, Tank, Pick(At(Mage, 20f)));
            var warnings = new AggroWarnings();

            Assert.IsFalse(warnings.ShouldWarn(7, close, Tank, 0f), "The tank has it already.");
            Assert.IsFalse(warnings.ShouldWarn(7, close, Mage, 0f), "100 of the 130 it takes at range: 77%, not yet.");

            table.Add(Mage, 20f);
            close = MobThreatView.From(table, Tank, Pick(At(Mage, 20f)));
            Assert.IsTrue(warnings.ShouldWarn(7, close, Mage, 1f), "92%: watch out.");
            Assert.IsFalse(warnings.ShouldWarn(7, close, Mage, 2f), "Not again so soon.");
            Assert.IsTrue(warnings.ShouldWarn(8, close, Mage, 2f), "Another mob is another warning.");
            warnings.Prune(10f);
            Assert.IsTrue(warnings.ShouldWarn(7, close, Mage, 10f), "Long enough later, it warns again.");
        }

        [Test]
        public void Meter_GoesGreenToYellowToRed()
        {
            Assert.Greater(ThreatMarksView.MeterColor(0.2f).g, ThreatMarksView.MeterColor(0.2f).r, "Safe: green.");
            UnityEngine.Color nearly = ThreatMarksView.MeterColor(AggroWarnings.WarnShare);
            Assert.Greater(nearly.r, 0.9f, "Close: yellow.");
            Assert.Greater(nearly.g, 0.7f);
            UnityEngine.Color full = ThreatMarksView.MeterColor(1f);
            Assert.Greater(full.r, full.g * 3f, "It's yours: red.");
        }
    }
}
