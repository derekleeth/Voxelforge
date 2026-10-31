using Unity.Collections;
using Unity.Mathematics;
using static Voxelforge.Voxels.VoxelConstants;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// Read-only questions about the <see cref="VoxelNavGrid"/>: is this block solid, can a body stand here. Burst-safe, so
    /// path jobs and the main thread ask exactly the same way.
    /// <para>
    /// Each baked chunk is 1,024 rows of 32 bits, one bit per block, 1 = solid. A row is one (y, z) line of blocks along x,
    /// in the same order as the chunk's own array. A chunk that is all air or all solid keeps no bits at all.
    /// A chunk that isn't loaded, or isn't baked yet, is solid, just as <see cref="VoxelBodyMotor.IsSolid"/> says.
    /// </para>
    /// </summary>
    public readonly struct NavGridView
    {
        /// <summary>32-bit rows in one chunk's bits.</summary>
        public const int RowsPerChunk = ChunkArea;

        /// <summary>A chunk slot meaning "no bits, every block is open".</summary>
        public const int OpenChunk = -1;

        /// <summary>A chunk slot meaning "no bits, every block is solid".</summary>
        public const int SolidChunk = -2;

        readonly NativeHashMap<int3, int> _slots;
        readonly NativeArray<uint> _bits;

        public NavGridView(NativeHashMap<int3, int> slots, NativeArray<uint> bits)
        {
            _slots = slots;
            _bits = bits;
        }

        /// <summary>Which of a chunk's 1,024 rows holds <paramref name="local"/>.</summary>
        public static int RowOf(int3 local) => (local.y << ChunkShift) | local.z;

        /// <summary>Blocks movement: a solid block, or any block in a chunk that isn't loaded and baked.</summary>
        public bool IsSolid(int3 block)
        {
            if (!_slots.TryGetValue(block >> ChunkShift, out int slot))
                return true;
            if (slot == OpenChunk)
                return false;
            if (slot == SolidChunk)
                return true;
            int3 local = block & ChunkMask;
            return ((_bits[slot * RowsPerChunk + RowOf(local)] >> local.x) & 1u) != 0;
        }

        /// <summary>The <paramref name="height"/> blocks from <paramref name="feet"/> upward are all open.</summary>
        public bool IsClear(int3 feet, int height)
        {
            for (int i = 0; i < height; i++)
            {
                if (IsSolid(feet + new int3(0, i, 0)))
                    return false;
            }
            return true;
        }

        /// <summary>A body <paramref name="height"/> blocks tall fits at <paramref name="feet"/>, on a solid block.</summary>
        public bool IsStandable(int3 feet, int height) => IsSolid(feet - new int3(0, 1, 0)) && IsClear(feet, height);
    }
}
