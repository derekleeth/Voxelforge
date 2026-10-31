using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using Voxelforge.Voxels;
using static Voxelforge.Voxels.VoxelConstants;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 1.4: terrain is deterministic from the seed, and every biome, cave and floor rule holds.</summary>
    public class TerrainGenerationTests
    {
        const string TerrainAssetPath = "Assets/_Voxelforge/Data/Terrain/TerrainSettings.asset";
        const string RegistryAssetPath = "Assets/_Voxelforge/Data/Blocks/BlockRegistry.asset";

        static readonly WorldSeed TestSeed = new WorldSeed(20261027);

        /// <summary>
        /// Recorded output of <see cref="GoldenChunk_MatchesRecordedHash"/>. If this test fails, terrain output changed:
        /// that's fine on purpose (update this and bump <see cref="TerrainSampler.GeneratorVersion"/>), a bug otherwise.
        /// </summary>
        const uint GoldenHash = 3653086797u; // GeneratorVersion 3: Thunderhoof Plains and mesas.

        TerrainSettings _settings;
        BlockRegistry _registry;
        NativeArray<BiomeInfo> _biomes;
        TerrainParams _terrain;

        [SetUp]
        public void SetUp()
        {
            _settings = AssetDatabase.LoadAssetAtPath<TerrainSettings>(TerrainAssetPath);
            _registry = AssetDatabase.LoadAssetAtPath<BlockRegistry>(RegistryAssetPath);
            Assert.IsNotNull(_settings, $"Missing {TerrainAssetPath}");
            Assert.IsNotNull(_registry, $"Missing {RegistryAssetPath}");
            _biomes = _settings.CreateBiomeLookup(Allocator.Persistent);
            _terrain = _settings.ToTerrainParams();
        }

        [TearDown]
        public void TearDown()
        {
            if (_biomes.IsCreated)
                _biomes.Dispose();
        }

        [Test]
        public void ProjectTerrainSettings_IsValid_WithFiveBiomes()
        {
            CollectionAssert.IsEmpty(_settings.Validate(_registry));
            Assert.AreEqual(5, _settings.Biomes.Count);
        }

        [Test]
        public void SameSeed_GivesIdenticalChunk()
        {
            var coord = new ChunkCoord(3, 2, 5);
            Assert.AreEqual(HashChunk(TestSeed, coord), HashChunk(TestSeed, coord));
        }

        [Test]
        public void DifferentSeed_GivesDifferentChunk()
        {
            var coord = new ChunkCoord(3, 2, 5);
            Assert.AreNotEqual(HashChunk(TestSeed, coord), HashChunk(new WorldSeed(TestSeed.Value + 1), coord));
        }

        [Test]
        public void GoldenChunk_MatchesRecordedHash()
        {
            uint hash = HashChunk(TestSeed, new ChunkCoord(3, 2, 5));
            Assert.AreEqual(GoldenHash, hash, $"Terrain output changed. Actual hash: {hash}u");
        }

        [Test]
        public void Scheduler_GivesSameWorld_InAnyRequestOrder()
        {
            var coords = new List<ChunkCoord>();
            for (int y = 0; y < 3; y++)
            for (int x = 0; x < 2; x++)
                coords.Add(new ChunkCoord(x, y, 1));

            using VoxelWorld forward = GenerateWorld(coords);
            coords.Reverse();
            using VoxelWorld backward = GenerateWorld(coords);

            Assert.AreEqual(coords.Count, forward.ChunkCount);
            foreach (ChunkCoord coord in coords)
            {
                Assert.IsTrue(forward.TryGetChunk(coord, out VoxelChunk a));
                Assert.IsTrue(backward.TryGetChunk(coord, out VoxelChunk b));
                for (int i = 0; i < ChunkVolume; i++)
                {
                    if (a.GetVoxel(i) != b.GetVoxel(i))
                        Assert.Fail($"{coord} differs at index {i}.");
                }
            }
        }

        [Test]
        public void Scheduler_IgnoresDuplicateAndLoadedRequests()
        {
            using var world = new VoxelWorld(_registry.CreateLookup(Allocator.Persistent));
            using var generator = new ChunkGenerationScheduler(world, TestSeed, _terrain, _settings.CreateBiomeLookup(Allocator.Persistent));
            var coord = new ChunkCoord(0, 0, 0);

            Assert.IsTrue(generator.Request(coord));
            Assert.IsFalse(generator.Request(coord));
            Assert.IsTrue(generator.IsRequested(coord));
            generator.CompleteAll();

            Assert.IsFalse(generator.IsRequested(coord));
            Assert.IsFalse(generator.Request(coord), "Already loaded.");
            Assert.AreEqual(1, generator.GeneratedCount);
        }

        [Test]
        public void BiomeWeights_AreNeverNegative_AndSumToOne()
        {
            using var sampler = new TerrainSampler(TestSeed, _terrain, _biomes, Allocator.Temp);
            using var weights = new NativeArray<float>(_biomes.Length, Allocator.Temp);
            for (float t = -1f; t <= 1f; t += 0.1f)
            for (float r = -1f; r <= 1f; r += 0.1f)
            {
                sampler.BiomeWeights(new float2(t, r), weights);
                float sum = 0f;
                foreach (float w in weights)
                {
                    Assert.GreaterOrEqual(w, 0f);
                    sum += w;
                }
                Assert.AreEqual(1f, sum, 1e-5f);
            }
        }

        [Test]
        public void EveryBiome_AppearsInALargeArea()
        {
            using var sampler = new TerrainSampler(TestSeed, _terrain, _biomes, Allocator.Temp);
            var counts = new int[_biomes.Length];
            for (int z = 0; z < 4096; z += 32)
            for (int x = 0; x < 4096; x += 32)
                counts[sampler.SampleColumn(x, z).Biome]++;

            for (int i = 0; i < counts.Length; i++)
                Assert.Greater(counts[i], 0, $"Biome '{_settings.Biomes[i].Name}' never appeared.");
        }

        [Test]
        public void ChunkAboveTheTerrain_IsUniformAir()
        {
            using NativeArray<Voxel> voxels = Generate(TestSeed, new ChunkCoord(0, 20, 0), out bool uniform);
            Assert.IsTrue(uniform);
            Assert.AreEqual(Voxel.Air, voxels[0]);
        }

        [Test]
        public void BottomLayer_IsNeverCarved()
        {
            for (int cz = 0; cz < 3; cz++)
            for (int cx = 0; cx < 3; cx++)
            {
                using NativeArray<Voxel> voxels = Generate(TestSeed, new ChunkCoord(cx, 0, cz), out bool uniform);
                for (int z = 0; z < ChunkSize; z++)
                for (int x = 0; x < ChunkSize; x++)
                {
                    Voxel bottom = uniform ? voxels[0] : voxels[VoxelMath.ToIndex(x, 0, z)];
                    Assert.AreNotEqual(BlockIds.Air, bottom.BlockId, $"Hole in the floor at chunk ({cx}, 0, {cz}), block ({x}, 0, {z}).");
                }
            }
        }

        [Test]
        public void Terrain_HasCavesAndOre_Underground()
        {
            int air = 0, ore = 0;
            for (int cz = 0; cz < 2; cz++)
            for (int cx = 0; cx < 2; cx++)
            {
                using NativeArray<Voxel> voxels = Generate(TestSeed, new ChunkCoord(cx, 1, cz), out bool uniform);
                Assert.IsFalse(uniform);
                foreach (Voxel voxel in voxels)
                {
                    if (voxel.BlockId == BlockIds.Air)
                        air++;
                    else if (voxel.BlockId == BlockIds.ManaCrystalOre)
                        ore++;
                }
            }
            Assert.Greater(air, 0, "No caves between y=32 and y=63.");
            Assert.Greater(ore, 0, "No Mana Crystal Ore between y=32 and y=63.");
        }

        NativeArray<Voxel> Generate(WorldSeed seed, ChunkCoord coord, out bool uniform)
        {
            var voxels = new NativeArray<Voxel>(ChunkVolume, Allocator.TempJob);
            var isUniform = new NativeReference<bool>(Allocator.TempJob);
            new GenerateChunkJob
            {
                Origin = coord.WorldOrigin,
                Seed = seed,
                Terrain = _terrain,
                Biomes = _biomes,
                Voxels = voxels,
                IsUniform = isUniform,
            }.Run();
            uniform = isUniform.Value;
            isUniform.Dispose();
            return voxels;
        }

        /// <summary>FNV-1a over every block ID in the chunk.</summary>
        uint HashChunk(WorldSeed seed, ChunkCoord coord)
        {
            using NativeArray<Voxel> voxels = Generate(seed, coord, out bool uniform);
            uint hash = 2166136261u;
            for (int i = 0; i < ChunkVolume; i++)
            {
                hash ^= (uniform ? voxels[0] : voxels[i]).BlockId;
                hash *= 16777619u;
            }
            return hash;
        }

        VoxelWorld GenerateWorld(List<ChunkCoord> coords)
        {
            var world = new VoxelWorld(_registry.CreateLookup(Allocator.Persistent));
            using var generator = new ChunkGenerationScheduler(world, TestSeed, _terrain, _settings.CreateBiomeLookup(Allocator.Persistent), 2);
            foreach (ChunkCoord coord in coords)
                generator.Request(coord);
            generator.CompleteAll();
            return world;
        }
    }
}
