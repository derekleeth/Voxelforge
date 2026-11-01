using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using Voxelforge.Gameplay;
using Voxelforge.Voxels;

namespace Voxelforge.Tests
{
    /// <summary>
    /// Roadmap 5.4 step C: where the maps and discoveries think the ruins are (<see cref="StampSiteCache"/>), and that a
    /// ruin shows only on the maps of players who've been there.
    /// </summary>
    public class StampMapTests
    {
        static readonly WorldSeed TestSeed = new WorldSeed(20261027);

        TerrainSettings _settings;
        VoxelWorld _world;
        ChunkGenerationScheduler _generator;
        StampSiteCache _cache;
        TerrainSampler _sampler;
        NativeArray<BiomeInfo> _samplerBiomes;

        [SetUp]
        public void SetUp()
        {
            _settings = AssetDatabase.LoadAssetAtPath<TerrainSettings>("Assets/_Voxelforge/Data/Terrain/TerrainSettings.asset");
            var registry = AssetDatabase.LoadAssetAtPath<BlockRegistry>("Assets/_Voxelforge/Data/Blocks/BlockRegistry.asset");
            var library = AssetDatabase.LoadAssetAtPath<StampLibrary>("Assets/_Voxelforge/Data/Stamps/StampLibrary.asset");
            Assert.IsNotNull(library, "Run Voxelforge > Build Stamps.");
            var names = new List<string>();
            foreach (BiomeSettings biome in _settings.Biomes)
                names.Add(biome.Name);
            _world = new VoxelWorld(registry.CreateLookup(Allocator.Persistent));
            _generator = new ChunkGenerationScheduler(_world, TestSeed, _settings.ToTerrainParams(),
                _settings.CreateBiomeLookup(Allocator.Persistent))
            {
                Stamps = library.CreateTable(names, Allocator.Persistent, new int2(256, 256), 96),
            };
            _cache = new StampSiteCache(_generator);
            _samplerBiomes = _settings.CreateBiomeLookup(Allocator.Persistent);
            _sampler = new TerrainSampler(TestSeed, _settings.ToTerrainParams(), _samplerBiomes, Allocator.Persistent);
        }

        [TearDown]
        public void TearDown()
        {
            _sampler.Dispose();
            _samplerBiomes.Dispose();
            _generator.Dispose();
            _world.Dispose();
        }

        /// <summary>A region near the origin with at least one site in it.</summary>
        int2 RegionWithSites()
        {
            for (int z = -2; z <= 2; z++)
            for (int x = -2; x <= 2; x++)
            {
                if (_cache.SitesIn(new int2(x, z)).Count > 0)
                    return new int2(x, z);
            }
            Assert.Fail("No sites near the origin.");
            return default;
        }

        [Test]
        public void Cache_FindsTheSameSitesAsPlacement_EachInItsOwnRegion_AndRemembersThem()
        {
            var managed = new List<StampSite>();
            StampTable table = _generator.Stamps;
            StampPlacement.SitesNear(table, ref _sampler, StampPlacement.SeedHash(TestSeed), new float2(0f, 0f), 2000f, managed);
            var cached = new List<StampSite>();
            _cache.SitesNear(new float2(0f, 0f), 2000f, cached);
            Assert.Greater(cached.Count, 5);

            int missing = 0;
            foreach (StampSite site in cached)
            {
                if (!managed.Exists(m => m.Centre.Equals(site.Centre) && m.Stamp == site.Stamp))
                    missing++;
            }
            for (int z = -2; z <= 2; z++)
            for (int x = -2; x <= 2; x++)
            {
                foreach (StampSite site in _cache.SitesIn(new int2(x, z)))
                    Assert.AreEqual(new int2(x, z), StampSiteCache.RegionOf(site.Centre.x, site.Centre.y), "A site belongs to its region.");
            }
            // The cache runs the chunk jobs' strict Burst maths; this test's sampler is managed and may, rarely, differ.
            Assert.LessOrEqual(missing, 1, "The cache and the placement rules disagree about where the ruins are.");
            Assert.AreSame(_cache.SitesIn(new int2(0, 0)), _cache.SitesIn(new int2(0, 0)), "A region is worked out once.");
        }

        [Test]
        public void Markers_ComeOnlyFromExploredRegions()
        {
            int2 region = RegionWithSites();
            IReadOnlyList<StampSite> sites = _cache.SitesIn(region);
            int2 centre = sites[0].Centre;

            var explored = new ExploredMap();
            explored.Add(new[] { ExploredMap.CellOf(centre.x + 0.5f, centre.y + 0.5f) });
            using var source = new StampMarkers(_cache);
            source.Watch(explored);
            var markers = new List<MapMarker>();
            for (int frame = 0; frame < 3; frame++)
            {
                markers.Clear();
                source.CollectMarkers(markers);
            }
            Assert.AreEqual(sites.Count, markers.Count, "Every site in the explored region, and nothing else.");
            foreach (MapMarker marker in markers)
                Assert.AreEqual(region, StampSiteCache.RegionOf(marker.WorldXZ.x, marker.WorldXZ.y));

            // Exploring further away brings that region's sites in too.
            int2 far = region + new int2(6, 0);
            explored.Add(new[] { ExploredMap.CellOf(far.x * StampSiteCache.RegionSize + 1f, far.y * StampSiteCache.RegionSize + 1f) });
            markers.Clear();
            source.CollectMarkers(markers);
            Assert.AreEqual(sites.Count + _cache.SitesIn(far).Count, markers.Count);
        }

        [Test]
        public void Markers_ShowOnlyOnTheMapsOfPlayersWhoHaveBeenThere()
        {
            int2 centre = _cache.SitesIn(RegionWithSites())[0].Centre;
            var ruin = new MapMarker { Kind = MapMarkerKind.Ruin, WorldXZ = (float2)centre + 0.5f };
            var arena = new MapMarker { Kind = MapMarkerKind.Arena, WorldXZ = (float2)centre + 0.5f };
            var home = new MapMarker { Kind = MapMarkerKind.Home, WorldXZ = (float2)centre + 0.5f };

            var kian = new ExploredMap();
            kian.Add(new[] { ExploredMap.CellOf(centre.x + 0.5f, centre.y + 0.5f) });
            var derek = new ExploredMap();

            Assert.IsTrue(MapMarkerLayer.Shows(ruin, kian));
            Assert.IsTrue(MapMarkerLayer.Shows(arena, kian));
            Assert.IsFalse(MapMarkerLayer.Shows(ruin, derek), "Derek hasn't been there.");
            Assert.IsFalse(MapMarkerLayer.Shows(ruin, null));
            Assert.IsTrue(MapMarkerLayer.Shows(home, derek), "Homes and heroes always show.");
        }
    }
}
