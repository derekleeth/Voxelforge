using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// Where creatures can walk (Roadmap 5.1, D-032): one solid/open bit per block for every loaded chunk, kept in step
    /// with the <see cref="VoxelWorld"/>. It is our "NavMesh". The world already is a grid, so there's no mesh to build;
    /// rebuilding after a block edit just flips one bit.
    /// <list type="bullet">
    /// <item>A chunk that arrives is <b>baked</b> on a worker thread (<see cref="NavBakeJob"/>) from a copy of its blocks.
    /// Until then it counts as solid, just as the motor treats chunks still generating.</item>
    /// <item>A block that changes (<c>RequestModifyVoxel</c>, Law #3) flips its bit at once, so a wall someone just built
    /// blocks the very next path search.</item>
    /// <item>Every change stamps its chunk with a new <see cref="Stamp"/>. <see cref="NavPathFollower"/> compares stamps to
    /// notice that a trench or wall appeared along its path, and asks for a new one.</item>
    /// </list>
    /// Path jobs read the bits while they run. They register with <see cref="AddReader"/>, and every change here waits for
    /// them first, so a job never sees half an edit.
    /// </summary>
    public sealed class VoxelNavGrid : IDisposable
    {
        sealed class Bake
        {
            public ChunkCoord Coord;
            public NativeArray<Voxel> Voxels;
            public NativeArray<uint> Rows;
            public JobHandle Handle;
            /// <summary>A block changed after the copy was made, so bake again when this one lands.</summary>
            public bool Stale;
        }

        readonly VoxelWorld _world;
        readonly int _maxBakesInFlight;
        NativeHashMap<int3, int> _slots;
        NativeList<uint> _bits;
        readonly Stack<int> _freeSlots = new Stack<int>();
        readonly Dictionary<ChunkCoord, long> _stamps = new Dictionary<ChunkCoord, long>();
        readonly Queue<ChunkCoord> _bakeQueue = new Queue<ChunkCoord>();
        readonly HashSet<ChunkCoord> _queued = new HashSet<ChunkCoord>();
        readonly List<Bake> _baking = new List<Bake>();
        readonly Stack<Bake> _bakePool = new Stack<Bake>();
        JobHandle _readers;
        long _stamp;
        bool _isDisposed;

        public VoxelNavGrid(VoxelWorld world, int maxBakesInFlight = 16)
        {
            _world = world;
            _maxBakesInFlight = math.max(1, maxBakesInFlight);
            _slots = new NativeHashMap<int3, int>(1024, Allocator.Persistent);
            _bits = new NativeList<uint>(256 * NavGridView.RowsPerChunk, Allocator.Persistent);
            _world.ChunkAdded += OnChunkAdded;
            _world.ChunkRemoved += OnChunkRemoved;
            _world.VoxelChanged += OnVoxelChanged;
            foreach (VoxelChunk chunk in _world.Chunks)
                OnChunkAdded(chunk.Coord);
        }

        /// <summary>The newest change number. Every change to any chunk gets a bigger one than all before it.</summary>
        public long Stamp => _stamp;

        /// <summary>Chunks waiting for, or in the middle of, their bake.</summary>
        public int PendingBakes => _queued.Count + _baking.Count;

        /// <summary>Chunks with nav data. Unbaked and unloaded chunks are solid.</summary>
        public int BakedCount => _slots.Count;

        /// <summary>
        /// Main thread: ask about blocks right now. Reading alongside running path jobs is fine; don't keep the view
        /// across frames, as the bits move when chunks arrive.
        /// </summary>
        public NavGridView View => new NavGridView(_slots, _bits.AsArray());

        /// <summary>For path jobs: the chunk slots, read-only.</summary>
        internal NativeHashMap<int3, int> Slots => _slots;

        /// <summary>For path jobs: every chunk's bits, read-only. Valid until the next change; jobs <see cref="AddReader"/>.</summary>
        internal NativeArray<uint> Bits => _bits.AsArray();

        public bool IsBaked(ChunkCoord coord) => _slots.ContainsKey(coord.Value);

        /// <summary>
        /// The newest change stamp of any chunk from <paramref name="minChunk"/> to <paramref name="maxChunk"/>, or 0 if none
        /// of them ever changed. A path planned at stamp S is out of date once this is above S for the chunks it crosses.
        /// </summary>
        public long StampIn(int3 minChunk, int3 maxChunk)
        {
            long newest = 0;
            for (int y = minChunk.y; y <= maxChunk.y; y++)
            for (int z = minChunk.z; z <= maxChunk.z; z++)
            for (int x = minChunk.x; x <= maxChunk.x; x++)
            {
                if (_stamps.TryGetValue(new ChunkCoord(x, y, z), out long stamp) && stamp > newest)
                    newest = stamp;
            }
            return newest;
        }

        /// <summary>A job reading <see cref="Slots"/> and <see cref="Bits"/>. Changes wait for it to finish.</summary>
        public void AddReader(JobHandle handle) => _readers = JobHandle.CombineDependencies(_readers, handle);

        /// <summary>Once a frame: puts finished bakes in and starts new ones. Never waits for a job.</summary>
        public void Update()
        {
            CollectBakes(waitForAll: false);
            ScheduleBakes();
        }

        /// <summary>Bakes everything waiting, right now. For tests and loading screens; play uses <see cref="Update"/>.</summary>
        public void Flush()
        {
            while (_queued.Count > 0 || _baking.Count > 0)
            {
                ScheduleBakes();
                CollectBakes(waitForAll: true);
            }
        }

        void OnChunkAdded(ChunkCoord coord)
        {
            // Its blocks are copied in after this, so bake it on the next update rather than now.
            if (_queued.Add(coord))
                _bakeQueue.Enqueue(coord);
        }

        void OnChunkRemoved(ChunkCoord coord)
        {
            _queued.Remove(coord); // Its queue entry is skipped by ScheduleBakes.
            if (!_slots.TryGetValue(coord.Value, out int slot))
                return;
            WaitForReaders();
            ReleaseSlot(slot);
            _slots.Remove(coord.Value);
            Touch(coord);
        }

        void OnVoxelChanged(int3 block, Voxel voxel)
        {
            ChunkCoord coord = VoxelMath.WorldToChunk(block);
            foreach (Bake bake in _baking)
            {
                if (bake.Coord == coord)
                    bake.Stale = true;
            }
            // Not baked yet: the bake will copy the block as it is now.
            if (!_slots.TryGetValue(coord.Value, out int slot))
                return;

            bool solid = IsSolidBlock(voxel.BlockId);
            if (slot == (solid ? NavGridView.SolidChunk : NavGridView.OpenChunk))
                return;

            WaitForReaders();
            if (slot < 0)
            {
                // The chunk was all one thing; now it needs its own bits.
                uint fill = slot == NavGridView.SolidChunk ? uint.MaxValue : 0u;
                slot = AllocateSlot();
                NativeArray<uint> bits = _bits.AsArray();
                for (int i = 0; i < NavGridView.RowsPerChunk; i++)
                    bits[slot * NavGridView.RowsPerChunk + i] = fill;
                _slots[coord.Value] = slot;
            }

            int3 local = VoxelMath.WorldToLocal(block);
            int index = slot * NavGridView.RowsPerChunk + NavGridView.RowOf(local);
            uint mask = 1u << local.x;
            _bits[index] = solid ? _bits[index] | mask : _bits[index] & ~mask;
            Touch(coord);
        }

        void ScheduleBakes()
        {
            while (_baking.Count < _maxBakesInFlight && _bakeQueue.Count > 0)
            {
                ChunkCoord coord = _bakeQueue.Dequeue();
                if (!_queued.Remove(coord) || !_world.TryGetChunk(coord, out VoxelChunk chunk))
                    continue;

                if (chunk.IsUniform)
                {
                    // Nothing to work out: the whole chunk is open or solid.
                    WaitForReaders();
                    SetUniform(coord, IsSolidBlock(chunk.UniformVoxel.BlockId));
                    continue;
                }

                Bake bake = _bakePool.Count > 0 ? _bakePool.Pop() : new Bake
                {
                    Voxels = new NativeArray<Voxel>(VoxelConstants.ChunkVolume, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                    Rows = new NativeArray<uint>(NavGridView.RowsPerChunk, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                };
                bake.Coord = coord;
                bake.Stale = false;
                CopyVoxels(chunk, bake.Voxels);
                bake.Handle = new NavBakeJob { Voxels = bake.Voxels, Blocks = _world.Blocks, Rows = bake.Rows }.Schedule();
                _baking.Add(bake);
            }
            JobHandle.ScheduleBatchedJobs();
        }

        void CollectBakes(bool waitForAll)
        {
            for (int i = _baking.Count - 1; i >= 0; i--)
            {
                Bake bake = _baking[i];
                if (!waitForAll && !bake.Handle.IsCompleted)
                    continue;

                bake.Handle.Complete();
                _baking.RemoveAt(i);
                // Unloaded while it baked: throw the result away.
                if (_world.TryGetChunk(bake.Coord, out _))
                {
                    WaitForReaders();
                    ApplyRows(bake.Coord, bake.Rows);
                    if (bake.Stale && _queued.Add(bake.Coord))
                        _bakeQueue.Enqueue(bake.Coord);
                }
                _bakePool.Push(bake);
            }
        }

        void ApplyRows(ChunkCoord coord, NativeArray<uint> rows)
        {
            uint all = uint.MaxValue, any = 0u;
            for (int i = 0; i < rows.Length; i++)
            {
                all &= rows[i];
                any |= rows[i];
            }
            if (any == 0u || all == uint.MaxValue)
            {
                SetUniform(coord, any != 0u);
                return;
            }

            if (!_slots.TryGetValue(coord.Value, out int slot) || slot < 0)
            {
                slot = AllocateSlot();
                _slots[coord.Value] = slot;
            }
            NativeArray<uint>.Copy(rows, 0, _bits.AsArray(), slot * NavGridView.RowsPerChunk, NavGridView.RowsPerChunk);
            Touch(coord);
        }

        void SetUniform(ChunkCoord coord, bool solid)
        {
            if (_slots.TryGetValue(coord.Value, out int slot))
                ReleaseSlot(slot);
            _slots[coord.Value] = solid ? NavGridView.SolidChunk : NavGridView.OpenChunk;
            Touch(coord);
        }

        int AllocateSlot()
        {
            if (_freeSlots.Count > 0)
                return _freeSlots.Pop();
            int slot = _bits.Length / NavGridView.RowsPerChunk;
            _bits.Resize(_bits.Length + NavGridView.RowsPerChunk, NativeArrayOptions.UninitializedMemory);
            return slot;
        }

        void ReleaseSlot(int slot)
        {
            if (slot >= 0)
                _freeSlots.Push(slot);
        }

        // Stamps stay after a chunk unloads, so an unload still counts as a change. They're 20 bytes per chunk ever visited.
        void Touch(ChunkCoord coord) => _stamps[coord] = ++_stamp;

        bool IsSolidBlock(ushort id) => id < _world.Blocks.Length && _world.Blocks[id].IsSolid;

        void WaitForReaders()
        {
            _readers.Complete();
            _readers = default;
        }

        static unsafe void CopyVoxels(VoxelChunk chunk, NativeArray<Voxel> destination)
        {
            NativeArray<Voxel>.ReadOnly source = chunk.GetReadOnlyVoxels();
            UnsafeUtility.MemCpy(destination.GetUnsafePtr(), source.GetUnsafeReadOnlyPtr(),
                (long)VoxelConstants.ChunkVolume * UnsafeUtility.SizeOf<Voxel>());
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;
            _isDisposed = true;

            _world.ChunkAdded -= OnChunkAdded;
            _world.ChunkRemoved -= OnChunkRemoved;
            _world.VoxelChanged -= OnVoxelChanged;
            WaitForReaders();
            foreach (Bake bake in _baking)
            {
                bake.Handle.Complete();
                _bakePool.Push(bake);
            }
            _baking.Clear();
            while (_bakePool.Count > 0)
            {
                Bake bake = _bakePool.Pop();
                bake.Voxels.Dispose();
                bake.Rows.Dispose();
            }
            if (_slots.IsCreated)
                _slots.Dispose();
            if (_bits.IsCreated)
                _bits.Dispose();
        }
    }
}
