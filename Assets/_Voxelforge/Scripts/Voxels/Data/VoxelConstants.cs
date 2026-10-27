namespace Voxelforge.Voxels
{
    /// <summary>
    /// Chunk dimensions. Chunks are 32 blocks on each side so one row of blocks fits in a
    /// 32-bit integer, which the binary greedy mesher (Roadmap 1.3) relies on.
    /// </summary>
    public static class VoxelConstants
    {
        /// <summary>Blocks along each edge of a chunk.</summary>
        public const int ChunkSize = 32;

        /// <summary>log2(ChunkSize). Shifting by this converts between world and chunk coordinates.</summary>
        public const int ChunkShift = 5;

        /// <summary>ChunkSize - 1. AND-ing with this gives the local coordinate inside a chunk.</summary>
        public const int ChunkMask = ChunkSize - 1;

        /// <summary>Blocks in one horizontal layer of a chunk.</summary>
        public const int ChunkArea = ChunkSize * ChunkSize;

        /// <summary>Blocks in a whole chunk (32,768).</summary>
        public const int ChunkVolume = ChunkArea * ChunkSize;
    }
}
