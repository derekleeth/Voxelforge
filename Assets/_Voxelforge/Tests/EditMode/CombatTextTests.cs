using System.Collections.Generic;
using NUnit.Framework;
using Voxelforge.Gameplay;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 4.4: the floating combat text's lines, lifetimes, pool and merged pickups.</summary>
    public class CombatTextTests
    {
        static CombatTextEvent Hit(int amount) => new CombatTextEvent { Kind = CombatTextKind.SpellHit, Amount = amount };

        static CombatTextEvent Pickup(ushort item, int amount) =>
            new CombatTextEvent { Kind = CombatTextKind.Pickup, ItemId = item, Amount = amount };

        [Test]
        public void Lines_GoAwayWhenTheirTimeIsUp()
        {
            var feed = new CombatTextFeed(8);
            feed.Push(Hit(45), 0f);
            feed.Push(new CombatTextEvent { Kind = CombatTextKind.CraterHit, Amount = 112 }, 0f);
            feed.Expire(CombatTextFeed.Lifetime(CombatTextKind.SpellHit) + 0.01f);
            Assert.AreEqual(1, feed.Entries.Count);
            Assert.AreEqual(CombatTextKind.CraterHit, feed.Entries[0].Event.Kind);
            feed.Expire(CombatTextFeed.Lifetime(CombatTextKind.CraterHit) + 0.01f);
            Assert.AreEqual(0, feed.Entries.Count);
        }

        [Test]
        public void Pool_IsLimited_OldestGivesWay_AndLabelsAreNeverShared()
        {
            var feed = new CombatTextFeed(3);
            for (int i = 1; i <= 5; i++)
                feed.Push(Hit(i), i * 0.01f);
            Assert.AreEqual(3, feed.Entries.Count);
            Assert.AreEqual(3, feed.Entries[0].Event.Amount, "The two oldest lines gave way.");
            var labels = new HashSet<int>();
            foreach (CombatTextFeed.Entry entry in feed.Entries)
            {
                Assert.That(entry.Label, Is.InRange(0, 2));
                Assert.IsTrue(labels.Add(entry.Label));
            }
        }

        [Test]
        public void Pickups_OfTheSameItemCloseTogether_AddUp()
        {
            var feed = new CombatTextFeed(8);
            feed.Push(Pickup(2, 3), 0f);
            int version = feed.Entries[0].Version;
            feed.Push(Pickup(2, 4), CombatTextFeed.MergeSeconds * 0.5f);
            Assert.AreEqual(1, feed.Entries.Count);
            Assert.AreEqual(7, feed.Entries[0].Event.Amount);
            Assert.AreNotEqual(version, feed.Entries[0].Version, "The words changed, so the label must redraw.");

            feed.Push(Pickup(5, 1), 0.2f); // A different item: its own line.
            feed.Push(Pickup(2, 1), 2f); // Too late to merge.
            Assert.AreEqual(3, feed.Entries.Count);
        }
    }
}
