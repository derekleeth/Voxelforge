namespace Voxelforge.Voxels
{
    /// <summary>What happened to a <see cref="VoxelWorldEditor.RequestModifyVoxel"/> request.</summary>
    public enum VoxelEditResult : byte
    {
        /// <summary>The block changed, was logged, and its chunks are queued to remesh.</summary>
        Applied,
        /// <summary>The block was already that type. Nothing changed.</summary>
        Unchanged,
        /// <summary>The chunk isn't loaded (or is above or below the world), so we can't know what was there.</summary>
        ChunkNotLoaded,
        /// <summary>No registered block has that ID.</summary>
        UnknownBlock,
        /// <summary>Every edit needs a player to blame (CLAUDE.md Law #1).</summary>
        NoInstigator,
    }
}
