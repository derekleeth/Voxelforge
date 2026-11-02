using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using Voxelforge.Gameplay;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 5.7 step D: which mobs' bodies can be skinned or salvaged, what it gives and the skill it needs.</summary>
    public class SkinningTests
    {
        const string ProfessionBookPath = "Assets/_Voxelforge/Data/Professions/ProfessionBook.asset";
        const string MobBookPath = "Assets/_Voxelforge/Data/Mobs/MobBook.asset";

        [Test]
        public void EachMob_NeedsItsSkill_AndGivesMoreOfItsOwnMaterial()
        {
            var book = AssetDatabase.LoadAssetAtPath<ProfessionBook>(ProfessionBookPath);
            CollectionAssert.IsEmpty(book.Validate());
            var expected = new Dictionary<MobId, (int skill, ushort first)>
            {
                { MobId.BristlebackBoar, (1, ItemIds.BoarHide) },
                { MobId.FrostfangWolf, (15, ItemIds.WolfPelt) },
                { MobId.CrystalSlimeSmall, (25, ItemIds.CrystalShard) },
                { MobId.ThunderRhino, (40, ItemIds.RhinoHide) },
            };
            foreach (KeyValuePair<MobId, (int skill, ushort first)> mob in expected)
            {
                Assert.IsTrue(book.TryGetNode(GatherNodeKind.Mob, (ushort)mob.Key, out GatherNode node), $"{mob.Key} can't be skinned.");
                Assert.AreEqual(ProfessionId.Skinning, node.Profession);
                Assert.AreEqual(mob.Value.skill, node.RequiredSkill, mob.Key.ToString());
                Assert.Greater(node.GatherSeconds, 0f);
                Assert.AreEqual(LootSource.Gathering, node.Yield.Source);
                Assert.AreEqual(mob.Value.first, node.Yield.Drops[0].Item, $"{mob.Key}: the cast bar shows the first drop.");
                Assert.AreEqual(1f, node.Yield.Drops[0].Chance, $"{mob.Key}: skinning always gives its main material.");
            }
            Assert.IsFalse(book.TryGetNode(GatherNodeKind.Mob, (ushort)MobId.CrystalSlime, out _),
                "A big slime splits instead: only its smallest pieces are salvaged.");
            Assert.IsFalse(book.TryGetNode(GatherNodeKind.Mob, (ushort)MobId.CrystalSlimeMedium, out _));
        }

        [Test]
        public void Slimes_AreSalvaged_EverythingElseIsSkinned()
        {
            var mobs = AssetDatabase.LoadAssetAtPath<MobBook>(MobBookPath);
            foreach (MobDefinition mob in mobs.Mobs)
            {
                bool slime = mob.Id == MobId.CrystalSlime || mob.Id == MobId.CrystalSlimeMedium || mob.Id == MobId.CrystalSlimeSmall;
                Assert.AreEqual(slime, mob.Salvaged, mob.Id.ToString());
            }
        }

        [Test]
        public void SkinningSkill_GoesGreyOnBoars_LongBeforeRhinos()
        {
            var book = AssetDatabase.LoadAssetAtPath<ProfessionBook>(ProfessionBookPath);
            book.TryGetNode(GatherNodeKind.Mob, (ushort)MobId.BristlebackBoar, out GatherNode boar);
            book.TryGetNode(GatherNodeKind.Mob, (ushort)MobId.ThunderRhino, out GatherNode rhino);
            Assert.AreEqual(GatherDifficulty.Grey, ProfessionRules.Difficulty(45, boar.RequiredSkill, book),
                "By 45, boars teach nothing: time to skin rhinos.");
            Assert.AreEqual(GatherDifficulty.Orange, ProfessionRules.Difficulty(45, rhino.RequiredSkill, book));
            Assert.IsFalse(ProfessionRules.CanGather(ProfessionSkills.Start.With(ProfessionId.Skinning, 39), rhino));
        }
    }
}
