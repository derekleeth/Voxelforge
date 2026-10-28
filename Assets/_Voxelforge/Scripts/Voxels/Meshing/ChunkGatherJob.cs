using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using static Voxelforge.Voxels.VoxelConstants;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// Where the gather job reads one chunk from. A chunk either has a voxel array, or is uniform
    /// (one block everywhere, no array). A chunk that isn't loaded counts as uniform Air.
    /// </summary>
    public struct GatherSource
    {
        public bool HasArray;
        public ushort UniformId;
    }

    /// <summary>
    /// Copies the block IDs of a chunk, plus a one-block border taken from its 6 face neighbours,
    /// into a padded 34x34x34 buffer. The mesher then reads only this buffer, so it never touches
    /// <see cref="VoxelChunk"/> memory. That means chunks can be edited again as soon as this
    /// short job finishes, even if meshing is still running.
    /// <para>
    /// Only the center and the 6 face slabs are written. The padding edges and corners stay zero,
    /// because face culling never looks at them.
    /// </para>
    /// </summary>
    [BurstCompile]
    public struct ChunkGatherJob : IJob
    {
        /// <summary>Blocks along each edge of the padded buffer: the chunk plus one on each side.</summary>
        public const int PaddedSize = ChunkSize + 2;
        public const int PaddedVolume = PaddedSize * PaddedSize * PaddedSize;

        [ReadOnly] public NativeArray<Voxel>.ReadOnly Center;
        [ReadOnly] public NativeArray<Voxel>.ReadOnly NegX;
        [ReadOnly] public NativeArray<Voxel>.ReadOnly PosX;
        [ReadOnly] public NativeArray<Voxel>.ReadOnly NegY;
        [ReadOnly] public NativeArray<Voxel>.ReadOnly PosY;
        [ReadOnly] public NativeArray<Voxel>.ReadOnly NegZ;
        [ReadOnly] public NativeArray<Voxel>.ReadOnly PosZ;

        public GatherSource CenterSource;
        public GatherSource NegXSource;
        public GatherSource PosXSource;
        public GatherSource NegYSource;
        public GatherSource PosYSource;
        public GatherSource NegZSource;
        public GatherSource PosZSource;

        /// <summary>Output: <see cref="PaddedVolume"/> block IDs, indexed with <see cref="PaddedIndex"/>.</summary>
        [WriteOnly] public NativeArray<ushort> Padded;

        /// <summary>
        /// Index into the padded buffer. Takes padded coordinates (local + 1), so 0 and 33 are the
        /// neighbour border. Same X-fastest, then Z, then Y order as <see cref="VoxelMath.ToIndex(int, int, int)"/>.
        /// </summary>
        public static int PaddedIndex(int x, int y, int z) => x + z * PaddedSize + y * PaddedSize * PaddedSize;

        public void Execute()
        {
            for (int y = 0; y < ChunkSize; y++)
            for (int z = 0; z < ChunkSize; z++)
            {
                int dst = PaddedIndex(1, y + 1, z + 1);
                int src = VoxelMath.ToIndex(0, y, z);
                for (int x = 0; x < ChunkSize; x++)
                    Padded[dst + x] = Read(Center, CenterSource, src + x);
            }

            const int last = PaddedSize - 1;
            for (int a = 0; a < ChunkSize; a++)
            for (int b = 0; b < ChunkSize; b++)
            {
                // X neighbours: a = y, b = z.
                Padded[PaddedIndex(0, a + 1, b + 1)] = Read(NegX, NegXSource, VoxelMath.ToIndex(ChunkMask, a, b));
                Padded[PaddedIndex(last, a + 1, b + 1)] = Read(PosX, PosXSource, VoxelMath.ToIndex(0, a, b));
                // Y neighbours: a = x, b = z.
                Padded[PaddedIndex(a + 1, 0, b + 1)] = Read(NegY, NegYSource, VoxelMath.ToIndex(a, ChunkMask, b));
                Padded[PaddedIndex(a + 1, last, b + 1)] = Read(PosY, PosYSource, VoxelMath.ToIndex(a, 0, b));
                // Z neighbours: a = x, b = y.
                Padded[PaddedIndex(a + 1, b + 1, 0)] = Read(NegZ, NegZSource, VoxelMath.ToIndex(a, b, ChunkMask));
                Padded[PaddedIndex(a + 1, b + 1, last)] = Read(PosZ, PosZSource, VoxelMath.ToIndex(a, b, 0));
            }
        }

        static ushort Read(in NativeArray<Voxel>.ReadOnly voxels, in GatherSource source, int index)
        {
            return source.HasArray ? voxels[index].BlockId : source.UniformId;
        }
    }
}
