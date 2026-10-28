using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// Every loaded chunk, plus the queue of chunks whose mesh is out of date.
    /// Plain C# (not a MonoBehaviour) so tests can create one without a scene.
    /// <para>
    /// Edit methods are <c>internal</c>: gameplay code goes through
    /// <c>VoxelWorldManager.RequestModifyVoxel</c> (Roadmap 3.1), never through this class.
    /// </para>
    /// </summary>
    public sealed class VoxelWorld : IDisposable
    {
        /// <summary>The 6 face neighbours, in the same order <see cref="ChunkMeshScheduler"/> gathers them.</summary>
        internal static readonly int3[] NeighbourOffsets =
        {
            new int3(-1, 0, 0), new int3(1, 0, 0),
            new int3(0, -1, 0), new int3(0, 1, 0),
            new int3(0, 0, -1), new int3(0, 0, 1),
        };

        readonly Dictionary<ChunkCoord, VoxelChunk> _chunks = new Dictionary<ChunkCoord, VoxelChunk>();
        readonly Queue<ChunkCoord> _dirtyQueue = new Queue<ChunkCoord>();
        readonly HashSet<ChunkCoord> _dirtySet = new HashSet<ChunkCoord>();
        NativeArray<BlockInfo> _blocks;

        /// <summary>Takes ownership of <paramref name="blocks"/> (from <see cref="BlockRegistry.CreateLookup"/>) and disposes it.</summary>
        public VoxelWorld(NativeArray<BlockInfo> blocks)
        {
            _blocks = blocks;
        }

        /// <summary>Block lookup indexed by ID, shared read-only by every meshing job.</summary>
        public NativeArray<BlockInfo> Blocks => _blocks;

        public int ChunkCount => _chunks.Count;

        /// <summary>Chunks waiting to be (re)meshed.</summary>
        public int DirtyCount => _dirtySet.Count;

        public IEnumerable<VoxelChunk> Chunks => _chunks.Values;

        public bool TryGetChunk(ChunkCoord coord, out VoxelChunk chunk) => _chunks.TryGetValue(coord, out chunk);

        /// <summary>The block at a world position. Positions in unloaded chunks read as Air.</summary>
        public Voxel GetVoxel(int3 world)
        {
            return _chunks.TryGetValue(VoxelMath.WorldToChunk(world), out VoxelChunk chunk)
                ? chunk.GetVoxel(VoxelMath.WorldToLocal(world))
                : Voxel.Air;
        }

        /// <summary>
        /// Adds a new chunk filled with <paramref name="fill"/> and queues it, and its loaded
        /// neighbours (whose border faces may now be hidden), for meshing.
        /// </summary>
        internal VoxelChunk CreateChunk(ChunkCoord coord, Voxel fill = default)
        {
            if (_chunks.ContainsKey(coord))
                throw new InvalidOperationException($"{coord} already exists.");

            var chunk = new VoxelChunk(coord, fill);
            _chunks.Add(coord, chunk);
            MarkDirty(coord);
            foreach (int3 offset in NeighbourOffsets)
                MarkDirty(new ChunkCoord(coord.Value + offset));
            return chunk;
        }

        /// <summary>
        /// Unloads a chunk and frees its memory. Returns false if it wasn't loaded.
        /// Neighbours are <b>not</b> remeshed: their faces toward the gap stay hidden, which is fine at the edge of
        /// the loaded area (D-010). Callers must make sure no gather job is reading the chunk
        /// (<see cref="ChunkMeshScheduler.CompleteGatherJobs"/>).
        /// </summary>
        internal bool RemoveChunk(ChunkCoord coord)
        {
            if (!_chunks.Remove(coord, out VoxelChunk chunk))
                return false;

            chunk.Dispose();
            _dirtySet.Remove(coord); // Its queue entry is skipped by TryTakeDirty.
            return true;
        }

        /// <summary>
        /// Sets one block and queues the remeshing it needs. Returns false if the chunk isn't loaded or
        /// nothing changed. Callers must make sure no gather job is reading the chunk
        /// (<see cref="ChunkMeshScheduler.CompleteGatherJobs"/>).
        /// </summary>
        internal bool SetVoxel(int3 world, Voxel voxel)
        {
            ChunkCoord coord = VoxelMath.WorldToChunk(world);
            if (!_chunks.TryGetValue(coord, out VoxelChunk chunk))
                return false;

            int3 local = VoxelMath.WorldToLocal(world);
            if (!chunk.SetVoxel(local, voxel))
                return false;

            MarkDirty(coord);
            // A block on the chunk's edge also changes which faces the neighbour can see.
            for (int axis = 0; axis < 3; axis++)
            {
                if (local[axis] == 0)
                    MarkDirty(new ChunkCoord(coord.Value + NeighbourOffsets[axis * 2]));
                else if (local[axis] == VoxelConstants.ChunkMask)
                    MarkDirty(new ChunkCoord(coord.Value + NeighbourOffsets[axis * 2 + 1]));
            }
            return true;
        }

        /// <summary>Queues a loaded chunk for meshing. Unloaded or already-queued chunks are ignored.</summary>
        internal void MarkDirty(ChunkCoord coord)
        {
            if (_chunks.ContainsKey(coord) && _dirtySet.Add(coord))
                _dirtyQueue.Enqueue(coord);
        }

        /// <summary>Takes the oldest chunk waiting for a mesh.</summary>
        internal bool TryTakeDirty(out ChunkCoord coord)
        {
            while (_dirtyQueue.Count > 0)
            {
                coord = _dirtyQueue.Dequeue();
                if (_dirtySet.Remove(coord)) // False for chunks unloaded while they were queued.
                    return true;
            }
            coord = default;
            return false;
        }

        public void Dispose()
        {
            foreach (VoxelChunk chunk in _chunks.Values)
                chunk.Dispose();
            _chunks.Clear();
            _dirtyQueue.Clear();
            _dirtySet.Clear();
            if (_blocks.IsCreated)
                _blocks.Dispose();
        }
    }
}
