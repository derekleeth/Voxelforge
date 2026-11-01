using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using Voxelforge.Core;
using Voxelforge.Gameplay;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 5.6 step A: XP, levels, what a level adds and who shares a mob's XP, without any networking.</summary>
    public class LevelTests
    {
        const string LevelBookAssetPath = "Assets/_Voxelforge/Data/Levels/LevelBook.asset";
        const string MobBookAssetPath = "Assets/_Voxelforge/Data/Mobs/MobBook.asset";

        LevelBook _book;

        [SetUp]
        public void SetUp() => _book = LevelBook.Create(4, new[] { 100, 150, 200 }, healthPerLevel: 10f, manaPerLevel: 5f);

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_book);

        // ---- Gaining XP ----

        [Test]
        public void Gain_AddsXp_AndLevelsUpWithTheLeftoverCarried()
        {
            LevelState state = LevelRules.Gain(LevelState.Start, 60, _book, out int gained);
            Assert.AreEqual(new LevelState(1, 60), state);
            Assert.AreEqual(0, gained);

            state = LevelRules.Gain(state, 70, _book, out gained);
            Assert.AreEqual(new LevelState(2, 30), state, "60 + 70 = 130: level 2 with 30 over.");
            Assert.AreEqual(1, gained);
        }

        [Test]
        public void Gain_CanJumpSeveralLevelsAtOnce()
        {
            LevelState state = LevelRules.Gain(LevelState.Start, 100 + 150 + 20, _book, out int gained);
            Assert.AreEqual(new LevelState(3, 20), state);
            Assert.AreEqual(2, gained);
        }

        [Test]
        public void Gain_StopsAtTheCap_WithNoXpOver()
        {
            LevelState state = LevelRules.Gain(LevelState.Start, 99999, _book, out int gained);
            Assert.AreEqual(new LevelState(4, 0), state);
            Assert.AreEqual(3, gained);
            state = LevelRules.Gain(state, 500, _book, out gained);
            Assert.AreEqual(new LevelState(4, 0), state, "Nothing more at the cap.");
            Assert.AreEqual(0, gained);
        }

        [Test]
        public void Gain_TreatsAnOldZeroedStateAsLevelOne()
        {
            LevelState state = LevelRules.Gain(default, 10, _book, out _);
            Assert.AreEqual(new LevelState(1, 10), state);
            Assert.AreEqual(1, default(LevelState).EffectiveLevel);
        }

        [Test]
        public void Bonuses_GrowEachLevel_FromNothingAtLevelOne()
        {
            Assert.AreEqual(0f, LevelRules.BonusHealth(1, _book));
            Assert.AreEqual(30f, LevelRules.BonusHealth(4, _book));
            Assert.AreEqual(15f, LevelRules.BonusMana(4, _book));
            Assert.AreEqual(0f, LevelRules.BonusHealth(5, null), "No book, no bonus.");
        }

        [Test]
        public void Progress_IsTheShareOfThisLevel_AndFullAtTheCap()
        {
            Assert.AreEqual(0.5f, LevelRules.Progress(new LevelState(2, 75), _book), 1e-5f);
            Assert.AreEqual(1f, LevelRules.Progress(new LevelState(4, 0), _book));
        }

        // ---- Sharing ----

        [Test]
        public void Sharers_AreEveryHeroInRange_FightingOrNot()
        {
            var heroes = new List<HeroAt>
            {
                new HeroAt(new PlayerId(1), new float3(5f, 0f, 0f)),
                new HeroAt(new PlayerId(2), new float3(0f, 0f, 39f)),
                new HeroAt(new PlayerId(3), new float3(41f, 0f, 0f)),
                new HeroAt(PlayerId.None, float3.zero),
            };
            var sharers = new List<PlayerId>();
            LevelRules.Sharers(float3.zero, heroes, 40f, sharers);
            CollectionAssert.AreEqual(new[] { new PlayerId(1), new PlayerId(2) }, sharers);
        }

        // ---- The HUD (step B) ----

        [Test]
        public void XpText_AddsUpOnOneLine_AndLevelUpGetsItsOwn()
        {
            var feed = new CombatTextFeed(8);
            feed.Push(new CombatTextEvent { Kind = CombatTextKind.XpGained, Amount = 20 }, 0f);
            feed.Push(new CombatTextEvent { Kind = CombatTextKind.XpGained, Amount = 15 }, 0.2f);
            feed.Push(new CombatTextEvent { Kind = CombatTextKind.LevelUp, Amount = 2 }, 0.2f);
            Assert.AreEqual(2, feed.Entries.Count);
            Assert.AreEqual(35, feed.Entries[0].Event.Amount, "Two kills close together: +35 XP.");
            Assert.AreEqual(CombatTextKind.LevelUp, feed.Entries[1].Event.Kind);
            Assert.Greater(CombatTextFeed.Lifetime(CombatTextKind.LevelUp), CombatTextFeed.Lifetime(CombatTextKind.XpGained));
        }

        // ---- The book ----

        [Test]
        public void Book_Validate_CatchesAShortOrFallingCurveAndDuplicateMobs()
        {
            LevelBook bad = LevelBook.Create(5, new[] { 100, 50, 200 }, mobXp: new[]
            {
                new MobXp(MobId.BristlebackBoar, 20), new MobXp(MobId.BristlebackBoar, 30), new MobXp(MobId.None, 5),
            });
            try
            {
                List<string> errors = bad.Validate();
                Assert.That(errors, Has.Some.Contains("needs 4 XP steps"));
                Assert.That(errors, Has.Some.Contains("needs less XP than"));
                Assert.That(errors, Has.Some.Contains("more than one XP entry"));
                Assert.That(errors, Has.Some.Contains("names no mob"));
            }
            finally
            {
                Object.DestroyImmediate(bad);
            }
            Assert.IsEmpty(_book.Validate());
            Assert.AreEqual(0, _book.XpToNext(4), "No next level at the cap.");
        }

        [Test]
        public void RealLevelBook_IsValid_AndEveryMobGivesXp()
        {
            var book = AssetDatabase.LoadAssetAtPath<LevelBook>(LevelBookAssetPath);
            Assert.IsNotNull(book, "Run Voxelforge > Build Levels.");
            CollectionAssert.IsEmpty(book.Validate());
            Assert.AreEqual(20, book.MaxLevel);
            foreach (MobDefinition mob in AssetDatabase.LoadAssetAtPath<MobBook>(MobBookAssetPath).Mobs)
            {
                Assert.IsTrue(book.TryGetMobXp(mob.Id, out int xp), $"{mob.DisplayName} gives no XP.");
                Assert.Greater(xp, 0);
            }
            // A quick start: level 2 after about five boars.
            book.TryGetMobXp(MobId.BristlebackBoar, out int boar);
            Assert.That(book.XpToNext(1) / (float)boar, Is.InRange(4f, 6f));
        }
    }
}
