using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using Voxelforge.Core;
using Voxelforge.Gameplay;
using Voxelforge.Voxels;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 4.6: Earthen Rock crumbles on time (even across a save), and pillars only rise where there's room.</summary>
    public class EarthTests
    {
        const string BlockRegistryPath = "Assets/_Voxelforge/Data/Blocks/BlockRegistry.asset";

        [Test]
        public void Timers_ReportOnlyEarthWhoseTimeIsUp()
        {
            var timers = new EarthTimers();
            timers.Add(new int3(0, 1, 0), 30f);
            timers.Add(new int3(0, 2, 0), 31f);
            var expired = new List<int3>();

            timers.CollectExpired(30.5f, expired);
            CollectionAssert.AreEqual(new[] { new int3(0, 1, 0) }, expired);
            Assert.AreEqual(2, timers.Count, "Collecting doesn't remove: a cell in an unloaded chunk waits.");
            Assert.IsTrue(timers.Remove(new int3(0, 1, 0)));
            Assert.AreEqual(1, timers.Count);
        }

        [Test]
        public void Timers_SaveSecondsLeft_AndCountOnFromTheLoad()
        {
            var timers = new EarthTimers();
            timers.Add(new int3(4, 5, 6), 130f);
            timers.Add(new int3(4, 6, 6), 90f); // Already due when saved: saved as 0, never negative.
            Dictionary<int3, float> saved = timers.Snapshot(100f);
            Assert.AreEqual(30f, saved[new int3(4, 5, 6)], 1e-4f);
            Assert.AreEqual(0f, saved[new int3(4, 6, 6)], 1e-4f);

            var loaded = new EarthTimers();
            loaded.Restore(saved, 5f); // A new session's clock starts again near 0.
            var expired = new List<int3>();
            loaded.CollectExpired(34.9f, expired);
            CollectionAssert.AreEqual(new[] { new int3(4, 6, 6) }, expired);
            expired.Clear();
            loaded.CollectExpired(35f, expired);
            Assert.AreEqual(2, expired.Count, "The other crumbles 30 s after loading.");
        }

        [Test]
        public void Pillar_RisesOnlyWithAirUnderfoot_AndRoomOverhead()
        {
            var blocks = new NativeArray<BlockInfo>(BlockIds.Stone + 1, Allocator.Persistent);
            blocks[BlockIds.Air] = new BlockInfo { Id = BlockIds.Air, Flags = BlockFlags.Registered | BlockFlags.Transparent };
            blocks[BlockIds.Stone] = new BlockInfo { Id = BlockIds.Stone, Flags = BlockFlags.Registered | BlockFlags.Solid };
            var world = new VoxelWorld(blocks);
            try
            {
                world.CreateChunk(new ChunkCoord(0, 0, 0), new Voxel(BlockIds.Stone)); // Stone up to y = 31.
                world.CreateChunk(new ChunkCoord(0, 1, 0)); // Air above.
                var editor = new VoxelWorldEditor(world, new VoxelDeltaLog(), null);
                var feet = new int3(5, 32, 5); // Standing on the stone.

                Assert.IsTrue(EarthRules.CanRise(world, feet));
                editor.RequestModifyVoxel(feet + new int3(0, 2, 0), BlockIds.Stone, new PlayerId(1));
                Assert.IsFalse(EarthRules.CanRise(world, feet), "A ceiling right over the head stops it.");
                Assert.IsFalse(EarthRules.CanRise(world, new int3(5, 31, 5)), "Feet inside stone: no room for rock.");
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void EarthenRock_IsARealBlock_ThatDropsNothing()
        {
            var registry = AssetDatabase.LoadAssetAtPath<BlockRegistry>(BlockRegistryPath);
            Assert.IsTrue(registry.TryGet(BlockIds.EarthenRock, out BlockDefinition earth));
            Assert.IsTrue(earth.IsSolid);
            Assert.AreEqual(0, earth.DropCount);
            Assert.AreEqual(HarvestKind.Pick, earth.HarvestKind);
            Assert.IsFalse(earth.IsBlastProof, "Craters break it like other rock.");
        }

        [Test]
        public void WorldPlayer_IsValid_AndNeverAPlayer()
        {
            Assert.IsTrue(PlayerId.World.IsValid);
            Assert.AreEqual("World", PlayerId.World.ToString());
        }
    }
}
