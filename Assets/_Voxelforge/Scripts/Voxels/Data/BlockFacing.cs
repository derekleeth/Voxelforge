namespace Voxelforge.Voxels
{
    /// <summary>
    /// Which way a directional block (stairs, logs, furnaces) points.
    /// Stored in the low 3 bits of <see cref="Voxel.State"/>.
    /// </summary>
    public enum BlockFacing : byte
    {
        North = 0, // +Z
        South = 1, // -Z
        East = 2,  // +X
        West = 3,  // -X
        Up = 4,    // +Y
        Down = 5,  // -Y
    }
}
