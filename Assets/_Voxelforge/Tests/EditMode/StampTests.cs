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
    /// <summary>
    /// Roadmap 5.4 step A: where stamps go, and that the chunk generator stamps them in the same way from every side.
    /// Uses the real terrain settings, with a test stamp built in code.
    /// </summary>
    public class StampTests
    {
        const string TerrainAssetPath = "Assets/_Voxelforge/Data/Terrain/TerrainSettings.asset";
        static readonly WorldSeed TestSeed = new WorldSeed(20261027);

        // A 40 x 6 x 9 hut: a stone floor, log walls round air, the corner columns left to the terrain, the top layer kept.
        static readonly int3 HutSize = new int3(40, 6, 9);

        TerrainSettings _settings;
        TerrainParams _terrain;
        NativeArray<BiomeInfo> _biomes;
        TerrainSampler _sampler;
        StampDefinition _hut;
        StampTable _table;
        StampTable _noStamps;
        List<string> _biomeNames;

        [SetUp]
        public void SetUp()
        {
            _settings = AssetDatabase.LoadAssetAtPath<TerrainSettings>(TerrainAssetPath);
            Assert.IsNotNull(_settings);
            _biomes = _settings.CreateBiomeLookup(Allocator.Persistent);
            _terrain = _settings.ToTerrainParams();
            _sampler = new TerrainSampler(TestSeed, _terrain, _biomes, Allocator.Persistent);
            _biomeNames = new List<string>();
            foreach (BiomeSettings biome in _settings.Biomes)
                _biomeNames.Add(biome.Name);
            _hut = StampDefinition.Create(StampId.BrokenWatchtower, StampKind.Ruin, HutSize, Hut(), sink: 1,
                foundation: BlockIds.Dirt, biomes: new[] { "Meadows" }, chance: 1f, rotate: true);
            _table = StampTable.Create(new[] { _hut }, _biomeNames, Allocator.Persistent);
            _noStamps = StampTable.CreateEmpty(Allocator.Persistent);
        }

        [TearDown]
        public void TearDown()
        {
            _table.Dispose();
            _noStamps.Dispose();
            _sampler.Dispose();
            if (_biomes.IsCreated)
                _biomes.Dispose();
            Object.DestroyImmediate(_hut);
        }

        static ushort[] Hut()
        {
            var blocks = new ushort[HutSize.x * HutSize.y * HutSize.z];
            for (int y = 0; y < HutSize.y; y++)
            for (int z = 0; z < HutSize.z; z++)
            for (int x = 0; x < HutSize.x; x++)
            {
                bool edge = x == 0 || z == 0 || x == HutSize.x - 1 || z == HutSize.z - 1;
                bool corner = (x == 0 || x == HutSize.x - 1) && (z == 0 || z == HutSize.z - 1);
                ushort block = corner || y == HutSize.y - 1 ? StampBlocks.Keep
                    : y == 0 ? BlockIds.Stone
                    : edge ? BlockIds.Log
                    : BlockIds.Air;
                blocks[StampDefinition.IndexOf(new int3(x, y, z), HutSize)] = block;
            }
            return blocks;
        }

        List<StampSite> SitesNear(in StampTable table, float radius = 2500f)
        {
            var sites = new List<StampSite>();
            StampPlacement.SitesNear(table, ref _sampler, StampPlacement.SeedHash(TestSeed), new float2(0f, 0f), radius, sites);
            return sites;
        }

        NativeArray<Voxel> Generate(int3 origin, in StampTable stamps, out bool uniform)
        {
            var voxels = new NativeArray<Voxel>(ChunkVolume, Allocator.TempJob);
            var isUniform = new NativeReference<bool>(Allocator.TempJob);
            new GenerateChunkJob
            {
                Origin = origin, Seed = TestSeed, Terrain = _terrain, Biomes = _biomes, Voxels = voxels, IsUniform = isUniform,
            }.WithStamps(stamps).Run();
            uniform = isUniform.Value;
            isUniform.Dispose();
            return voxels;
        }

        static ushort At(NativeArray<Voxel> voxels, bool uniform, int3 origin, int3 w) =>
            (uniform ? voxels[0] : voxels[VoxelMath.ToIndex(w - origin)]).BlockId;

        /// <summary>Every chunk origin that site's box (and its foundation, a few blocks down) touches.</summary>
        static IEnumerable<int3> ChunksOver(StampSite site, int below = 8)
        {
            int3 lo = (site.Min - new int3(0, below, 0)) >> 5, hi = (site.Max - 1) >> 5;
            for (int y = lo.y; y <= hi.y; y++)
            for (int z = lo.z; z <= hi.z; z++)
            for (int x = lo.x; x <= hi.x; x++)
                yield return new int3(x, y, z) * ChunkSize;
        }

        [Test]
        public void Turning_ShufflesNoBlocks_AndAQuarterTurnTakesEastToNorth()
        {
            var size = new int3(4, 2, 3);
            for (int turn = 0; turn < 4; turn++)
            {
                int3 placed = (turn & 1) == 0 ? size : size.zyx;
                var seen = new HashSet<int3>();
                for (int y = 0; y < placed.y; y++)
                for (int z = 0; z < placed.z; z++)
                for (int x = 0; x < placed.x; x++)
                {
                    int3 source = StampPlacement.Unturn(new int3(x, y, z), turn, size);
                    Assert.IsTrue(math.all(source >= 0 & source < size), $"Turn {turn}: {source} is outside the stamp.");
                    Assert.IsTrue(seen.Add(source), $"Turn {turn}: two blocks came from {source}.");
                }
            }
            // The stamp's east-south corner (3, 0, 0), a quarter-turn on, is at the placed north edge.
            Assert.AreEqual(new int3(3, 0, 0), StampPlacement.Unturn(new int3(2, 0, 3), 1, size));
        }

        [Test]
        public void Sites_ComeFromTheSeed_AndOnlyInTheirBiomes()
        {
            List<StampSite> first = SitesNear(_table);
            List<StampSite> again = SitesNear(_table);
            Assert.Greater(first.Count, 3, "Meadows ruins every 192 blocks: there should be several within 2.5 km.");
            Assert.AreEqual(first.Count, again.Count);
            for (int i = 0; i < first.Count; i++)
                Assert.AreEqual(first[i].Min, again[i].Min, "Same seed, same sites.");

            foreach (StampSite site in first)
            {
                string biome = _biomeNames[_sampler.SurfaceBiome(site.Centre.x, site.Centre.y)];
                StringAssert.StartsWith("Meadows", biome);
                Assert.AreEqual(_sampler.SampleColumn(site.Centre.x, site.Centre.y).Height + 1 - _hut.Sink, site.Min.y,
                    "Its floor follows the ground at its middle.");
            }
        }

        [Test]
        public void Sites_KeepClearOfTheWorldSpawn()
        {
            StampSite near = SitesNear(_table)[0];
            using StampTable clear = StampTable.Create(new[] { _hut }, _biomeNames, Allocator.Persistent, near.Centre, 300);
            foreach (StampSite site in SitesNear(clear))
                Assert.GreaterOrEqual(math.distance((float2)site.Centre, (float2)near.Centre), 300f);
        }

        [Test]
        public void Sites_NoneWithoutStamps_OrOutsideTheirBiomes()
        {
            Assert.AreEqual(0, SitesNear(_noStamps).Count);
            StampDefinition nowhere = StampDefinition.Create(StampId.FrostShrine, StampKind.Ruin, HutSize, Hut(),
                biomes: new[] { "No Such Biome" });
            using StampTable table = StampTable.Create(new[] { nowhere }, _biomeNames, Allocator.Persistent);
            Assert.AreEqual(0, SitesNear(table).Count);
            Object.DestroyImmediate(nowhere);
        }

        [Test]
        public void Chunks_StampTheSameSite_FromEverySide_AndKeepCellsKeepTheTerrain()
        {
            List<StampSite> sites = SitesNear(_table, 1200f);
            Assert.IsNotEmpty(sites);
            int checkedBlocks = 0, kept = 0;
            foreach (StampSite site in sites)
            {
                foreach (int3 origin in ChunksOver(site))
                {
                    using NativeArray<Voxel> stamped = Generate(origin, _table, out bool uniform);
                    using NativeArray<Voxel> plain = Generate(origin, _noStamps, out bool plainUniform);
                    int3 lo = math.max(site.Min, origin), hi = math.min(site.Max, origin + ChunkSize);
                    for (int y = lo.y; y < hi.y; y++)
                    for (int z = lo.z; z < hi.z; z++)
                    for (int x = lo.x; x < hi.x; x++)
                    {
                        var w = new int3(x, y, z);
                        ushort want = StampPlacement.BlockAt(_table, site, w);
                        ushort got = At(stamped, uniform, origin, w);
                        ushort terrain = At(plain, plainUniform, origin, w);
                        if (want != StampBlocks.Keep)
                        {
                            Assert.AreEqual(want, got, $"Chunk {origin} disagrees with the site at {w}.");
                            checkedBlocks++;
                        }
                        else if (terrain != BlockIds.TreeTrunk)
                        {
                            Assert.AreEqual(terrain, got, $"A Keep cell at {w} changed the terrain.");
                            kept++;
                        }
                    }
                }
            }
            Assert.Greater(checkedBlocks, 1000);
            Assert.Greater(kept, 0);
        }

        [Test]
        public void Chunks_GrowNoTreesThroughAStamp_AndFillFoundationsToTheGround()
        {
            int filled = 0;
            foreach (StampSite site in SitesNear(_table, 1200f))
            {
                foreach (int3 origin in ChunksOver(site))
                {
                    using NativeArray<Voxel> voxels = Generate(origin, _table, out bool uniform);
                    int3 lo = math.max(site.Min - new int3(1, 8, 1), origin);
                    int3 hi = math.min(site.Max + new int3(1, 0, 1), origin + ChunkSize);
                    for (int z = lo.z; z < hi.z; z++)
                    for (int x = lo.x; x < hi.x; x++)
                    {
                        // This sampler runs as managed code, the chunks as strict Burst: the ground can differ by a block.
                        int ground = _sampler.SampleColumn(x, z).Height + 1;
                        ushort bottom = StampPlacement.BlockAt(_table, site, new int3(x, site.Min.y, z));
                        bool underFloor = bottom != StampBlocks.Keep && bottom != BlockIds.Air;
                        for (int y = lo.y; y < hi.y; y++)
                        {
                            ushort got = At(voxels, uniform, origin, new int3(x, y, z));
                            Assert.AreNotEqual(BlockIds.TreeTrunk, got, $"A tree grew through the stamp at {new int3(x, y, z)}.");
                            if (underFloor && y > ground && y < site.Min.y)
                            {
                                Assert.AreEqual(BlockIds.Dirt, got, $"No foundation under the floor at {new int3(x, y, z)}.");
                                filled++;
                            }
                        }
                    }
                }
            }
            Assert.Greater(filled, 0, "No site in 1.2 km sat on a slope: try a bigger search.");
        }

        [Test]
        public void RealLibrary_IsValid_AndEveryStampTurnsUpInTheWorld()
        {
            var library = AssetDatabase.LoadAssetAtPath<StampLibrary>("Assets/_Voxelforge/Data/Stamps/StampLibrary.asset");
            Assert.IsNotNull(library, "Run Voxelforge > Build Stamps.");
            var errors = new List<string>();
            var ids = new HashSet<StampId>();
            foreach (StampDefinition stamp in library.Stamps)
            {
                stamp.Validate(errors);
                Assert.IsTrue(ids.Add(stamp.Id), $"Two stamps share {stamp.Id}.");
                Assert.AreNotEqual(0u, StampTable.BiomeMask(stamp.Biomes, _biomeNames), $"{stamp.name}'s biomes match none.");
            }
            CollectionAssert.IsEmpty(errors);

            using StampTable table = library.CreateTable(_biomeNames, Allocator.Persistent);
            var found = new HashSet<StampId>();
            foreach (StampSite site in SitesNear(table, 8000f))
                found.Add(table.Infos[site.Stamp].Id);
            foreach (StampDefinition stamp in library.Stamps)
                Assert.IsTrue(found.Contains(stamp.Id), $"No {stamp.DisplayName} within 8 km of the origin.");
        }

        [Test]
        public void Library_MatchesBiomesByTheStartOfTheirName()
        {
            uint mask = StampTable.BiomeMask(new[] { "meadows", "Snowy" }, new[] { "Meadows (Elwynn style)", "Desert", "Snowy Peaks" });
            Assert.AreEqual(0b101u, mask);
            var errors = new List<string>();
            StampDefinition broken = StampDefinition.Create(StampId.None, StampKind.Ruin, new int3(2, 2, 2), new ushort[3]);
            broken.Validate(errors);
            Assert.AreEqual(3, errors.Count, "No ID, the wrong number of blocks and no biomes.");
            Object.DestroyImmediate(broken);
        }
    }
}
