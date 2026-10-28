using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using static Voxelforge.Voxels.VoxelConstants;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// Generates requested chunks on worker threads without stalling the main thread.
    /// <para>
    /// Each <see cref="Update"/> adds finished chunks to the <see cref="VoxelWorld"/> (it never waits on jobs),
    /// then schedules more <see cref="GenerateChunkJob"/>s from the request queue, up to a limit in flight.
    /// <see cref="ChunkStreamer"/> calls <see cref="Request"/> around each viewer, nearest first.
    /// </para>
    /// </summary>
    public sealed class ChunkGenerationScheduler : IDisposable
    {
        sealed class PendingChunk
        {
            public ChunkCoord Coord;
            public NativeArray<Voxel> Voxels;
            public NativeReference<bool> IsUniform;
            public JobHandle Handle;
        }

        readonly VoxelWorld _world;
        readonly TerrainParams _terrain;
        readonly int _maxJobsInFlight;
        NativeArray<BiomeInfo> _biomes;

        readonly Queue<ChunkCoord> _requests = new Queue<ChunkCoord>();
        readonly HashSet<ChunkCoord> _requested = new HashSet<ChunkCoord>(); // Queued or generating.
        readonly List<PendingChunk> _pending = new List<PendingChunk>();
        readonly Stack<PendingChunk> _pool = new Stack<PendingChunk>();
        bool _isDisposed;

        /// <param name="biomes">From <see cref="TerrainSettings.CreateBiomeLookup"/>. The scheduler takes ownership and disposes it.</param>
        /// <param name="maxJobsInFlight">How many chunks may generate at once. Each uses 128 KB of scratch memory.</param>
        public ChunkGenerationScheduler(VoxelWorld world, WorldSeed seed, TerrainParams terrain,
            NativeArray<BiomeInfo> biomes, int maxJobsInFlight = 32)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            Seed = seed;
            _terrain = terrain;
            _biomes = biomes;
            _maxJobsInFlight = math.max(1, maxJobsInFlight);
        }

        public WorldSeed Seed { get; }

        /// <summary>The terrain values every chunk is built with. Decorations read them too.</summary>
        public TerrainParams Terrain => _terrain;

        /// <summary>The biome list, owned by this scheduler. Other jobs may read it while the scheduler is alive.</summary>
        public NativeArray<BiomeInfo> Biomes => _biomes;

        /// <summary>Chunks waiting for a free job slot.</summary>
        public int QueuedCount => _requests.Count;

        /// <summary>Chunks generating on worker threads.</summary>
        public int PendingCount => _pending.Count;

        /// <summary>Chunks added to the world so far.</summary>
        public int GeneratedCount { get; private set; }

        public bool IsIdle => _requests.Count == 0 && _pending.Count == 0;

        /// <summary>
        /// Optional: blocks players changed. A chunk that generates again gets its edits back before anyone sees it,
        /// so digging survives a chunk unloading and loading (D-014).
        /// </summary>
        public VoxelDeltaLog DeltaLog { get; set; }

        /// <summary>
        /// Saved edits whose generated block didn't match what generated this time: the terrain generator changed
        /// since the save was made (Roadmap 3.4). The edits are still put back.
        /// </summary>
        public int MismatchedDeltaCount { get; private set; }

        /// <summary>True while a chunk is queued or generating, so it will appear in the world soon.</summary>
        public bool IsRequested(ChunkCoord coord) => _requested.Contains(coord);

        /// <summary>Queues a chunk to generate. Returns false if it's already loaded or requested.</summary>
        public bool Request(ChunkCoord coord)
        {
            if (_world.TryGetChunk(coord, out _) || !_requested.Add(coord))
                return false;
            _requests.Enqueue(coord);
            return true;
        }

        /// <summary>
        /// Forgets every request that hasn't started yet, so a caller can queue them again in a new order.
        /// Chunks already generating still arrive.
        /// </summary>
        public void CancelQueued()
        {
            foreach (ChunkCoord coord in _requests)
                _requested.Remove(coord);
            _requests.Clear();
        }

        /// <summary>Call once per frame: adds finished chunks to the world, then schedules more.</summary>
        public void Update()
        {
            CollectFinished(waitForAll: false);
            ScheduleRequests();
        }

        /// <summary>Blocks until every requested chunk is in the world. For tests, tools and loading screens.</summary>
        public void CompleteAll()
        {
            while (!IsIdle)
            {
                ScheduleRequests();
                CollectFinished(waitForAll: true);
            }
        }

        void ScheduleRequests()
        {
            while (_pending.Count < _maxJobsInFlight && _requests.Count > 0)
            {
                ChunkCoord coord = _requests.Dequeue();
                PendingChunk pending = _pool.Count > 0 ? _pool.Pop() : new PendingChunk
                {
                    Voxels = new NativeArray<Voxel>(ChunkVolume, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                    IsUniform = new NativeReference<bool>(Allocator.Persistent),
                };
                pending.Coord = coord;
                pending.Handle = new GenerateChunkJob
                {
                    Origin = coord.WorldOrigin,
                    Seed = Seed,
                    Terrain = _terrain,
                    Biomes = _biomes,
                    Voxels = pending.Voxels,
                    IsUniform = pending.IsUniform,
                }.Schedule();
                _pending.Add(pending);
            }
            JobHandle.ScheduleBatchedJobs();
        }

        void CollectFinished(bool waitForAll)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                PendingChunk pending = _pending[i];
                if (!waitForAll && !pending.Handle.IsCompleted)
                    continue;

                pending.Handle.Complete();
                _pending.RemoveAt(i);
                _requested.Remove(pending.Coord);

                // Adding a new chunk never touches existing chunk arrays, so this is safe while meshing jobs run.
                if (!_world.TryGetChunk(pending.Coord, out _))
                {
                    VoxelChunk chunk;
                    if (pending.IsUniform.Value)
                        chunk = _world.CreateChunk(pending.Coord, pending.Voxels[0]);
                    else
                        (chunk = _world.CreateChunk(pending.Coord)).CopyFrom(pending.Voxels);
                    if (DeltaLog != null)
                    {
                        int mismatches = VoxelWorldEditor.ReapplyDeltas(chunk, DeltaLog);
                        if (mismatches > 0 && MismatchedDeltaCount == 0)
                            UnityEngine.Debug.LogWarning($"[Voxelforge] Saved edits in chunk {pending.Coord} were made on " +
                                "different terrain. The terrain generator changed since this world was saved; edits are put back anyway.");
                        MismatchedDeltaCount += mismatches;
                    }
                    GeneratedCount++;
                }
                _pool.Push(pending);
            }
        }

        /// <summary>Waits for running jobs and frees all scratch memory and the biome list.</summary>
        public void Dispose()
        {
            if (_isDisposed)
                return;
            _isDisposed = true;

            foreach (PendingChunk pending in _pending)
            {
                pending.Handle.Complete();
                _pool.Push(pending);
            }
            _pending.Clear();
            _requests.Clear();
            _requested.Clear();

            while (_pool.Count > 0)
            {
                PendingChunk pending = _pool.Pop();
                pending.Voxels.Dispose();
                pending.IsUniform.Dispose();
            }
            if (_biomes.IsCreated)
                _biomes.Dispose();
        }
    }
}
