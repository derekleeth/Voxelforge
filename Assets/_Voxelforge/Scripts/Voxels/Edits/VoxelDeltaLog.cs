using System.Collections.Generic;
using Unity.Mathematics;
using Voxelforge.Core;

namespace Voxelforge.Voxels
{
    /// <summary>One block that differs from what the world generator made there.</summary>
    public readonly struct VoxelDelta
    {
        /// <summary>The block there now.</summary>
        public readonly Voxel Current;
        /// <summary>The block the generator made. Putting it back removes the delta.</summary>
        public readonly Voxel Generated;
        /// <summary>The last player who changed this block.</summary>
        public readonly PlayerId Instigator;

        public VoxelDelta(Voxel current, Voxel generated, PlayerId instigator)
        {
            Current = current;
            Generated = generated;
            Instigator = instigator;
        }
    }

    /// <summary>
    /// Every block players have changed, grouped by chunk (Roadmap 3.1, D-014). The world is its
    /// <see cref="WorldSeed"/> plus this log: chunks that generate again get their deltas put back, saves (3.4) write
    /// it out, and joining players (6.2) receive it.
    /// <para>
    /// It holds the <b>net</b> change, not a history. Editing a block twice keeps one entry, and changing a block back
    /// to what was generated deletes it, so the log only grows with blocks that really differ.
    /// </para>
    /// </summary>
    public sealed class VoxelDeltaLog
    {
        static readonly IReadOnlyDictionary<int, VoxelDelta> NoDeltas = new Dictionary<int, VoxelDelta>();

        // Chunk -> (local index -> delta). Index order is VoxelMath.ToIndex.
        readonly Dictionary<ChunkCoord, Dictionary<int, VoxelDelta>> _chunks = new Dictionary<ChunkCoord, Dictionary<int, VoxelDelta>>();

        /// <summary>Chunks with at least one changed block.</summary>
        public int ChunkCount => _chunks.Count;

        /// <summary>Changed blocks across every chunk.</summary>
        public int BlockCount { get; private set; }

        public IEnumerable<ChunkCoord> Chunks => _chunks.Keys;

        /// <summary>
        /// Notes that the block at <paramref name="world"/> went from <paramref name="before"/> to <paramref name="after"/>.
        /// The first change of a block remembers <paramref name="before"/> as the generated block.
        /// </summary>
        public void Record(int3 world, Voxel before, Voxel after, PlayerId instigator)
        {
            ChunkCoord coord = VoxelMath.WorldToChunk(world);
            int index = VoxelMath.ToIndex(VoxelMath.WorldToLocal(world));

            if (!_chunks.TryGetValue(coord, out Dictionary<int, VoxelDelta> deltas))
            {
                if (before == after)
                    return;
                deltas = new Dictionary<int, VoxelDelta>();
                _chunks.Add(coord, deltas);
            }

            Voxel generated = deltas.TryGetValue(index, out VoxelDelta existing) ? existing.Generated : before;
            bool hadEntry = deltas.ContainsKey(index);
            if (after == generated)
            {
                if (hadEntry)
                {
                    deltas.Remove(index);
                    BlockCount--;
                }
                if (deltas.Count == 0)
                    _chunks.Remove(coord);
                return;
            }

            deltas[index] = new VoxelDelta(after, generated, instigator);
            if (!hadEntry)
                BlockCount++;
        }

        public bool TryGet(int3 world, out VoxelDelta delta)
        {
            delta = default;
            return _chunks.TryGetValue(VoxelMath.WorldToChunk(world), out Dictionary<int, VoxelDelta> deltas) &&
                   deltas.TryGetValue(VoxelMath.ToIndex(VoxelMath.WorldToLocal(world)), out delta);
        }

        /// <summary>One chunk's changed blocks, keyed by local index (<see cref="VoxelMath.ToIndex(int3)"/>). Empty if none.</summary>
        public IReadOnlyDictionary<int, VoxelDelta> GetChunkDeltas(ChunkCoord coord) =>
            _chunks.TryGetValue(coord, out Dictionary<int, VoxelDelta> deltas) ? deltas : NoDeltas;

        public bool HasDeltas(ChunkCoord coord) => _chunks.ContainsKey(coord);

        public void Clear()
        {
            _chunks.Clear();
            BlockCount = 0;
        }
    }
}
