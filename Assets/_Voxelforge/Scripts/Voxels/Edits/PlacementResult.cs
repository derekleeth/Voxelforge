namespace Voxelforge.Voxels
{
    /// <summary>Whether a block can be placed, from <see cref="VoxelPlacement.Check"/> (Roadmap 3.3).</summary>
    public enum PlacementResult : byte
    {
        /// <summary>The cell is open and nobody stands in it.</summary>
        Ok,
        /// <summary>The aim started inside a block, so there's no face to build against.</summary>
        NoFace,
        /// <summary>The cell's chunk isn't loaded (or is above or below the world).</summary>
        ChunkNotLoaded,
        /// <summary>Something is already in the cell, including an invisible tree trunk.</summary>
        Occupied,
        /// <summary>The block would overlap a character.</summary>
        BlocksCharacter,
    }
}
