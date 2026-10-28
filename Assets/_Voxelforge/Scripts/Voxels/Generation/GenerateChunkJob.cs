using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using static Voxelforge.Voxels.VoxelConstants;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// Builds one chunk of terrain with <see cref="TerrainSampler"/>. Top-level (not nested) so Burst registers it.
    /// <para>
    /// Strict float mode stops Burst from reordering math, so the same seed gives the same blocks every time.
    /// Compiled synchronously so the editor never runs a slower (and possibly slightly different) managed copy.
    /// </para>
    /// </summary>
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true)]
    public struct GenerateChunkJob : IJob
    {
        public int3 Origin;
        public WorldSeed Seed;
        public TerrainParams Terrain;
        [ReadOnly] public NativeArray<BiomeInfo> Biomes;

        /// <summary><see cref="ChunkVolume"/> voxels. When <see cref="IsUniform"/> is set, only element 0 is filled in.</summary>
        [WriteOnly] public NativeArray<Voxel> Voxels;
        [WriteOnly] public NativeReference<bool> IsUniform;

        public void Execute()
        {
            var sampler = new TerrainSampler(Seed, Terrain, Biomes, Allocator.Temp);
            var columns = new NativeArray<TerrainColumn>(ChunkArea, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            int top = int.MinValue;
            for (int z = 0; z < ChunkSize; z++)
            for (int x = 0; x < ChunkSize; x++)
            {
                TerrainColumn column = sampler.SampleColumn(Origin.x + x, Origin.z + z);
                columns[z * ChunkSize + x] = column;
                top = math.max(top, column.Height + column.TreeHeight);
            }

            // Sky chunk: nothing to carve, so skip the 3D noise entirely.
            if (Origin.y > top)
            {
                Voxels[0] = Voxel.Air;
                IsUniform.Value = true;
            }
            else
            {
                ushort first = 0;
                bool uniform = true;
                for (int i = 0; i < ChunkVolume; i++)
                {
                    int3 local = VoxelMath.FromIndex(i);
                    ushort block = sampler.BlockAt(Origin + local, columns[local.z * ChunkSize + local.x]);
                    Voxels[i] = new Voxel(block);
                    if (i == 0)
                        first = block;
                    else
                        uniform &= block == first;
                }
                IsUniform.Value = uniform;
            }

            columns.Dispose();
            sampler.Dispose();
        }
    }
}
