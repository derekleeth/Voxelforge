namespace Voxelforge.Voxels
{
    /// <summary>What a <see cref="VoxelWorldEditor.RequestDamageVoxels"/> batch did, block by block.</summary>
    public struct VoxelBatchResult
    {
        /// <summary>Blocks that broke (turned to air). A tree trunk or torch that went with one isn't counted.</summary>
        public int Broken;
        /// <summary>Blocks that took damage but still stand.</summary>
        public int Damaged;
        /// <summary>Requests that did nothing: air, unbreakable or blast-proof blocks, unloaded chunks, no damage.</summary>
        public int Skipped;
    }
}
