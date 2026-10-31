using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Voxelforge.Voxels
{
    /// <summary>Map tile sizes and coordinates (Roadmap 4.8). A tile is <see cref="Size"/> blocks square, one pixel per block.</summary>
    public static class MapTile
    {
        public const int Size = 128;

        /// <summary>The tile plus one column on every side, for slopes and contour lines that cross tile edges.</summary>
        public const int BorderedSize = Size + 2;

        /// <summary>The tile holding world column (<paramref name="x"/>, <paramref name="z"/>). Works for negative columns too.</summary>
        public static int2 Of(int x, int z) => new int2(FloorDiv(x, Size), FloorDiv(z, Size));

        public static int2 Of(float2 worldXZ) => Of((int)math.floor(worldXZ.x), (int)math.floor(worldXZ.y));

        /// <summary>World x, z of the tile's south-west column.</summary>
        public static int2 Origin(int2 tile) => tile * Size;

        static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);
    }

    /// <summary>
    /// Paints the parchment map of the world the seed makes (Roadmap 4.8, D-031), a tile at a time, on worker threads
    /// (CLAUDE.md Law #4). One per PC, shared by every local player's minimap and World Map: the land is the same for
    /// everyone, and each player's fog is drawn over it by their own HUD.
    /// <para>
    /// Ask for the tiles you're about to draw with <see cref="TryGetTile"/> every frame. Tiles nobody asked for this
    /// frame drop out of the queue, so a map that pans away never waits behind tiles it left behind. Finished jobs
    /// become textures in <see cref="Update"/>, which never waits on a job. The least recently drawn tiles are freed
    /// once more than <c>capacity</c> are cached.
    /// </para>
    /// </summary>
    public sealed class MapTileCache : IDisposable
    {
        sealed class Pending
        {
            public int2 Tile;
            public NativeArray<MapColumn> Columns;
            public NativeArray<Color32> Pixels;
            public JobHandle Handle;
        }

        sealed class Cached
        {
            public Texture2D Texture;
            public int LastUsed;
        }

        readonly WorldSeed _seed;
        readonly TerrainParams _terrain;
        readonly NativeArray<BiomeInfo> _biomes; // Borrowed.
        readonly NativeArray<BlockInfo> _blocks; // Borrowed.
        readonly int _capacity;
        readonly int _maxJobsInFlight;

        readonly Dictionary<int2, Cached> _tiles = new Dictionary<int2, Cached>();
        readonly List<Pending> _pending = new List<Pending>();
        readonly List<int2> _wanted = new List<int2>();
        readonly HashSet<int2> _wantedSet = new HashSet<int2>();
        readonly List<int2> _evict = new List<int2>();
        int _frame;
        bool _isDisposed;

        /// <param name="biomes">Read, not owned: keep it alive (e.g. <see cref="ChunkGenerationScheduler.Biomes"/>) until this is disposed.</param>
        /// <param name="blocks">Read, not owned, like <paramref name="biomes"/>: the block lookup, for map colours.</param>
        /// <param name="capacity">Most tiles kept as textures. Each is 64 KB.</param>
        public MapTileCache(WorldSeed seed, TerrainParams terrain, NativeArray<BiomeInfo> biomes, NativeArray<BlockInfo> blocks,
            int capacity = 160, int maxJobsInFlight = 4)
        {
            _seed = seed;
            _terrain = terrain;
            _biomes = biomes;
            _blocks = blocks;
            _capacity = math.max(1, capacity);
            _maxJobsInFlight = math.max(1, maxJobsInFlight);
        }

        public int CachedCount => _tiles.Count;

        public int PendingCount => _pending.Count;

        /// <summary>The painted <paramref name="tile"/>, or false (and it's queued) if it isn't painted yet.</summary>
        public bool TryGetTile(int2 tile, out Texture2D texture)
        {
            if (_tiles.TryGetValue(tile, out Cached cached))
            {
                cached.LastUsed = _frame;
                texture = cached.Texture;
                return true;
            }
            if (!_isDisposed && _wantedSet.Add(tile))
                _wanted.Add(tile);
            texture = null;
            return false;
        }

        /// <summary>Turns finished jobs into textures, starts the tiles asked for this frame, and frees old tiles. Call once a frame.</summary>
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
                Store(pending.Tile, pending.Pixels);
                Release(pending);
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

        /// <summary>Paints <paramref name="tile"/> straight away, waiting for the jobs. For tests and tools, not gameplay.</summary>
        public Texture2D PaintNow(int2 tile)
        {
            if (_tiles.TryGetValue(tile, out Cached cached))
                return cached.Texture;
            Pending pending = Schedule(tile);
            pending.Handle.Complete();
            Texture2D texture = Store(tile, pending.Pixels);
            Release(pending);
            return texture;
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
            int2 origin = MapTile.Origin(tile);
            var pending = new Pending
            {
                Tile = tile,
                Columns = new NativeArray<MapColumn>(MapTile.BorderedSize * MapTile.BorderedSize, Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory),
                Pixels = new NativeArray<Color32>(MapTile.Size * MapTile.Size, Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory),
            };
            JobHandle sampled = new SampleMapColumnsJob
            {
                Origin = origin,
                Seed = _seed,
                Terrain = _terrain,
                Biomes = _biomes,
                Columns = pending.Columns,
            }.Schedule(MapTile.BorderedSize, 4);
            pending.Handle = new PaintMapTileJob
            {
                Origin = origin,
                Columns = pending.Columns,
                Blocks = _blocks,
                Pixels = pending.Pixels,
            }.Schedule(pending.Pixels.Length, 256, sampled);
            return pending;
        }

        Texture2D Store(int2 tile, NativeArray<Color32> pixels)
        {
            var texture = new Texture2D(MapTile.Size, MapTile.Size, TextureFormat.RGBA32, mipChain: false)
            {
                name = $"Map Tile {tile.x},{tile.y}",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            texture.SetPixelData(pixels, 0);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            _tiles[tile] = new Cached { Texture = texture, LastUsed = _frame };
            return texture;
        }

        static void Release(Pending pending)
        {
            pending.Columns.Dispose();
            pending.Pixels.Dispose();
        }

        void Evict()
        {
            int extra = _tiles.Count - _capacity;
            if (extra <= 0)
                return;
            _evict.Clear();
            foreach (KeyValuePair<int2, Cached> pair in _tiles)
            {
                if (pair.Value.LastUsed < _frame)
                    _evict.Add(pair.Key);
            }
            _evict.Sort((a, b) => _tiles[a].LastUsed.CompareTo(_tiles[b].LastUsed));
            for (int i = 0; i < extra && i < _evict.Count; i++)
            {
                DestroyTexture(_tiles[_evict[i]].Texture);
                _tiles.Remove(_evict[i]);
            }
        }

        static void DestroyTexture(Texture2D texture)
        {
            if (Application.isPlaying)
                Object.Destroy(texture);
            else
                Object.DestroyImmediate(texture);
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;
            _isDisposed = true;
            foreach (Pending pending in _pending)
            {
                pending.Handle.Complete();
                Release(pending);
            }
            _pending.Clear();
            foreach (Cached cached in _tiles.Values)
                DestroyTexture(cached.Texture);
            _tiles.Clear();
        }
    }
}
