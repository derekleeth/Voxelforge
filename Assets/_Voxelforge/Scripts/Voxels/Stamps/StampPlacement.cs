using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;

namespace Voxelforge.Voxels
{
    /// <summary>One stamp in the world (Roadmap 5.4): which, where its lowest corner is, and how it's turned.</summary>
    public struct StampSite
    {
        /// <summary>Index into the <see cref="StampTable"/>.</summary>
        public int Stamp;
        /// <summary>Its lowest corner (smallest x, y, z), in world blocks.</summary>
        public int3 Min;
        /// <summary>Quarter-turns clockwise seen from above (0-3).</summary>
        public int Turn;
        /// <summary>Its size as placed: x and z swap on a quarter-turn.</summary>
        public int3 Size;

        /// <summary>The column at its middle, where it's measured from and where its map marker goes.</summary>
        public int2 Centre => Min.xz + Size.xz / 2;

        public int3 Max => Min + Size;
    }

    /// <summary>
    /// Where stamps go (Roadmap 5.4), the way <see cref="TreePlacement"/> places trees: the world is cut into square cells
    /// per <see cref="StampKind"/>, and each cell has one <b>candidate</b> site, placed by hash far enough inside the cell
    /// that any stamp of its kind fits. The candidate's biome (at its centre) picks one of the stamps found there, then
    /// a roll against its chance decides if it's really built. Its floor follows the ground at its centre.
    /// <para>
    /// Every answer depends only on the seed, the settings and the cell, so each chunk works out the same sites on its
    /// own, on any thread, and a stamp across a chunk border always lines up. Burst-safe and integer-only, except the
    /// ground height (which the terrain itself already depends on).
    /// </para>
    /// </summary>
    public static class StampPlacement
    {
        /// <summary>One ruin candidate per this many blocks each way.</summary>
        public const int RuinCell = 192;

        /// <summary>One arena candidate per this many blocks each way.</summary>
        public const int ArenaCell = 768;

        const uint StampSalt = 10;

        /// <summary>The per-world number every stamp hash starts from.</summary>
        public static uint SeedHash(WorldSeed seed) => seed.Hash(StampSalt);

        public static int CellSize(StampKind kind) => kind == StampKind.Arena ? ArenaCell : RuinCell;

        /// <summary>
        /// The site in cell (<paramref name="cellX"/>, <paramref name="cellZ"/>) of the <paramref name="kind"/> grid, if
        /// the cell has one.
        /// </summary>
        public static bool TrySite(in StampTable table, ref TerrainSampler sampler, uint seedHash, StampKind kind, int cellX,
            int cellZ, out StampSite site)
        {
            site = default;
            int widest = Widest(table, kind);
            if (widest == 0)
                return false;
            int cell = CellSize(kind);
            int margin = widest / 2 + 2;
            int span = cell - 2 * margin;
            if (span <= 0)
                return false;

            uint k = (uint)kind;
            int cx = cellX * cell + margin + (int)(Hash(cellX, cellZ, seedHash, 10 + k * 8) % (uint)span);
            int cz = cellZ * cell + margin + (int)(Hash(cellX, cellZ, seedHash, 11 + k * 8) % (uint)span);
            int2 fromSpawn = new int2(cx, cz) - table.KeepClearCenter;
            if (table.KeepClearRadius > 0 &&
                (long)fromSpawn.x * fromSpawn.x + (long)fromSpawn.y * fromSpawn.y < (long)table.KeepClearRadius * table.KeepClearRadius)
                return false;

            // Which of the stamps found in this biome, then whether it's built at all.
            int biome = sampler.SurfaceBiome(cx, cz);
            uint biomeBit = biome < 32 ? 1u << biome : 0u;
            int eligible = 0;
            for (int i = 0; i < table.Count; i++)
            {
                if (table.Infos[i].Kind == kind && (table.Infos[i].BiomeMask & biomeBit) != 0)
                    eligible++;
            }
            if (eligible == 0)
                return false;
            int pick = (int)(Hash(cellX, cellZ, seedHash, 12 + k * 8) % (uint)eligible);
            int stamp = -1;
            for (int i = 0; i < table.Count && stamp < 0; i++)
            {
                if (table.Infos[i].Kind == kind && (table.Infos[i].BiomeMask & biomeBit) != 0 && pick-- == 0)
                    stamp = i;
            }
            StampInfo info = table.Infos[stamp];
            float roll = (Hash(cellX, cellZ, seedHash, 13 + k * 8) >> 8) * (1f / 16777216f);
            if (roll >= info.Chance)
                return false;

            int turn = info.Rotate ? (int)(Hash(cellX, cellZ, seedHash, 14 + k * 8) & 3u) : 0;
            int3 size = (turn & 1) == 0 ? info.Size : info.Size.zyx;
            int ground = sampler.SampleColumn(cx, cz).Height;
            site = new StampSite
            {
                Stamp = stamp, Turn = turn, Size = size,
                Min = new int3(cx - size.x / 2, ground + 1 - info.Sink, cz - size.z / 2),
            };
            return true;
        }

        /// <summary>
        /// Every site that may change the chunk at <paramref name="origin"/> (<paramref name="chunkSize"/> blocks each way),
        /// added to <paramref name="into"/>: those over it across the ground and not wholly below it.
        /// </summary>
        public static void SitesTouching(in StampTable table, ref TerrainSampler sampler, uint seedHash, int3 origin,
            int chunkSize, NativeList<StampSite> into)
        {
            for (int kind = 0; kind <= (int)StampKind.Arena; kind++)
            {
                int cell = CellSize((StampKind)kind);
                int x0 = FloorDiv(origin.x, cell), x1 = FloorDiv(origin.x + chunkSize - 1, cell);
                int z0 = FloorDiv(origin.z, cell), z1 = FloorDiv(origin.z + chunkSize - 1, cell);
                for (int cz = z0; cz <= z1; cz++)
                for (int cx = x0; cx <= x1; cx++)
                {
                    if (!TrySite(table, ref sampler, seedHash, (StampKind)kind, cx, cz, out StampSite site))
                        continue;
                    // A foundation can reach any way down, so only sites wholly below the chunk are skipped.
                    bool overlaps = site.Min.x < origin.x + chunkSize && site.Max.x > origin.x &&
                                    site.Min.z < origin.z + chunkSize && site.Max.z > origin.z && site.Max.y > origin.y;
                    if (overlaps)
                        into.Add(site);
                }
            }
        }

        /// <summary>
        /// Every site whose centre is within <paramref name="radius"/> blocks of <paramref name="centre"/>, for the map
        /// (Roadmap 5.4 step C) and the debug keys. Run it in a Burst job to match the chunks exactly: as managed code its
        /// float maths can, rarely, put a floor a block off or pick another biome right on a border.
        /// </summary>
        public static void SitesNear(in StampTable table, ref TerrainSampler sampler, uint seedHash, float2 centre,
            float radius, List<StampSite> into)
        {
            for (int kind = 0; kind <= (int)StampKind.Arena; kind++)
            {
                int cell = CellSize((StampKind)kind);
                int x0 = FloorDiv((int)math.floor(centre.x - radius), cell), x1 = FloorDiv((int)math.ceil(centre.x + radius), cell);
                int z0 = FloorDiv((int)math.floor(centre.y - radius), cell), z1 = FloorDiv((int)math.ceil(centre.y + radius), cell);
                for (int cz = z0; cz <= z1; cz++)
                for (int cx = x0; cx <= x1; cx++)
                {
                    if (TrySite(table, ref sampler, seedHash, (StampKind)kind, cx, cz, out StampSite site) &&
                        math.distancesq((float2)site.Centre, centre) <= radius * radius)
                        into.Add(site);
                }
            }
        }

        /// <summary>
        /// The stamp's block at world block <paramref name="w"/>, or <see cref="StampBlocks.Keep"/> outside it, turned
        /// with the site: each quarter-turn takes the stamp's +x (east) side round to +z (north).
        /// </summary>
        public static ushort BlockAt(in StampTable table, in StampSite site, int3 w)
        {
            int3 p = w - site.Min;
            if (math.any(p < 0) || math.any(p >= site.Size))
                return StampBlocks.Keep;
            StampInfo info = table.Infos[site.Stamp];
            int3 source = Unturn(p, site.Turn, info.Size);
            return table.Blocks[info.Offset + StampDefinition.IndexOf(source, info.Size)];
        }

        /// <summary>The stamp's own block for placed block <paramref name="p"/> (relative to the site's corner).</summary>
        public static int3 Unturn(int3 p, int turn, int3 size)
        {
            switch (turn & 3)
            {
                case 1: return new int3(p.z, p.y, size.z - 1 - p.x);
                case 2: return new int3(size.x - 1 - p.x, p.y, size.z - 1 - p.z);
                case 3: return new int3(size.x - 1 - p.z, p.y, p.x);
                default: return p;
            }
        }

        /// <summary>
        /// Stamps every site in <paramref name="sites"/> onto <paramref name="voxels"/>, the chunk at
        /// <paramref name="origin"/>, whose terrain columns are <paramref name="columns"/>: the stamp's blocks over the
        /// terrain (leaving <see cref="StampBlocks.Keep"/> alone), then its foundation from under its bottom layer down
        /// to the ground. Returns whether it changed anything.
        /// </summary>
        public static bool Apply(in StampTable table, NativeList<StampSite> sites, int3 origin, int chunkSize,
            NativeArray<TerrainColumn> columns, NativeArray<Voxel> voxels)
        {
            bool changed = false;
            for (int s = 0; s < sites.Length; s++)
            {
                StampSite site = sites[s];
                ushort foundation = table.Infos[site.Stamp].Foundation;
                int3 lo = math.max(site.Min, origin);
                int3 hi = math.min(site.Max, origin + chunkSize);
                for (int z = lo.z; z < hi.z; z++)
                for (int x = lo.x; x < hi.x; x++)
                {
                    TerrainColumn column = columns[(z - origin.z) * chunkSize + (x - origin.x)];
                    // The stamp itself.
                    for (int y = lo.y; y < hi.y; y++)
                    {
                        ushort block = BlockAt(table, site, new int3(x, y, z));
                        if (block == StampBlocks.Keep)
                            continue;
                        voxels[VoxelMath.ToIndex(new int3(x, y, z) - origin)] = new Voxel(block);
                        changed = true;
                    }
                    // Its foundation: only under a solid bottom block, from just under it down to the ground.
                    if (foundation == 0)
                        continue;
                    ushort bottom = BlockAt(table, site, new int3(x, site.Min.y, z));
                    if (bottom == StampBlocks.Keep || bottom == BlockIds.Air)
                        continue;
                    int fillLo = math.max(column.Height + 1, origin.y);
                    int fillHi = math.min(site.Min.y, origin.y + chunkSize);
                    for (int y = fillLo; y < fillHi; y++)
                    {
                        voxels[VoxelMath.ToIndex(new int3(x, y, z) - origin)] = new Voxel(foundation);
                        changed = true;
                    }
                }
            }
            return changed;
        }

        /// <summary>Whether column (<paramref name="x"/>, <paramref name="z"/>) is under any site, or within <paramref name="margin"/> of one.</summary>
        public static bool InFootprint(NativeList<StampSite> sites, int x, int z, int margin)
        {
            for (int s = 0; s < sites.Length; s++)
            {
                StampSite site = sites[s];
                if (x >= site.Min.x - margin && x < site.Max.x + margin && z >= site.Min.z - margin && z < site.Max.z + margin)
                    return true;
            }
            return false;
        }

        /// <summary>The widest stamp of <paramref name="kind"/> across the ground, either way.</summary>
        static int Widest(in StampTable table, StampKind kind)
        {
            int widest = 0;
            for (int i = 0; i < table.Count; i++)
            {
                if (table.Infos[i].Kind == kind)
                    widest = math.max(widest, math.max(table.Infos[i].Size.x, table.Infos[i].Size.z));
            }
            return widest;
        }

        static uint Hash(int cellX, int cellZ, uint seedHash, uint salt) =>
            math.hash(new uint4((uint)cellX, (uint)cellZ, seedHash, salt));

        /// <summary>Division that rounds down for negative numbers too (-1 / 6 = -1, not 0).</summary>
        static int FloorDiv(int value, int divisor) => (value >= 0 ? value : value - divisor + 1) / divisor;
    }
}
