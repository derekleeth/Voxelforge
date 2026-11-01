using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using Voxelforge.Gameplay;
using Voxelforge.Voxels;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 5.2 step D: which mob lives where, how many may be about, and where round a hero they spawn.</summary>
    public class MobSpawnRulesTests
    {
        const string Meadows = "Meadows (Elwynn style)";
        const string Peaks = "Snowy Peaks (Dun Morogh style)";
        const string Plains = "Thunderhoof Plains";

        static readonly MobAttack Bite = new MobAttack { Kind = MobAttackKind.Melee, Damage = 5f, Range = 1.5f, Windup = 0.3f };

        GameObject _body;
        readonly List<MobDefinition> _mobs = new List<MobDefinition>();
        MobDefinition _boar, _wolf, _rhino, _slime, _smallSlime;

        MobDefinition Make(MobId id, string name, MobSpawnRule rule)
        {
            MobDefinition mob = MobDefinition.Create(id, name, 50f, MotorSettings.Default, MobTemperament.Aggressive, 10f, 30f, Bite)
                .WithSpawn(rule);
            // LivesAt needs a body to spawn; a bare Mob on an inactive object is enough here.
            typeof(MobDefinition).GetField("prefab", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(mob, _body.GetComponent<Mob>());
            _mobs.Add(mob);
            return mob;
        }

        [SetUp]
        public void SetUp()
        {
            _body = new GameObject("Mob Body");
            _body.SetActive(false);
            _body.AddComponent<Mob>();
            _mobs.Clear();
            _boar = Make(MobId.BristlebackBoar, "Boar", new MobSpawnRule
                { Place = MobSpawnPlace.Surface, Biomes = new[] { "Meadows" }, GroupMin = 1, GroupMax = 2, Weight = 1f });
            _wolf = Make(MobId.FrostfangWolf, "Wolf", new MobSpawnRule
                { Place = MobSpawnPlace.Surface, Biomes = new[] { "Snowy Peaks" }, AboveSnowLine = true, GroupMin = 3, GroupMax = 4, Weight = 1f });
            _rhino = Make(MobId.ThunderRhino, "Rhino", new MobSpawnRule
                { Place = MobSpawnPlace.Surface, Biomes = new[] { "Thunderhoof Plains" }, GroupMin = 2, GroupMax = 3, Weight = 3f });
            _slime = Make(MobId.CrystalSlime, "Slime", new MobSpawnRule
                { Place = MobSpawnPlace.Cave, GroupMin = 1, GroupMax = 2, Weight = 1f });
            _smallSlime = Make(MobId.CrystalSlimeSmall, "Small Slime", default);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (MobDefinition mob in _mobs)
                Object.DestroyImmediate(mob);
            Object.DestroyImmediate(_body);
        }

        [Test]
        public void EachMob_LivesInItsOwnBiome_OnTheSurface()
        {
            Assert.IsTrue(MobSpawnRules.LivesAt(_boar, Meadows, cave: false, aboveSnowLine: false), "Matched by the start of the name.");
            Assert.IsFalse(MobSpawnRules.LivesAt(_boar, Plains, false, false));
            Assert.IsFalse(MobSpawnRules.LivesAt(_boar, Meadows, cave: true, aboveSnowLine: false), "Not in the caves under it.");
            Assert.IsTrue(MobSpawnRules.LivesAt(_rhino, Plains, false, false));
            Assert.IsTrue(MobSpawnRules.LivesAt(_wolf, Peaks, false, false));
        }

        [Test]
        public void Wolves_AlsoLiveAboveTheSnowLine_InAnyBiome()
        {
            Assert.IsFalse(MobSpawnRules.LivesAt(_wolf, Meadows, false, aboveSnowLine: false));
            Assert.IsTrue(MobSpawnRules.LivesAt(_wolf, Meadows, false, aboveSnowLine: true));
            Assert.IsFalse(MobSpawnRules.LivesAt(_wolf, Meadows, cave: true, aboveSnowLine: true), "A cave isn't up in the snow.");
        }

        [Test]
        public void Slimes_LiveInEveryBiomesCaves_AndSplitOnlySlimesNowhere()
        {
            Assert.IsTrue(MobSpawnRules.LivesAt(_slime, Meadows, cave: true, aboveSnowLine: false));
            Assert.IsTrue(MobSpawnRules.LivesAt(_slime, Plains, cave: true, aboveSnowLine: false));
            Assert.IsFalse(MobSpawnRules.LivesAt(_slime, Plains, cave: false, aboveSnowLine: false));
            Assert.IsFalse(MobSpawnRules.LivesAt(_smallSlime, Plains, cave: true, aboveSnowLine: false));
        }

        [Test]
        public void Pick_GoesByWeight_AmongTheMobsThatLiveThere()
        {
            // On the Plains above the snow line, wolves (weight 1) and rhinos (weight 3) both live.
            Assert.AreSame(_wolf, MobSpawnRules.Pick(_mobs, Plains, false, true, 0f));
            Assert.AreSame(_wolf, MobSpawnRules.Pick(_mobs, Plains, false, true, 0.24f));
            Assert.AreSame(_rhino, MobSpawnRules.Pick(_mobs, Plains, false, true, 0.26f));
            Assert.AreSame(_rhino, MobSpawnRules.Pick(_mobs, Plains, false, true, 1f));
            Assert.AreSame(_slime, MobSpawnRules.Pick(_mobs, "Desert (Tanaris style)", true, false, 0.5f));
            Assert.IsNull(MobSpawnRules.Pick(_mobs, "Desert (Tanaris style)", false, false, 0.5f), "Nothing lives on the desert yet.");
        }

        [Test]
        public void Groups_AreSizedByTheRule_AndFitTheRoomLeft()
        {
            MobSpawnRule pack = _wolf.Spawn;
            Assert.AreEqual(3, MobSpawnRules.GroupSize(pack, 0f, room: 10));
            Assert.AreEqual(4, MobSpawnRules.GroupSize(pack, 0.99f, room: 10));
            Assert.AreEqual(3, MobSpawnRules.GroupSize(pack, 0.99f, room: 3), "Never over the caps.");
            Assert.AreEqual(0, MobSpawnRules.GroupSize(pack, 0.99f, room: 2), "Two wolves aren't a pack: wait for room.");
        }

        [Test]
        public void Room_IsTheTighterOfTheHeroCap_AndTheWorldCap()
        {
            Assert.AreEqual(4, MobSpawnRules.Room(nearHero: 2, perHeroCap: 6, alive: 5, totalCap: 20));
            Assert.AreEqual(1, MobSpawnRules.Room(nearHero: 0, perHeroCap: 6, alive: 19, totalCap: 20));
            Assert.AreEqual(0, MobSpawnRules.Room(nearHero: 7, perHeroCap: 6, alive: 7, totalCap: 20));
        }

        [Test]
        public void RingSpots_AreInTheRing_AndBehindTheHero_WhenAsked()
        {
            var centre = new float2(100f, -50f);
            var facing = new float2(0f, 1f); // Looking north (+z).
            for (int i = 0; i <= 10; i++)
            {
                float roll = i / 10f;
                float2 spot = MobSpawnRules.RingSpot(centre, facing, behind: true, 24f, 56f, roll, roll);
                float distance = math.distance(spot, centre);
                Assert.GreaterOrEqual(distance, 24f - 1e-3f);
                Assert.LessOrEqual(distance, 56f + 1e-3f);
                Assert.LessOrEqual(spot.y - centre.y, 1e-3f, "Behind a hero looking north is south of them.");
            }
        }

        [Test]
        public void Mobs_LeaveWhenEveryHeroIsFarAway_UnlessFighting()
        {
            var heroes = new List<float3> { new float3(0f, 64f, 0f), new float3(200f, 64f, 0f) };
            Assert.IsFalse(MobSpawnRules.ShouldDespawn(new float3(150f, 64f, 0f), false, heroes, 80f), "Near the second hero.");
            Assert.IsTrue(MobSpawnRules.ShouldDespawn(new float3(100f, 64f, 0f), false, heroes, 80f));
            Assert.IsFalse(MobSpawnRules.ShouldDespawn(new float3(100f, 64f, 0f), true, heroes, 80f), "Still chasing someone.");
            Assert.IsTrue(MobSpawnRules.IsNearAny(new float3(10f, 0f, 10f), heroes, 20f));
        }

        [Test]
        public void Book_ChecksSpawnRules()
        {
            MobDefinition broken = Make(MobId.CrystalSlimeMedium, "Broken", new MobSpawnRule
                { Place = MobSpawnPlace.Surface, GroupMin = 3, GroupMax = 2, Weight = 0f });
            MobBook book = MobBook.Create(new[] { broken, _smallSlime });
            try
            {
                string errors = string.Join("\n", book.Validate());
                StringAssert.Contains("'Broken' spawns in groups of 3-2", errors);
                StringAssert.Contains("'Broken' has a spawn place but a weight of 0", errors);
                StringAssert.DoesNotContain("Small Slime' spawns", errors, "Nowhere needs no group.");
            }
            finally
            {
                Object.DestroyImmediate(book);
            }
        }
    }
}
