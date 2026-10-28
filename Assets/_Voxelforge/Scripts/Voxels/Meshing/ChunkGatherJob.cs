using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using static Voxelforge.Voxels.VoxelConstants;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// Where the gather job reads one chunk from. A chunk either has a voxel array, or is uniform
    /// (one block everywhere, no array). A chunk that isn't loaded counts as uniform Air.
    /// </summary>
    public unsafe struct GatherSource
    {
        /// <summary>The chunk's voxels, or null when the chunk is uniform or not loaded.</summary>
        [NativeDisableUnsafePtrRestriction] public Voxel* Voxels;
        public ushort UniformId;

        public static GatherSource Uniform(ushort id) => new GatherSource { UniformId = id };

        public static GatherSource Of(NativeArray<Voxel>.ReadOnly voxels) =>
            new GatherSource { Voxels = (Voxel*)voxels.GetUnsafeReadOnlyPtr() };
    }

    /// <summary>
    /// Copies the block IDs of a chunk, plus a one-block border taken from all 26 neighbours (faces, edges and
    /// corners), into a padded 34x34x34 buffer. The mesher then reads only this buffer, so it never touches
    /// <see cref="VoxelChunk"/> memory. That means chunks can be edited again as soon as this
    /// short job finishes, even if meshing is still running.
    /// <para>
    /// Face culling only needs the 6 face neighbours; corner ambient occlusion (Roadmap 2.6) also looks diagonally,
    /// so the edges and corners of the padding come from the diagonal chunks.
    /// </para>
    /// <para>
    /// The sources are read through pointers, because a job can't hold 27 NativeArrays. The scheduler keeps the
    /// usual promise instead: no chunk is edited or freed while a gather job may read it
    /// (<see cref="ChunkMeshScheduler.CompleteGatherJobs"/>).
    /// </para>
    /// </summary>
    [BurstCompile]
    public struct ChunkGatherJob : IJob
    {
        /// <summary>Blocks along each edge of the padded buffer: the chunk plus one on each side.</summary>
        public const int PaddedSize = ChunkSize + 2;
        public const int PaddedVolume = PaddedSize * PaddedSize * PaddedSize;

        /// <summary>The chunk and its neighbours, see <see cref="SourceIndex"/>. The chunk itself is <see cref="CenterSource"/>.</summary>
        public const int SourceCount = 27;
        public const int CenterSource = 13;

        /// <summary>27 sources, indexed by <see cref="SourceIndex"/>.</summary>
        [ReadOnly] public NativeArray<GatherSource> Sources;

        /// <summary>Output: <see cref="PaddedVolume"/> block IDs, indexed with <see cref="PaddedIndex"/>.</summary>
        [WriteOnly] public NativeArray<ushort> Padded;

        /// <summary>
        /// Index into the padded buffer. Takes padded coordinates (local + 1), so 0 and 33 are the
        /// neighbour border. Same X-fastest, then Z, then Y order as <see cref="VoxelMath.ToIndex(int, int, int)"/>.
        /// </summary>
        public static int PaddedIndex(int x, int y, int z) => x + z * PaddedSize + y * PaddedSize * PaddedSize;

        /// <summary>The source slot for the chunk at <paramref name="offset"/> (each axis -1, 0 or +1) from the centre.</summary>
        public static int SourceIndex(int3 offset) => (offset.x + 1) + (offset.y + 1) * 3 + (offset.z + 1) * 9;

        /// <summary>The chunk offset of a source slot. The inverse of <see cref="SourceIndex"/>.</summary>
        public static int3 SourceOffset(int index) => new int3(index % 3, index / 3 % 3, index / 9) - 1;

        public void Execute()
        {
            for (int py = 0; py < PaddedSize; py++)
            for (int pz = 0; pz < PaddedSize; pz++)
            {
                Split(py, out int sy, out int ly);
                Split(pz, out int sz, out int lz);
                int row = PaddedIndex(0, py, pz);
                int sourceRow = sy * 3 + sz * 9;

                // The two border blocks of this row, then the 32 in between.
                Padded[row] = Read(Sources[sourceRow], VoxelMath.ToIndex(ChunkMask, ly, lz));
                Padded[row + PaddedSize - 1] = Read(Sources[sourceRow + 2], VoxelMath.ToIndex(0, ly, lz));

                GatherSource middle = Sources[sourceRow + 1];
                int src = VoxelMath.ToIndex(0, ly, lz);
                for (int x = 0; x < ChunkSize; x++)
                    Padded[row + 1 + x] = Read(middle, src + x);
            }
        }

        /// <summary>Which source along one axis (0, 1, 2) and which local coordinate a padded coordinate reads.</summary>
        static void Split(int padded, out int source, out int local)
        {
            if (padded == 0)
            {
                source = 0;
                local = ChunkMask;
            }
            else if (padded == PaddedSize - 1)
            {
                source = 2;
                local = 0;
            }
            else
            {
                source = 1;
                local = padded - 1;
            }
        }

        static unsafe ushort Read(in GatherSource source, int index)
        {
            return source.Voxels != null ? source.Voxels[index].BlockId : source.UniformId;
        }
    }
}
