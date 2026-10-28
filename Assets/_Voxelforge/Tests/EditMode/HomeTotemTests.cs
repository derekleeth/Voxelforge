using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using Voxelforge.Core;
using Voxelforge.Gameplay;
using Voxelforge.Voxels;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 3.8: Home Totems, landing beside them, and remembering them in the save.</summary>
    public class HomeTotemTests
    {
        static readonly PlayerId P1 = new PlayerId(1);
        static readonly float3 Body = new float3(0.3f, 0.9f, 0.3f);

        VoxelWorld _world;

        // Chunk (0,0,0) is stone up to y = 31 and the chunk above is air, so the ground is at y = 32.
        [SetUp]
        public void SetUp()
        {
            var blocks = new NativeArray<BlockInfo>(BlockIds.HomeTotem + 1, Allocator.Persistent);
            blocks[BlockIds.Air] = new BlockInfo { Id = BlockIds.Air, Flags = BlockFlags.Registered | BlockFlags.Transparent };
            blocks[BlockIds.Stone] = new BlockInfo { Id = BlockIds.Stone, MaxDurability = 60, Flags = BlockFlags.Registered | BlockFlags.Solid };
            blocks[BlockIds.HomeTotem] = new BlockInfo
            {
                Id = BlockIds.HomeTotem, MaxDurability = 60,
                Flags = BlockFlags.Registered | BlockFlags.Solid | BlockFlags.Transparent | BlockFlags.Prop,
            };
            _world = new VoxelWorld(blocks);
            _world.CreateChunk(new ChunkCoord(0, 0, 0), new Voxel(BlockIds.Stone));
            _world.CreateChunk(new ChunkCoord(0, 1, 0));
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        static readonly int3 Totem = new int3(10, 32, 10);

        [Test]
        public void IsStanding_ReadsLoadedBlocks_AndFallsBackToTheDeltaLogForFarAwayOnes()
        {
            var deltas = new VoxelDeltaLog();
            Assert.IsFalse(HomeTotems.IsStanding(_world, deltas, Totem));
            _world.SetVoxel(Totem, new Voxel(BlockIds.HomeTotem));
            Assert.IsTrue(HomeTotems.IsStanding(_world, deltas, Totem));

            var far = new int3(500, 40, 500); // Its chunk isn't loaded.
            Assert.IsFalse(HomeTotems.IsStanding(_world, deltas, far));
            deltas.Record(far, Voxel.Air, new Voxel(BlockIds.HomeTotem), P1);
            Assert.IsTrue(HomeTotems.IsStanding(_world, deltas, far), "Placed by a player, so the log remembers it.");
            deltas.Record(far, new Voxel(BlockIds.HomeTotem), new Voxel(BlockIds.Stone), P1);
            Assert.IsFalse(HomeTotems.IsStanding(_world, deltas, far), "Mined and replaced.");
        }

        [Test]
        public void Landing_IsBesideTheTotem_OnTheGround_FacingIt()
        {
            _world.SetVoxel(Totem, new Voxel(BlockIds.HomeTotem));
            Assert.IsTrue(HomeTotems.TryFindLanding(_world, Totem, Body, out float3 feet, out float yaw));

            Assert.AreEqual(Totem.y, feet.y, 1e-5f, "Standing on the ground beside it.");
            Assert.AreEqual(1f, math.distance(feet.xz, new float2(Totem.x, Totem.z) + 0.5f), 1e-5f, "One block away.");
            Assert.IsTrue(HomeTotems.CanStand(_world, feet, Body));
            float2 facing = new float2(math.sin(math.radians(yaw)), math.cos(math.radians(yaw)));
            float2 towards = math.normalize(new float2(Totem.x, Totem.z) + 0.5f - feet.xz);
            Assert.Greater(math.dot(facing, towards), 0.99f, "Facing the totem.");
        }

        [Test]
        public void Landing_SkipsBlockedSides_AndClimbsOnTopWhenWalledIn()
        {
            _world.SetVoxel(Totem, new Voxel(BlockIds.HomeTotem));
            // Wall in every cell around the totem, two high, and the level above too.
            for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            for (int dy = -1; dy <= 2; dy++)
            {
                if (dx == 0 && dz == 0)
                    continue;
                _world.SetVoxel(Totem + new int3(dx, dy, dz), new Voxel(BlockIds.Stone));
            }
            Assert.IsTrue(HomeTotems.TryFindLanding(_world, Totem, Body, out float3 feet, out _));
            Assert.AreEqual(new float3(Totem.x + 0.5f, Totem.y + 1, Totem.z + 0.5f), feet, "On top of the totem.");

            // Open one side: that's where they land instead.
            _world.SetVoxel(Totem + new int3(1, 0, 0), Voxel.Air);
            _world.SetVoxel(Totem + new int3(1, 1, 0), Voxel.Air);
            Assert.IsTrue(HomeTotems.TryFindLanding(_world, Totem, Body, out feet, out _));
            Assert.AreEqual(new float3(Totem.x + 1.5f, Totem.y, Totem.z + 0.5f), feet);
        }

        [Test]
        public void IsReady_WaitsForTheChunksAroundTheTotem()
        {
            Assert.IsTrue(HomeTotems.IsReady(_world, Totem));
            Assert.IsFalse(HomeTotems.IsReady(_world, new int3(31, 32, 10)), "The chunk to the east isn't generated.");
        }

        // ---- Saving ----

        [Test]
        public void Save_RoundTripsTheHome()
        {
            var uuid = new PlayerUuid(1, 2);
            var save = new WorldSave { Seed = new WorldSeed(7), Deltas = new VoxelDeltaLog() };
            save.Profiles[uuid] = new PlayerProfile { Uuid = uuid, Name = "Player 1", HasHome = true, HomeBlock = new int3(-40, 65, 123) };

            using var memory = new MemoryStream();
            WorldSaveFile.Write(memory, save);
            memory.Position = 0;
            Assert.AreEqual(WorldLoadResult.Loaded, WorldSaveFile.Read(memory, out WorldSave loaded));
            PlayerProfile got = loaded.Profiles[uuid];
            Assert.IsTrue(got.HasHome);
            Assert.AreEqual(new int3(-40, 65, 123), got.HomeBlock);
            Assert.IsTrue(got.Clone().HasHome, "Clone keeps the home.");
        }

        [Test]
        public void Save_FromBeforeHomeTotems_StillLoads_WithNoHome()
        {
            // A format 1 save (Roadmap 3.4), written by hand: one profile with one bag slot.
            var body = new MemoryStream();
            using (var writer = new BinaryWriter(body, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(3); // Generator version.
                writer.Write(42); // Seed.
                writer.Write(DateTime.UtcNow.Ticks);
                DeltaLogCodec.Write(writer, new VoxelDeltaLog());
                writer.Write(1);
                writer.Write(5UL);
                writer.Write(6UL);
                writer.Write("Player 1");
                writer.Write(1f);
                writer.Write(40f);
                writer.Write(2f);
                writer.Write(90f);
                writer.Write(1);
                writer.Write(BlockIds.Log);
                writer.Write((ushort)12);
                writer.Write(0x21444E45u); // "END!"
            }
            var file = new MemoryStream();
            file.Write(new[] { (byte)'V', (byte)'F', (byte)'S', (byte)'V' }, 0, 4);
            file.Write(BitConverter.GetBytes(1), 0, 4);
            using (var gzip = new GZipStream(file, CompressionLevel.Optimal, leaveOpen: true))
                gzip.Write(body.ToArray(), 0, (int)body.Length);
            file.Position = 0;

            Assert.AreEqual(WorldLoadResult.Loaded, WorldSaveFile.Read(file, out WorldSave loaded));
            PlayerProfile profile = loaded.Profiles[new PlayerUuid(5, 6)];
            Assert.AreEqual(new ItemStack(BlockIds.Log, 12), profile.Bag[0]);
            Assert.IsFalse(profile.HasHome);
        }
    }
}
