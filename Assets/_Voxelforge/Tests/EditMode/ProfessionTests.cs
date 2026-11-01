using NUnit.Framework;
using UnityEditor;
using Voxelforge.Gameplay;
using Object = UnityEngine.Object;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 5.7 step A: gathering skills, how they rise and a skilled gatherer's bonus, without any networking.</summary>
    public class ProfessionTests
    {
        const string BookAssetPath = "Assets/_Voxelforge/Data/Professions/ProfessionBook.asset";

        ProfessionBook _book;

        [SetUp]
        public void SetUp() => _book = ProfessionBook.Create(maxSkill: 100, orangeRange: 10, yellowRange: 25, greenRange: 40,
            yellowChance: 0.6f, greenChance: 0.25f, bonusYieldRange: 50, maxBonusYieldChance: 0.5f, heatMapSkill: 10);

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_book);

        // ---- Skills ----

        [Test]
        public void Skills_StartAtOne_AndAZeroedSkillCountsAsOne()
        {
            Assert.AreEqual(1, ProfessionSkills.Start.Get(ProfessionId.Mining));
            Assert.AreEqual(1, ProfessionSkills.Start.Get(ProfessionId.Herbalism));
            Assert.AreEqual(1, ProfessionSkills.Start.Get(ProfessionId.Skinning));
            Assert.AreEqual(1, default(ProfessionSkills).Get(ProfessionId.Skinning), "An old save's zeroed skill is 1.");
            Assert.AreEqual(ProfessionSkills.Start, default(ProfessionSkills));
        }

        [Test]
        public void Skills_With_ChangesOnlyThatProfession_AndStaysInRange()
        {
            ProfessionSkills skills = ProfessionSkills.Start.With(ProfessionId.Herbalism, 37);
            Assert.AreEqual(1, skills.Get(ProfessionId.Mining));
            Assert.AreEqual(37, skills.Get(ProfessionId.Herbalism));
            Assert.AreEqual(1, skills.Get(ProfessionId.Skinning));
            Assert.AreEqual(1, skills.With(ProfessionId.Mining, -5).Get(ProfessionId.Mining));
            Assert.AreEqual(255, skills.With(ProfessionId.Mining, 999).Get(ProfessionId.Mining));
        }

        // ---- Gating and difficulty ----

        [Test]
        public void CanGather_NeedsTheNodesSkill()
        {
            var iron = new GatherNode(ProfessionId.Mining, GatherNodeKind.Ore, 21, requiredSkill: 25);
            Assert.IsFalse(ProfessionRules.CanGather(ProfessionSkills.Start.With(ProfessionId.Mining, 24), iron));
            Assert.IsTrue(ProfessionRules.CanGather(ProfessionSkills.Start.With(ProfessionId.Mining, 25), iron));
            Assert.IsFalse(ProfessionRules.CanGather(ProfessionSkills.Start.With(ProfessionId.Herbalism, 99), iron),
                "Another profession's skill doesn't count.");
        }

        [Test]
        public void Difficulty_GoesOrangeYellowGreenGrey_AsTheSkillPassesTheNode()
        {
            Assert.AreEqual(GatherDifficulty.TooHard, ProfessionRules.Difficulty(24, 25, _book));
            Assert.AreEqual(GatherDifficulty.Orange, ProfessionRules.Difficulty(25, 25, _book));
            Assert.AreEqual(GatherDifficulty.Orange, ProfessionRules.Difficulty(34, 25, _book));
            Assert.AreEqual(GatherDifficulty.Yellow, ProfessionRules.Difficulty(35, 25, _book));
            Assert.AreEqual(GatherDifficulty.Yellow, ProfessionRules.Difficulty(49, 25, _book));
            Assert.AreEqual(GatherDifficulty.Green, ProfessionRules.Difficulty(50, 25, _book));
            Assert.AreEqual(GatherDifficulty.Green, ProfessionRules.Difficulty(64, 25, _book));
            Assert.AreEqual(GatherDifficulty.Grey, ProfessionRules.Difficulty(65, 25, _book));
        }

        // ---- Skill-ups ----

        [Test]
        public void GainChance_FollowsTheDifficulty_AndIsZeroAtTheCap()
        {
            Assert.AreEqual(0f, ProfessionRules.GainChance(10, 25, _book), "Too hard: it can't be gathered at all.");
            Assert.AreEqual(1f, ProfessionRules.GainChance(25, 25, _book));
            Assert.AreEqual(0.6f, ProfessionRules.GainChance(40, 25, _book));
            Assert.AreEqual(0.25f, ProfessionRules.GainChance(55, 25, _book));
            Assert.AreEqual(0f, ProfessionRules.GainChance(70, 25, _book));
            Assert.AreEqual(0f, ProfessionRules.GainChance(100, 95, _book), "Nothing past the cap, even orange.");
        }

        [Test]
        public void TryGain_RaisesByOne_WhenTheRollComesInUnderTheChance()
        {
            ProfessionSkills skills = ProfessionSkills.Start.With(ProfessionId.Skinning, 40);
            Assert.IsTrue(ProfessionRules.TryGain(skills, ProfessionId.Skinning, 25, _book, 0.59, out ProfessionSkills after));
            Assert.AreEqual(41, after.Get(ProfessionId.Skinning));
            Assert.AreEqual(1, after.Get(ProfessionId.Mining), "Only the gathering profession rises.");

            Assert.IsFalse(ProfessionRules.TryGain(skills, ProfessionId.Skinning, 25, _book, 0.61, out after));
            Assert.AreEqual(skills, after, "No skill-up leaves the skills alone.");
        }

        [Test]
        public void TryGain_FromOne_ReachesTheCapAndStops()
        {
            ProfessionSkills skills = ProfessionSkills.Start;
            for (int i = 0; i < 200; i++)
            {
                // Always roll 0: every gather of a just-unlocked node counts, so use the best node for the skill.
                int skill = skills.Get(ProfessionId.Mining);
                int node = skill >= 50 ? 50 : skill >= 25 ? 25 : 1;
                ProfessionRules.TryGain(skills, ProfessionId.Mining, node, _book, 0.0, out skills);
            }
            Assert.AreEqual(90, skills.Get(ProfessionId.Mining),
                "With the top node needing 50, it goes grey at 50 + 40 = 90 and teaches nothing more.");

            for (int i = 0; i < 50; i++)
                ProfessionRules.TryGain(skills, ProfessionId.Mining, 95, _book, 0.0, out skills);
            Assert.AreEqual(90, skills.Get(ProfessionId.Mining), "A node it can't gather never raises it.");

            skills = skills.With(ProfessionId.Mining, 95);
            for (int i = 0; i < 50; i++)
                ProfessionRules.TryGain(skills, ProfessionId.Mining, 95, _book, 0.0, out skills);
            Assert.AreEqual(100, skills.Get(ProfessionId.Mining), "It stops at the cap.");
        }

        // ---- Bonus yield and heat maps ----

        [Test]
        public void BonusYieldChance_RisesFromZeroAtTheNodeToTheMost()
        {
            Assert.AreEqual(0f, ProfessionRules.BonusYieldChance(25, 25, _book));
            Assert.AreEqual(0.25f, ProfessionRules.BonusYieldChance(50, 25, _book), 1e-5f);
            Assert.AreEqual(0.5f, ProfessionRules.BonusYieldChance(75, 25, _book), 1e-5f);
            Assert.AreEqual(0.5f, ProfessionRules.BonusYieldChance(100, 1, _book), 1e-5f, "Never more than the most.");
            Assert.AreEqual(0f, ProfessionRules.BonusYieldChance(10, 25, _book), "Below the node: none.");
        }

        [Test]
        public void HeatMap_UnlocksAtTheBooksSkill()
        {
            Assert.IsFalse(ProfessionRules.HasHeatMap(9, _book));
            Assert.IsTrue(ProfessionRules.HasHeatMap(10, _book));
            Assert.IsFalse(ProfessionRules.HasHeatMap(100, null));
        }

        // ---- The book ----

        [Test]
        public void Validate_CatchesDuplicatesBadSkillsAndTheWrongProfession()
        {
            var yield = LootTable.Create(LootSource.Mob, MobId.BristlebackBoar, new LootDrop[0]);
            try
            {
                ProfessionBook book = ProfessionBook.Create(nodes: new[]
                {
                    new GatherNode(ProfessionId.Mining, GatherNodeKind.Ore, 20, 1),
                    new GatherNode(ProfessionId.Mining, GatherNodeKind.Ore, 20, 25),
                    new GatherNode(ProfessionId.Herbalism, GatherNodeKind.Herb, 1310, 0, yield, 1.5f),
                    new GatherNode(ProfessionId.Mining, GatherNodeKind.Mob, (ushort)MobId.ThunderRhino, 40, yield, 1.5f),
                    new GatherNode(ProfessionId.Herbalism, GatherNodeKind.Herb, 1311, 25),
                });
                var problems = book.Validate();
                Object.DestroyImmediate(book);
                Assert.AreEqual(5, problems.Count, string.Join("\n", problems));
            }
            finally
            {
                Object.DestroyImmediate(yield);
            }
        }

        [Test]
        public void TheBuiltBook_IsValid()
        {
            var book = AssetDatabase.LoadAssetAtPath<ProfessionBook>(BookAssetPath);
            Assert.IsNotNull(book, "Run Voxelforge > Build Professions.");
            var problems = book.Validate();
            Assert.AreEqual(0, problems.Count, string.Join("\n", problems));
            Assert.AreEqual(100, book.MaxSkill);
        }
    }
}
