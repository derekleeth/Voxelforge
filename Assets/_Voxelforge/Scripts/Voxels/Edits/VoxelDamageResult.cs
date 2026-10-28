namespace Voxelforge.Voxels
{
    /// <summary>What happened to a <see cref="VoxelWorldEditor.RequestDamageVoxel"/> request.</summary>
    public enum VoxelDamageResult : byte
    {
        /// <summary>The block took damage but still stands.</summary>
        Damaged,
        /// <summary>The damage reached the block's durability, so it was replaced with air through <see cref="VoxelWorldEditor.RequestModifyVoxel"/>.</summary>
        Broken,
        /// <summary>The block's durability is 0, so it can't be mined.</summary>
        Unbreakable,
        /// <summary>There's no solid block there to mine (air).</summary>
        NotSolid,
        /// <summary>The damage wasn't a positive amount. Nothing changed.</summary>
        NoDamage,
        /// <summary>The chunk isn't loaded (or is above or below the world).</summary>
        ChunkNotLoaded,
        /// <summary>Every edit needs a player to blame (CLAUDE.md Law #1).</summary>
        NoInstigator,
    }
}
