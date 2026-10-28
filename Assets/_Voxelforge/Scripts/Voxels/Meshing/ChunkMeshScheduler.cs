using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// Turns dirty chunks into meshes without stalling the main thread.
    /// <para>
    /// Each <see cref="Update"/> picks up finished jobs (it never waits on them), then schedules more
    /// from <see cref="VoxelWorld"/>'s dirty queue, up to a limit in flight. Each chunk runs two
    /// chained jobs: <see cref="ChunkGatherJob"/> (copies blocks, short) and then
    /// <see cref="GreedyMeshJob"/> (builds the mesh).
    /// </para>
    /// <para>
    /// <b>Stale results:</b> the scheduler records the version of the chunk and its 6 neighbours.
    /// If any of them changed before the job finished, the mesh is thrown away and the chunk is queued again.
    /// </para>
    /// </summary>
    public sealed class ChunkMeshScheduler : IDisposable
    {
        const int SourceCount = 7; // The chunk itself, then its 6 neighbours in VoxelWorld.NeighbourOffsets order.

        sealed class PendingMesh
        {
            public ChunkCoord Coord;
            public readonly VoxelChunk[] Sources = new VoxelChunk[SourceCount];
            public readonly int[] Versions = new int[SourceCount];
            public NativeArray<ushort> Padded;
            public Mesh.MeshDataArray MeshData;
            public JobHandle GatherHandle;
            public JobHandle MeshHandle;
        }

        readonly VoxelWorld _world;
        readonly Transform _parent;
        readonly Material _material;
        readonly int _maxJobsInFlight;

        readonly Dictionary<ChunkCoord, ChunkRenderer> _renderers = new Dictionary<ChunkCoord, ChunkRenderer>();
        readonly Stack<ChunkRenderer> _rendererPool = new Stack<ChunkRenderer>();
        readonly List<PendingMesh> _pending = new List<PendingMesh>();
        readonly HashSet<ChunkCoord> _pendingCoords = new HashSet<ChunkCoord>();
        readonly List<ChunkCoord> _deferred = new List<ChunkCoord>();
        readonly Stack<NativeArray<ushort>> _paddedPool = new Stack<NativeArray<ushort>>();
        NativeArray<Voxel> _emptyVoxels; // Stands in for chunks that have no voxel array.
        bool _isDisposed;

        /// <param name="parent">Chunk GameObjects are created under this transform.</param>
        /// <param name="maxJobsInFlight">How many chunks may be meshing at once. Each uses about 80 KB of scratch memory.</param>
        public ChunkMeshScheduler(VoxelWorld world, Transform parent, Material material, int maxJobsInFlight = 32)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _parent = parent;
            _material = material;
            _maxJobsInFlight = math.max(1, maxJobsInFlight);
            _emptyVoxels = new NativeArray<Voxel>(1, Allocator.Persistent);
        }

        /// <summary>Chunks currently meshing on worker threads.</summary>
        public int PendingCount => _pending.Count;

        /// <summary>Meshes applied to renderers so far.</summary>
        public int MeshedCount { get; private set; }

        /// <summary>Meshes thrown away because their chunk changed while they were being built.</summary>
        public int DiscardedCount { get; private set; }

        /// <summary>
        /// Optional: returns true for a chunk that isn't loaded yet but will be soon (e.g. it's generating).
        /// Chunks next to one wait for it, instead of meshing now and again when it arrives.
        /// </summary>
        public Func<ChunkCoord, bool> IsChunkIncoming { get; set; }

        /// <summary>Chunks that currently have a renderer (visible or empty).</summary>
        public int RendererCount => _renderers.Count;

        public bool TryGetRenderer(ChunkCoord coord, out ChunkRenderer chunkRenderer) => _renderers.TryGetValue(coord, out chunkRenderer);

        /// <summary>
        /// Hides an unloaded chunk's renderer and keeps it for reuse. Does nothing if the chunk has none.
        /// A mesh job still running for it is thrown away when it finishes, because its chunk is gone.
        /// </summary>
        public void ReleaseRenderer(ChunkCoord coord) => RemoveRenderer(coord);

        /// <summary>Call once per frame: applies finished meshes, then schedules more dirty chunks.</summary>
        public void Update()
        {
            CollectFinished(waitForAll: false);
            ScheduleDirty();
        }

        /// <summary>
        /// Waits for every gather job to finish. Call this before editing a chunk, because a gather job
        /// may still be reading it. Meshing jobs can keep running, since they only read their own copy.
        /// </summary>
        public void CompleteGatherJobs()
        {
            foreach (PendingMesh pending in _pending)
                pending.GatherHandle.Complete();
        }

        /// <summary>Blocks until every chunk in flight has finished, and applies the results.</summary>
        public void CompletePending() => CollectFinished(waitForAll: true);

        /// <summary>Blocks until no chunk is dirty or in flight. For tests, tools and loading screens.</summary>
        public void CompleteAll()
        {
            // A guard against endless loops, in case results keep coming back stale.
            for (int pass = 0; pass < 10_000 && (_world.DirtyCount > 0 || _pending.Count > 0); pass++)
            {
                ScheduleDirty();
                CompletePending();
            }
        }

        void ScheduleDirty()
        {
            int toCheck = _world.DirtyCount;
            while (toCheck-- > 0 && _pending.Count < _maxJobsInFlight && _world.TryTakeDirty(out ChunkCoord coord))
            {
                // Already meshing: try again once that job is done, since its result may now be stale.
                // Waiting on a neighbour: arriving would make this mesh stale straight away.
                if (_pendingCoords.Contains(coord) || IsWaitingOnNeighbour(coord))
                    _deferred.Add(coord);
                else
                    Schedule(coord);
            }

            foreach (ChunkCoord coord in _deferred)
                _world.MarkDirty(coord);
            _deferred.Clear();

            JobHandle.ScheduleBatchedJobs();
        }

        bool IsWaitingOnNeighbour(ChunkCoord coord)
        {
            if (IsChunkIncoming == null)
                return false;
            foreach (int3 offset in VoxelWorld.NeighbourOffsets)
            {
                if (IsChunkIncoming(new ChunkCoord(coord.Value + offset)))
                    return true;
            }
            return false;
        }

        void Schedule(ChunkCoord coord)
        {
            if (!_world.TryGetChunk(coord, out VoxelChunk center))
            {
                RemoveRenderer(coord);
                return;
            }

            var pending = new PendingMesh { Coord = coord };
            pending.Sources[0] = center;
            for (int i = 0; i < 6; i++)
            {
                _world.TryGetChunk(new ChunkCoord(coord.Value + VoxelWorld.NeighbourOffsets[i]), out VoxelChunk neighbour);
                pending.Sources[i + 1] = neighbour;
            }
            for (int i = 0; i < SourceCount; i++)
                pending.Versions[i] = pending.Sources[i]?.Version ?? 0;

            if (IsTriviallyEmpty(pending.Sources))
            {
                if (_renderers.TryGetValue(coord, out ChunkRenderer emptyRenderer))
                    emptyRenderer.Clear();
                return;
            }

            pending.Padded = _paddedPool.Count > 0
                ? _paddedPool.Pop()
                : new NativeArray<ushort>(ChunkGatherJob.PaddedVolume, Allocator.Persistent); // Zeroed: padding corners are never written.

            VoxelChunk[] s = pending.Sources;
            pending.GatherHandle = new ChunkGatherJob
            {
                Center = ArrayOf(s[0]), CenterSource = SourceOf(s[0]),
                NegX = ArrayOf(s[1]), NegXSource = SourceOf(s[1]),
                PosX = ArrayOf(s[2]), PosXSource = SourceOf(s[2]),
                NegY = ArrayOf(s[3]), NegYSource = SourceOf(s[3]),
                PosY = ArrayOf(s[4]), PosYSource = SourceOf(s[4]),
                NegZ = ArrayOf(s[5]), NegZSource = SourceOf(s[5]),
                PosZ = ArrayOf(s[6]), PosZSource = SourceOf(s[6]),
                Padded = pending.Padded,
            }.Schedule();

            pending.MeshData = Mesh.AllocateWritableMeshData(1);
            pending.MeshHandle = new GreedyMeshJob
            {
                Padded = pending.Padded,
                Blocks = _world.Blocks,
                MeshData = pending.MeshData[0],
            }.Schedule(pending.GatherHandle);

            _pending.Add(pending);
            _pendingCoords.Add(coord);
        }

        /// <summary>
        /// Skips the job for chunks that can't have visible faces: all see-through (e.g. all Air), or
        /// solid stone boxed in on all 6 sides by other solid uniform chunks.
        /// </summary>
        bool IsTriviallyEmpty(VoxelChunk[] sources)
        {
            VoxelChunk center = sources[0];
            if (!center.IsUniform)
                return false;
            if (!GreedyMesher.IsOpaque(_world.Blocks, center.UniformVoxel.BlockId))
                return true; // Transparent blocks aren't meshed yet, so a uniform see-through chunk draws nothing.

            for (int i = 1; i < SourceCount; i++)
            {
                VoxelChunk neighbour = sources[i];
                if (neighbour == null || !neighbour.IsUniform || !GreedyMesher.IsOpaque(_world.Blocks, neighbour.UniformVoxel.BlockId))
                    return false;
            }
            return true;
        }

        NativeArray<Voxel>.ReadOnly ArrayOf(VoxelChunk chunk)
        {
            return chunk != null && !chunk.IsUniform ? chunk.GetReadOnlyVoxels() : _emptyVoxels.AsReadOnly();
        }

        static GatherSource SourceOf(VoxelChunk chunk)
        {
            if (chunk == null)
                return new GatherSource { UniformId = BlockIds.Air };
            return chunk.IsUniform
                ? new GatherSource { UniformId = chunk.UniformVoxel.BlockId }
                : new GatherSource { HasArray = true };
        }

        void CollectFinished(bool waitForAll)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                PendingMesh pending = _pending[i];
                if (!waitForAll && !pending.MeshHandle.IsCompleted)
                    continue;

                pending.MeshHandle.Complete();
                _pending.RemoveAt(i);
                _pendingCoords.Remove(pending.Coord);
                _paddedPool.Push(pending.Padded);
                Finish(pending);
            }
        }

        void Finish(PendingMesh pending)
        {
            if (IsStale(pending))
            {
                pending.MeshData.Dispose();
                DiscardedCount++;
                _world.MarkDirty(pending.Coord); // Does nothing if the chunk was unloaded.
                if (!_world.TryGetChunk(pending.Coord, out _))
                    RemoveRenderer(pending.Coord);
                return;
            }

            if (pending.MeshData[0].vertexCount == 0)
            {
                pending.MeshData.Dispose();
                if (_renderers.TryGetValue(pending.Coord, out ChunkRenderer emptyRenderer))
                    emptyRenderer.Clear();
            }
            else
            {
                GetOrCreateRenderer(pending.Coord).ApplyMesh(pending.MeshData);
            }
            MeshedCount++;
        }

        bool IsStale(PendingMesh pending)
        {
            for (int i = 0; i < SourceCount; i++)
            {
                var coord = new ChunkCoord(i == 0 ? pending.Coord.Value : pending.Coord.Value + VoxelWorld.NeighbourOffsets[i - 1]);
                _world.TryGetChunk(coord, out VoxelChunk current);
                if (current != pending.Sources[i] || (current != null && current.Version != pending.Versions[i]))
                    return true;
            }
            return false;
        }

        ChunkRenderer GetOrCreateRenderer(ChunkCoord coord)
        {
            if (!_renderers.TryGetValue(coord, out ChunkRenderer chunkRenderer))
            {
                if (_rendererPool.Count > 0)
                {
                    chunkRenderer = _rendererPool.Pop();
                    chunkRenderer.Reuse(coord);
                }
                else
                {
                    chunkRenderer = ChunkRenderer.Create(coord, _parent, _material);
                }
                _renderers.Add(coord, chunkRenderer);
            }
            return chunkRenderer;
        }

        void RemoveRenderer(ChunkCoord coord)
        {
            if (_renderers.Remove(coord, out ChunkRenderer chunkRenderer))
            {
                chunkRenderer.Release();
                _rendererPool.Push(chunkRenderer);
            }
        }

        static void DestroyObject(UnityEngine.Object obj)
        {
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(obj);
            else
                UnityEngine.Object.DestroyImmediate(obj);
        }

        /// <summary>Waits for running jobs, frees all scratch memory and destroys the chunk GameObjects.</summary>
        public void Dispose()
        {
            if (_isDisposed)
                return;
            _isDisposed = true;

            foreach (PendingMesh pending in _pending)
            {
                pending.MeshHandle.Complete();
                pending.MeshData.Dispose();
                pending.Padded.Dispose();
            }
            _pending.Clear();
            _pendingCoords.Clear();

            while (_paddedPool.Count > 0)
                _paddedPool.Pop().Dispose();
            _emptyVoxels.Dispose();

            foreach (ChunkRenderer chunkRenderer in _renderers.Values)
            {
                if (chunkRenderer != null)
                    DestroyObject(chunkRenderer.gameObject);
            }
            _renderers.Clear();
            while (_rendererPool.Count > 0)
            {
                ChunkRenderer pooled = _rendererPool.Pop();
                if (pooled != null)
                    DestroyObject(pooled.gameObject);
            }
        }
    }
}
