using Unity.Mathematics;

namespace Voxelforge.Voxels
{
    /// <summary>One block something wants to damage, and by how much (<see cref="VoxelWorldEditor.RequestDamageVoxels"/>).</summary>
    public struct VoxelDamageRequest
    {
        public int3 Block;
        public float Amount;
    }
}
