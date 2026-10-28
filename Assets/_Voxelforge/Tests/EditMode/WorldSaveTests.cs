using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using Voxelforge.Core;
using Voxelforge.Gameplay;
using Voxelforge.Voxels;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 3.4: worlds save as seed + changed blocks + player profiles, and come back exactly.</summary>
    public class WorldSaveTests
    {
        static readonly PlayerId P1 = new PlayerId(1);
        static readonly Voxel Air = Voxel.Air;
        static readonly Voxel Stone = new Voxel(BlockIds.Stone);
        static readonly Voxel Dirt = new Voxel(BlockIds.Dirt);
        static readonly Voxel Log = new Voxel(BlockIds.Log);

        string _folder;

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "VoxelforgeSaveTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_folder))
                Directory.Delete(_folder, recursive: true);
        }

        static VoxelDeltaLog RoundTrip(VoxelDeltaLog log)
        {
            using var memory = new MemoryStream();
            using (var writer = new BinaryWriter(memory, System.Text.Encoding.UTF8, leaveOpen: true))
                DeltaLogCodec.Write(writer, log);
            memory.Position = 0;
            using var reader = new BinaryReader(memory);
            return DeltaLogCodec.Read(reader);
        }

        static void AssertSameDeltas(VoxelDeltaLog expected, VoxelDeltaLog actual)
        {
            Assert.AreEqual(expected.ChunkCount, actual.ChunkCount, "chunks");
            Assert.AreEqual(expected.BlockCount, actual.BlockCount, "blocks");
            foreach (ChunkCoord coord in expected.Chunks)
            {
                IReadOnlyDictionary<int, VoxelDelta> want = expected.GetChunkDeltas(coord);
                IReadOnlyDictionary<int, VoxelDelta> got = actual.GetChunkDeltas(coord);
                Assert.AreEqual(want.Count, got.Count, $"blocks in {coord}");
                foreach (var pair in want)
                {
                    Assert.IsTrue(got.TryGetValue(pair.Key, out VoxelDelta delta), $"block {pair.Key} in {coord}");
                    Assert.AreEqual(pair.Value.Current, delta.Current);
                    Assert.AreEqual(pair.Value.Generated, delta.Generated);
                }
            }
        }

        // ---- Delta log ----

        [Test]
        public void DeltaLog_RoundTrips_AcrossNegativeAndPositiveChunks()
        {
            var log = new VoxelDeltaLog();
            var random = new Unity.Mathematics.Random(1234);
            for (int i = 0; i < 2000; i++)
            {
                int3 block = random.NextInt3(new int3(-100, -40, -100), new int3(100, 120, 100));
                log.Record(block, Stone, random.NextBool() ? Air : Log, P1);
            }
            log.Record(new int3(5, 5, 5), Stone, new Voxel(BlockIds.Dirt, state: 3), P1); // Facing bits survive too.

            AssertSameDeltas(log, RoundTrip(log));
        }

        [Test]
        public void DeltaLog_EmptyLog_RoundTrips()
        {
            VoxelDeltaLog loaded = RoundTrip(new VoxelDeltaLog());
            Assert.AreEqual(0, loaded.ChunkCount);
            Assert.AreEqual(0, loaded.BlockCount);
        }

        [Test]
        public void RestoredLog_StillKeepsOnlyTheNetChange()
        {
            var log = new VoxelDeltaLog();
            log.Record(new int3(1, 2, 3), Stone, Air, P1);
            VoxelDeltaLog loaded = RoundTrip(log);

            loaded.Record(new int3(1, 2, 3), Air, Dirt, P1);
            Assert.IsTrue(loaded.TryGet(new int3(1, 2, 3), out VoxelDelta delta));
            Assert.AreEqual(Stone, delta.Generated, "A loaded block remembers what was generated there.");

            loaded.Record(new int3(1, 2, 3), Dirt, Stone, P1);
            Assert.AreEqual(0, loaded.BlockCount, "Putting the generated block back removes the delta.");
            Assert.AreEqual(0, loaded.ChunkCount);
        }

        [Test]
        public void Restore_SkipsDeltasThatChangeNothing()
        {
            var log = new VoxelDeltaLog();
            log.Restore(new ChunkCoord(0, 0, 0), 7, Stone, Stone);
            Assert.AreEqual(0, log.BlockCount);
        }

        [Test]
        public void ReapplyDeltas_CountsBlocksTheGeneratorMadeDifferently()
        {
            var blocks = new NativeArray<BlockInfo>(BlockIds.Log + 1, Allocator.Persistent);
            using var world = new VoxelWorld(blocks);
            VoxelChunk chunk = world.CreateChunk(new ChunkCoord(0, 0, 0), Stone);

            var log = new VoxelDeltaLog();
            log.Restore(chunk.Coord, 10, Air, Stone); // Saved on stone: matches.
            log.Restore(chunk.Coord, 20, Air, Dirt);  // Saved on dirt, but stone generated: the generator changed.

            Assert.AreEqual(1, VoxelWorldEditor.ReapplyDeltas(chunk, log));
            Assert.AreEqual(Air, chunk.GetVoxel(10));
            Assert.AreEqual(Air, chunk.GetVoxel(20), "Edits are put back even on changed terrain.");
        }

        // ---- Whole save file ----

        static WorldSave SampleSave()
        {
            var save = new WorldSave
            {
                Seed = new WorldSeed(-987654),
                GeneratorVersion = 2,
                SavedUtc = new DateTime(2026, 10, 28, 15, 30, 0, DateTimeKind.Utc),
            };
            save.Deltas.Record(new int3(-33, 10, 64), Stone, Air, P1);
            save.Deltas.Record(new int3(0, 70, 0), Air, Log, P1);

            var p1 = new PlayerProfile { Uuid = PlayerUuid.NewUuid(), Name = "Player 1", Feet = new float3(256.5f, 71f, -12.25f), Yaw = 135f };
            p1.Bag.Add(new ItemStack(BlockIds.Dirt, 12));
            p1.Bag.Add(new ItemStack(BlockIds.Stone, 0)); // Empty slots keep their place.
            p1.Bag.Add(new ItemStack(BlockIds.Log, 4));
            var p2 = new PlayerProfile { Uuid = PlayerUuid.NewUuid(), Name = "Player 2", Feet = new float3(-3f, 40f, 9f), Yaw = 0f };
            save.Profiles.Add(p1.Uuid, p1);
            save.Profiles.Add(p2.Uuid, p2);
            return save;
        }

        static WorldLoadResult ReadBytes(byte[] bytes, out WorldSave save)
        {
            using var memory = new MemoryStream(bytes);
            return WorldSaveFile.Read(memory, out save);
        }

        static byte[] WriteBytes(WorldSave save)
        {
            using var memory = new MemoryStream();
            WorldSaveFile.Write(memory, save);
            return memory.ToArray();
        }

        [Test]
        public void WorldSave_RoundTrips_SeedDeltasAndProfiles()
        {
            WorldSave save = SampleSave();
            Assert.AreEqual(WorldLoadResult.Loaded, ReadBytes(WriteBytes(save), out WorldSave loaded));

            Assert.AreEqual(save.Seed, loaded.Seed);
            Assert.AreEqual(save.GeneratorVersion, loaded.GeneratorVersion);
            Assert.AreEqual(save.SavedUtc, loaded.SavedUtc);
            AssertSameDeltas(save.Deltas, loaded.Deltas);

            Assert.AreEqual(2, loaded.Profiles.Count);
            foreach (PlayerProfile want in save.Profiles.Values)
            {
                Assert.IsTrue(loaded.Profiles.TryGetValue(want.Uuid, out PlayerProfile got));
                Assert.AreEqual(want.Name, got.Name);
                Assert.AreEqual(want.Feet, got.Feet);
                Assert.AreEqual(want.Yaw, got.Yaw);
                CollectionAssert.AreEqual(want.Bag, got.Bag, "Bag slots come back in the same order.");
            }
        }

        [Test]
        public void Read_RefusesFilesThatAreNotSaves()
        {
            Assert.AreEqual(WorldLoadResult.NotASave, ReadBytes(System.Text.Encoding.UTF8.GetBytes("hello, world"), out WorldSave save));
            Assert.IsNull(save);
            Assert.AreEqual(WorldLoadResult.NotASave, ReadBytes(new byte[2], out _));
        }

        [Test]
        public void Read_RefusesSavesFromANewerVersion()
        {
            byte[] bytes = WriteBytes(SampleSave());
            BitConverter.GetBytes(WorldSaveFile.FormatVersion + 1).CopyTo(bytes, 4);
            Assert.AreEqual(WorldLoadResult.NewerFormat, ReadBytes(bytes, out _));
        }

        [Test]
        public void Read_ReportsFilesCutShortAsCorrupt()
        {
            byte[] bytes = WriteBytes(SampleSave());
            // The last 8 bytes are the GZip footer, which decoders don't always check, so cut into the data itself.
            foreach (int length in new[] { 9, bytes.Length / 2, bytes.Length - 12 })
            {
                var cut = new byte[length];
                Array.Copy(bytes, cut, length);
                Assert.AreEqual(WorldLoadResult.Corrupt, ReadBytes(cut, out WorldSave save), $"cut to {length} bytes");
                Assert.IsNull(save);
            }
        }

        [Test]
        public void TenThousandEditedBlocks_CompressSmall()
        {
            // A 3x3 tunnel 1,100 blocks long plus a 10x10 wall: the kind of edits players really make.
            var save = new WorldSave { Seed = new WorldSeed(1) };
            for (int x = 0; x < 1100; x++)
            for (int y = 0; y < 3; y++)
            for (int z = 0; z < 3; z++)
                save.Deltas.Record(new int3(x, 30 + y, z), Stone, Air, P1);
            for (int x = 0; x < 10; x++)
            for (int y = 0; y < 10; y++)
                save.Deltas.Record(new int3(x, 80 + y, 50), Air, Log, P1);
            Assert.AreEqual(10_000, save.Deltas.BlockCount);

            int bytes = WriteBytes(save).Length;
            TestContext.WriteLine($"10,000 edited blocks saved in {bytes:N0} bytes.");
            Assert.Less(bytes, 64 * 1024);
        }

        [Test]
        public void WriteFile_ReplacesTheSave_AndKeepsTheOldOneAsBackup()
        {
            string path = Path.Combine(_folder, "World1", WorldSaveManager.SaveFileName);
            WorldSave first = SampleSave();
            WorldSaveFile.WriteFile(path, WorldSaveFile.EncodeBody(first));

            WorldSave second = SampleSave();
            second.Seed = new WorldSeed(42);
            long size = WorldSaveFile.WriteFile(path, WorldSaveFile.EncodeBody(second));

            Assert.Greater(size, 0);
            Assert.IsFalse(File.Exists(path + ".tmp"));
            Assert.AreEqual(WorldLoadResult.Loaded, WorldSaveFile.ReadFile(path, out WorldSave latest));
            Assert.AreEqual(new WorldSeed(42), latest.Seed);
            Assert.AreEqual(WorldLoadResult.Loaded, WorldSaveFile.ReadFile(path + ".bak", out WorldSave backup));
            Assert.AreEqual(first.Seed, backup.Seed);
        }

        [Test]
        public void ReadFile_MissingSave_IsNotFound()
        {
            Assert.AreEqual(WorldLoadResult.NotFound, WorldSaveFile.ReadFile(Path.Combine(_folder, "nope.vfsave"), out _));
        }

        // ---- Bag and identities ----

        [Test]
        public void BlockStacks_Restore_KeepsSlotOrder_MergesRepeats_AndCaps()
        {
            var stacks = new ItemStacks();
            stacks.Add(BlockIds.Stone, 50, out _);
            stacks.Restore(new[]
            {
                new ItemStack(BlockIds.Log, 4),
                new ItemStack(BlockIds.Dirt, 0),
                new ItemStack(BlockIds.Log, 3),
                new ItemStack(BlockIds.Snow, 2000),
            });

            Assert.AreEqual(3, stacks.Stacks.Count, "The old contents are gone and the repeated Log merged.");
            Assert.AreEqual(new ItemStack(BlockIds.Log, 7), stacks.Stacks[0]);
            Assert.AreEqual(new ItemStack(BlockIds.Dirt, 0), stacks.Stacks[1]);
            Assert.AreEqual(ItemStacks.MaxPerItem, stacks.CountOf(BlockIds.Snow));
            Assert.AreEqual(0, stacks.CountOf(BlockIds.Stone));
        }

        [Test]
        public void LocalPlayerIdentities_AreStablePerSlot_AndSurviveARestart()
        {
            string path = Path.Combine(_folder, LocalPlayerIdentities.FileName);
            var first = new LocalPlayerIdentities(path);
            PlayerUuid slot0 = first.Get(0);
            PlayerUuid slot1 = first.Get(1);

            Assert.IsTrue(slot0.IsValid);
            Assert.AreNotEqual(slot0, slot1);
            Assert.AreEqual(slot0, first.Get(0));

            var afterRestart = new LocalPlayerIdentities(path);
            Assert.AreEqual(slot0, afterRestart.Get(0));
            Assert.AreEqual(slot1, afterRestart.Get(1));
        }

        [Test]
        public void LocalPlayerIdentities_DamagedFile_GivesNewIdentities()
        {
            string path = Path.Combine(_folder, LocalPlayerIdentities.FileName);
            File.WriteAllText(path, "{ not json");
            var identities = new LocalPlayerIdentities(path);
            Assert.IsTrue(identities.Get(0).IsValid);
        }
    }
}
