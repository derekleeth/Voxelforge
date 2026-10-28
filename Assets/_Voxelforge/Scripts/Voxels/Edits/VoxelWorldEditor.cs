using System;
using Unity.Mathematics;
using Voxelforge.Core;

namespace Voxelforge.Voxels
{
    /// <summary>One block that changed, and who changed it.</summary>
    public readonly struct VoxelEdit
    {
        public readonly int3 Coord;
        public readonly Voxel Before;
        public readonly Voxel After;
        public readonly PlayerId Instigator;

        public VoxelEdit(int3 coord, Voxel before, Voxel after, PlayerId instigator)
        {
            Coord = coord;
            Before = before;
            After = after;
            Instigator = instigator;
        }
    }

    /// <summary>
    /// The only way blocks change after generation (CLAUDE.md Law #3, Roadmap 3.1, D-014). It checks a request, sets
    /// the block, records it in the <see cref="VoxelDeltaLog"/> and queues the remeshing. Plain C# so tests can drive
    /// it; <see cref="VoxelWorldManager.RequestModifyVoxel"/> is the scene's entry point to it.
    /// <para>
    /// <b>Host only.</b> Clients ask the host through a server RPC, and the host calls this (CLAUDE.md Law #2).
    /// </para>
    /// </summary>
    public sealed class VoxelWorldEditor
    {
        /// <summary>Tallest trunk a chop clears. Trees are 4-6 blocks (D-012); this is just a safety stop.</summary>
        const int MaxTrunkHeight = 32;

        readonly VoxelWorld _world;
        readonly ChunkMeshScheduler _meshes;

        /// <param name="meshes">Optional: its gather jobs are finished before a chunk is edited.</param>
        public VoxelWorldEditor(VoxelWorld world, VoxelDeltaLog deltaLog, ChunkMeshScheduler meshes)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            DeltaLog = deltaLog ?? throw new ArgumentNullException(nameof(deltaLog));
            _meshes = meshes;
        }

        public VoxelDeltaLog DeltaLog { get; }

        /// <summary>How far blocks have been mined (Roadmap 3.2). A block's entry goes when it changes.</summary>
        public BlockDamageMap Damage { get; } = new BlockDamageMap();

        /// <summary>Raised once for every block that changes, including the rest of a chopped tree trunk.</summary>
        public event Action<VoxelEdit> EditApplied;

        /// <summary>
        /// Mines the block at <paramref name="coord"/> by <paramref name="amount"/> on behalf of
        /// <paramref name="instigator"/>. Once its damage reaches <see cref="BlockInfo.MaxDurability"/> it breaks:
        /// it's set to air through <see cref="RequestModifyVoxel"/>, so it's logged and a tree trunk fells its tree.
        /// </summary>
        /// <param name="time">Now, in game seconds. Blocks left alone heal (<see cref="BlockDamageMap.HealIdle"/>).</param>
        public VoxelDamageResult RequestDamageVoxel(int3 coord, float amount, PlayerId instigator, float time)
        {
            if (!instigator.IsValid)
                return VoxelDamageResult.NoInstigator;
            if (!(amount > 0f))
                return VoxelDamageResult.NoDamage;
            if (!_world.TryGetChunk(VoxelMath.WorldToChunk(coord), out _))
                return VoxelDamageResult.ChunkNotLoaded;

            BlockInfo block = _world.Blocks[_world.GetVoxel(coord).BlockId];
            if (!block.IsSolid)
                return VoxelDamageResult.NotSolid;
            if (block.MaxDurability == 0)
                return VoxelDamageResult.Unbreakable;

            if (Damage.Add(coord, amount, time) < block.MaxDurability)
                return VoxelDamageResult.Damaged;

            RequestModifyVoxel(coord, BlockIds.Air, instigator); // Its Apply forgets the damage.
            return VoxelDamageResult.Broken;
        }

        /// <summary>
        /// Sets the block at <paramref name="coord"/> to <paramref name="blockId"/> on behalf of <paramref name="instigator"/>.
        /// Removing a <see cref="BlockIds.TreeTrunk"/> block clears the rest of its trunk too, so the tree falls.
        /// </summary>
        public VoxelEditResult RequestModifyVoxel(int3 coord, ushort blockId, PlayerId instigator)
        {
            if (!instigator.IsValid)
                return VoxelEditResult.NoInstigator;
            if (blockId >= _world.Blocks.Length || !_world.Blocks[blockId].IsRegistered)
                return VoxelEditResult.UnknownBlock;
            if (!_world.TryGetChunk(VoxelMath.WorldToChunk(coord), out _))
                return VoxelEditResult.ChunkNotLoaded;

            Voxel before = _world.GetVoxel(coord);
            var after = new Voxel(blockId);
            if (before == after)
                return VoxelEditResult.Unchanged;

            _meshes?.CompleteGatherJobs(); // A gather job may be reading the chunk we're about to change.
            Apply(coord, before, after, instigator);

            if (before.BlockId == BlockIds.TreeTrunk && blockId != BlockIds.TreeTrunk)
            {
                ClearTrunk(coord, new int3(0, 1, 0), instigator);
                ClearTrunk(coord, new int3(0, -1, 0), instigator);
            }
            return VoxelEditResult.Applied;
        }

        void ClearTrunk(int3 from, int3 step, PlayerId instigator)
        {
            int3 coord = from + step;
            for (int i = 0; i < MaxTrunkHeight; i++, coord += step)
            {
                if (!_world.TryGetChunk(VoxelMath.WorldToChunk(coord), out _))
                    return;
                Voxel before = _world.GetVoxel(coord);
                if (before.BlockId != BlockIds.TreeTrunk)
                    return;
                Apply(coord, before, Voxel.Air, instigator);
            }
        }

        void Apply(int3 coord, Voxel before, Voxel after, PlayerId instigator)
        {
            _world.SetVoxel(coord, after);
            DeltaLog.Record(coord, before, after, instigator);
            Damage.Remove(coord);
            EditApplied?.Invoke(new VoxelEdit(coord, before, after, instigator));
        }

        /// <summary>
        /// Puts a freshly generated chunk's logged edits back. Called by the <see cref="ChunkGenerationScheduler"/>
        /// before anyone can see the chunk, so it isn't an edit: nothing is logged or raised.
        /// </summary>
        /// <returns>
        /// How many deltas expected a different generated block than the one there. Above 0 means the terrain
        /// generator changed since the save was made (Roadmap 3.4); the edits are put back anyway.
        /// </returns>
        internal static int ReapplyDeltas(VoxelChunk chunk, VoxelDeltaLog deltaLog)
        {
            int mismatches = 0;
            foreach (var pair in deltaLog.GetChunkDeltas(chunk.Coord))
            {
                Voxel there = chunk.GetVoxel(pair.Key);
                Voxel generated = pair.Value.Generated;
                if (there.BlockId != generated.BlockId || there.State != generated.State)
                    mismatches++;
                chunk.SetVoxel(pair.Key, pair.Value.Current);
            }
            return mismatches;
        }
    }
}
