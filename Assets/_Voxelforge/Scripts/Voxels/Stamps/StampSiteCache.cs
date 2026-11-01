using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// Where the world's stamps are (Roadmap 5.4 step C), for the main thread: the maps' markers, discoveries and the
    /// debug keys. The world is cut into regions of <see cref="RegionSize"/> blocks (one arena cell, four by four ruin
    /// cells); the first time a region is asked about, a Burst job works out its sites with exactly the maths the chunk
    /// jobs use, so a marker is always where the ruin really is. Owned by <see cref="VoxelWorldManager"/>.
    /// </summary>
    public sealed class StampSiteCache
    {
        public const int RegionSize = StampPlacement.ArenaCell;

        readonly ChunkGenerationScheduler _generator;
        readonly Dictionary<int2, StampSite[]> _regions = new Dictionary<int2, StampSite[]>();

        public StampSiteCache(ChunkGenerationScheduler generator) => _generator = generator;

        /// <summary>The stamps the sites refer to (<see cref="StampSite.Stamp"/> indexes it).</summary>
        public StampTable Table => _generator.Stamps;

        public static int2 RegionOf(float x, float z) => (int2)math.floor(new float2(x, z) / RegionSize);

        /// <summary>Every site whose middle is in <paramref name="region"/>, worked out the first time it's asked for.</summary>
        public IReadOnlyList<StampSite> SitesIn(int2 region)
        {
            if (_regions.TryGetValue(region, out StampSite[] sites))
                return sites;
            StampTable table = _generator.Stamps;
            if (table.Count == 0)
                sites = new StampSite[0];
            else
            {
                var found = new NativeList<StampSite>(8, Allocator.TempJob);
                new FindStampSitesJob
                {
                    Region = region, Seed = _generator.Seed, Terrain = _generator.Terrain, Biomes = _generator.Biomes,
                    StampInfos = table.Infos, StampKeepClearCenter = table.KeepClearCenter,
                    StampKeepClearRadius = table.KeepClearRadius, Found = found,
                }.Run();
                sites = found.AsArray().ToArray();
                found.Dispose();
            }
            _regions[region] = sites;
            return sites;
        }

        /// <summary>Every site whose middle is within <paramref name="radius"/> blocks of <paramref name="centre"/>.</summary>
        public void SitesNear(float2 centre, float radius, List<StampSite> into)
        {
            int2 lo = RegionOf(centre.x - radius, centre.y - radius), hi = RegionOf(centre.x + radius, centre.y + radius);
            for (int z = lo.y; z <= hi.y; z++)
            for (int x = lo.x; x <= hi.x; x++)
            {
                foreach (StampSite site in SitesIn(new int2(x, z)))
                {
                    if (math.distancesq((float2)site.Centre, centre) <= radius * radius)
                        into.Add(site);
                }
            }
        }

        /// <summary>The stamp built at <paramref name="site"/>.</summary>
        public StampInfo InfoOf(in StampSite site) => _generator.Stamps.Infos[site.Stamp];
    }

    /// <summary>
    /// Finds the sites in one <see cref="StampSiteCache"/> region, compiled the same strict way as
    /// <see cref="GenerateChunkJob"/> so it agrees with the terrain to the block.
    /// </summary>
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true)]
    public struct FindStampSitesJob : IJob
    {
        public int2 Region;
        public WorldSeed Seed;
        public TerrainParams Terrain;
        [ReadOnly] public NativeArray<BiomeInfo> Biomes;
        [ReadOnly] public NativeArray<StampInfo> StampInfos;
        public int2 StampKeepClearCenter;
        public int StampKeepClearRadius;
        public NativeList<StampSite> Found;

        public void Execute()
        {
            var sampler = new TerrainSampler(Seed, Terrain, Biomes, Allocator.Temp);
            var stamps = new StampTable
            {
                Infos = StampInfos, KeepClearCenter = StampKeepClearCenter, KeepClearRadius = StampKeepClearRadius,
            };
            uint seedHash = StampPlacement.SeedHash(Seed);
            for (int kind = 0; kind <= (int)StampKind.Arena; kind++)
            {
                // A region is a whole number of cells of either kind.
                int perRegion = StampSiteCache.RegionSize / StampPlacement.CellSize((StampKind)kind);
                for (int cz = 0; cz < perRegion; cz++)
                for (int cx = 0; cx < perRegion; cx++)
                {
                    if (StampPlacement.TrySite(stamps, ref sampler, seedHash, (StampKind)kind, Region.x * perRegion + cx,
                            Region.y * perRegion + cz, out StampSite site))
                        Found.Add(site);
                }
            }
            sampler.Dispose();
        }
    }
}
