using Unity.Mathematics;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// What a block that needs support (<see cref="BlockInfo.NeedsSupport"/>, a torch, Roadmap 3.5) rests on: the solid
    /// block below it, or else one beside it. Placement, the editor (which breaks torches whose support goes) and the
    /// <see cref="Decorator"/> (which stands a torch on the floor or hangs it on that wall) all use these rules.
    /// </summary>
    public static class VoxelSupport
    {
        /// <summary>
        /// Where support is looked for, in order of preference: below, then north (+Z), east (+X), south, west.
        /// Bit i of a support mask is <c>Neighbours[i]</c>. Never above: torches don't hang from ceilings.
        /// </summary>
        public static readonly int3[] Neighbours =
        {
            new int3(0, -1, 0), new int3(0, 0, 1), new int3(1, 0, 0), new int3(0, 0, -1), new int3(-1, 0, 0),
        };

        /// <summary>No support at all.</summary>
        public const int None = -1;

        /// <summary>Index into <see cref="Neighbours"/> of the support used: the first bit set, or <see cref="None"/>.</summary>
        public static int Choose(int supportMask) => supportMask == 0 ? None : math.tzcnt(supportMask);

        /// <summary>Which neighbours of <paramref name="cell"/> are solid, as a mask (unloaded chunks count as solid).</summary>
        public static int SupportMask(VoxelWorld world, int3 cell)
        {
            int mask = 0;
            for (int i = 0; i < Neighbours.Length; i++)
            {
                if (VoxelBodyMotor.IsSolid(cell + Neighbours[i], world))
                    mask |= 1 << i;
            }
            return mask;
        }

        public static bool HasSupport(VoxelWorld world, int3 cell) => SupportMask(world, cell) != 0;
    }
}
