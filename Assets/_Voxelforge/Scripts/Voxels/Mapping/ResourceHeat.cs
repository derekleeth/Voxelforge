using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Voxelforge.Voxels
{
    /// <summary>How rich in resources one patch of a map tile is (Roadmap 5.7): each ore vein's richness, and its biome.</summary>
    public struct ResourceHeat
    {
        /// <summary>
        /// Each ore vein's richness here, 0-1, in <see cref="TerrainParams.OreVeins"/> order: 0 where it hardly grows, 1
        /// where it's at its richest (<see cref="ResourceHeatRules.OreScore"/>).
        /// </summary>
        public float4 Ore;
        /// <summary>The biome whose ground covers the patch, which decides its herbs.</summary>
        public byte Biome;
    }

    /// <summary>The sums behind the heat maps (Roadmap 5.7), as plain C# so tests can drive them. Burst-safe.</summary>
    public static class ResourceHeatRules
    {
        /// <summary>A heat map tile is <see cref="Cells"/> squared patches, each <see cref="CellSize"/> blocks across.</summary>
        public const int CellSize = 8;
        public const int Cells = MapTile.Size / CellSize;

        // A vein grows where noise times richness passes its threshold, so richness over threshold says how much grows:
        // below LowRatio hardly any, by HighRatio plenty (see ProfessionBuilder's measured shares).
        const float LowRatio = 1.15f;
        const float HighRatio = 1.6f;

        /// <summary>How rich a vein with <paramref name="threshold"/> is where its richness is <paramref name="richness"/>, 0-1.</summary>
        public static float OreScore(float richness, float threshold) =>
            richness <= 0f || threshold <= 0f ? 0f : math.smoothstep(LowRatio, HighRatio, richness / threshold);
    }

    /// <summary>
    /// Samples one heat map tile's patches from the seed, each at its middle column. One row per index, with its own
    /// sampler, like <see cref="SampleMapColumnsJob"/>.
    /// </summary>
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true)]
    public struct SampleResourceHeatJob : IJobParallelFor
    {
        /// <summary>World x, z of the tile's south-west column.</summary>
        public int2 Origin;
        public WorldSeed Seed;
        public TerrainParams Terrain;
        [ReadOnly] public NativeArray<BiomeInfo> Biomes;

        /// <summary><see cref="ResourceHeatRules.Cells"/> squared, row by row from the south-west.</summary>
        [NativeDisableParallelForRestriction, WriteOnly] public NativeArray<ResourceHeat> Heat;

        public void Execute(int row)
        {
            var sampler = new TerrainSampler(Seed, Terrain, Biomes, Allocator.Temp);
            int z = Origin.y + row * ResourceHeatRules.CellSize + ResourceHeatRules.CellSize / 2;
            for (int i = 0; i < ResourceHeatRules.Cells; i++)
            {
                int x = Origin.x + i * ResourceHeatRules.CellSize + ResourceHeatRules.CellSize / 2;
                TerrainColumn column = sampler.SampleColumn(x, z);
                float4 ore = float4.zero;
                for (int v = 0; v < Terrain.OreVeins.Length && v < 4; v++)
                    ore[v] = ResourceHeatRules.OreScore(column.OreRichness[v], Terrain.OreVeins[v].Threshold);
                Heat[row * ResourceHeatRules.Cells + i] = new ResourceHeat { Ore = ore, Biome = (byte)column.Biome };
            }
            sampler.Dispose();
        }
    }

    /// <summary>
    /// The heat maps' data for this world (Roadmap 5.7), a map tile at a time, worked out from the seed on worker threads
    /// (CLAUDE.md Law #4). One per PC, shared by every local player: what's underground is the same for everyone, and each
    /// player's HUD colours it for the tiers they can gather. Works like <see cref="MapTileCache"/>: ask for the tiles you
    /// draw with <see cref="TryGet"/> every frame, and <see cref="Update"/> once a frame.
    /// </summary>
    public sealed class ResourceHeatCache : IDisposable
    {
        sealed class Pending
        {
            public int2 Tile;
            public NativeArray<ResourceHeat> Heat;
            public JobHandle Handle;
        }

        readonly WorldSeed _seed;
        readonly TerrainParams _terrain;
        readonly NativeArray<BiomeInfo> _biomes; // Borrowed.
        readonly int _capacity;
        readonly int _maxJobsInFlight;
        readonly Dictionary<int2, (ResourceHeat[] heat, int lastUsed)> _tiles = new Dictionary<int2, (ResourceHeat[], int)>();
        readonly List<Pending> _pending = new List<Pending>();
        readonly List<int2> _wanted = new List<int2>();
        readonly HashSet<int2> _wantedSet = new HashSet<int2>();
        readonly List<int2> _evict = new List<int2>();
        int _frame;
        bool _isDisposed;

        /// <param name="biomes">Read, not owned: keep it alive until this is disposed.</param>
        public ResourceHeatCache(WorldSeed seed, TerrainParams terrain, NativeArray<BiomeInfo> biomes, int capacity = 512,
            int maxJobsInFlight = 4)
        {
            _seed = seed;
            _terrain = terrain;
            _biomes = biomes;
            _capacity = math.max(1, capacity);
            _maxJobsInFlight = math.max(1, maxJobsInFlight);
        }

        /// <summary>The ore veins the scores are for, in order (block and threshold).</summary>
        public FixedList128Bytes<OreVeinInfo> Veins => _terrain.OreVeins;

        /// <summary>
        /// <paramref name="tile"/>'s patches (<see cref="ResourceHeatRules.Cells"/> squared, row by row from the
        /// south-west), or false (and it's queued) if they aren't worked out yet.
        /// </summary>
        public bool TryGet(int2 tile, out ResourceHeat[] heat)
        {
            if (_tiles.TryGetValue(tile, out (ResourceHeat[] heat, int lastUsed) cached))
            {
                _tiles[tile] = (cached.heat, _frame);
                heat = cached.heat;
                return true;
            }
            if (!_isDisposed && _wantedSet.Add(tile))
                _wanted.Add(tile);
            heat = null;
            return false;
        }

        /// <summary>Collects finished tiles, starts the ones asked for this frame and forgets old ones. Call once a frame.</summary>
        public void Update()
        {
            if (_isDisposed)
                return;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                Pending pending = _pending[i];
                if (!pending.Handle.IsCompleted)
                    continue;
                pending.Handle.Complete();
                _tiles[pending.Tile] = (pending.Heat.ToArray(), _frame);
                pending.Heat.Dispose();
                _pending.RemoveAt(i);
            }
            foreach (int2 tile in _wanted)
            {
                if (_pending.Count >= _maxJobsInFlight)
                    break;
                if (!_tiles.ContainsKey(tile) && !IsPending(tile))
                    _pending.Add(Schedule(tile));
            }
            _wanted.Clear();
            _wantedSet.Clear();
            Evict();
            _frame++;
        }

        /// <summary>Works out <paramref name="tile"/> straight away, waiting for the job. For tests and tools.</summary>
        public ResourceHeat[] SampleNow(int2 tile)
        {
            Pending pending = Schedule(tile);
            pending.Handle.Complete();
            ResourceHeat[] heat = pending.Heat.ToArray();
            pending.Heat.Dispose();
            return heat;
        }

        bool IsPending(int2 tile)
        {
            foreach (Pending pending in _pending)
            {
                if (pending.Tile.Equals(tile))
                    return true;
            }
            return false;
        }

        Pending Schedule(int2 tile)
        {
            var pending = new Pending
            {
                Tile = tile,
                Heat = new NativeArray<ResourceHeat>(ResourceHeatRules.Cells * ResourceHeatRules.Cells, Allocator.Persistent),
            };
            pending.Handle = new SampleResourceHeatJob
            {
                Origin = MapTile.Origin(tile),
                Seed = _seed,
                Terrain = _terrain,
                Biomes = _biomes,
                Heat = pending.Heat,
            }.Schedule(ResourceHeatRules.Cells, 2);
            return pending;
        }

        void Evict()
        {
            int extra = _tiles.Count - _capacity;
            if (extra <= 0)
                return;
            _evict.Clear();
            foreach (KeyValuePair<int2, (ResourceHeat[] heat, int lastUsed)> pair in _tiles)
            {
                if (pair.Value.lastUsed < _frame)
                    _evict.Add(pair.Key);
            }
            _evict.Sort((a, b) => _tiles[a].lastUsed.CompareTo(_tiles[b].lastUsed));
            for (int i = 0; i < extra && i < _evict.Count; i++)
                _tiles.Remove(_evict[i]);
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;
            _isDisposed = true;
            foreach (Pending pending in _pending)
            {
                pending.Handle.Complete();
                pending.Heat.Dispose();
            }
            _pending.Clear();
            _tiles.Clear();
        }
    }
}
