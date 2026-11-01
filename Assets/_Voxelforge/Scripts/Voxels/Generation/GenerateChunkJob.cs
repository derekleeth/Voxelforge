using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using static Voxelforge.Voxels.VoxelConstants;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// Builds one chunk of terrain with <see cref="TerrainSampler"/>, then stamps in any ruins and arenas that reach it
    /// (Roadmap 5.4, <see cref="StampPlacement"/>). Top-level (not nested) so Burst registers it.
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

        // The world's stamps (Roadmap 5.4), as top-level fields: job containers nested inside another struct trip up the
        // job system. Set them all with WithStamps; the arrays must be created, even if empty.
        [ReadOnly] public NativeArray<StampInfo> StampInfos;
        [ReadOnly] public NativeArray<ushort> StampBlocks;
        public int2 StampKeepClearCenter;
        public int StampKeepClearRadius;

        /// <summary><see cref="ChunkVolume"/> voxels. When <see cref="IsUniform"/> is set, only element 0 is filled in.</summary>
        [WriteOnly] public NativeArray<Voxel> Voxels;
        [WriteOnly] public NativeReference<bool> IsUniform;

        /// <summary>This job with <paramref name="stamps"/> to stamp in.</summary>
        public GenerateChunkJob WithStamps(in StampTable stamps)
        {
            GenerateChunkJob job = this;
            job.StampInfos = stamps.Infos;
            job.StampBlocks = stamps.Blocks;
            job.StampKeepClearCenter = stamps.KeepClearCenter;
            job.StampKeepClearRadius = stamps.KeepClearRadius;
            return job;
        }

        public void Execute()
        {
            var stamps = new StampTable
            {
                Infos = StampInfos, Blocks = StampBlocks, KeepClearCenter = StampKeepClearCenter,
                KeepClearRadius = StampKeepClearRadius,
            };
            var sampler = new TerrainSampler(Seed, Terrain, Biomes, Allocator.Temp);
            var columns = new NativeArray<TerrainColumn>(ChunkArea, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var sites = new NativeList<StampSite>(4, Allocator.Temp);
            if (stamps.Count > 0)
                StampPlacement.SitesTouching(stamps, ref sampler, StampPlacement.SeedHash(Seed), Origin, ChunkSize, sites);

            int top = int.MinValue;
            for (int z = 0; z < ChunkSize; z++)
            for (int x = 0; x < ChunkSize; x++)
            {
                TerrainColumn column = sampler.SampleColumn(Origin.x + x, Origin.z + z);
                // No tree grows through a stamp (or right against its walls).
                if (sites.Length > 0 && StampPlacement.InFootprint(sites, Origin.x + x, Origin.z + z, 1))
                    column.TreeHeight = 0;
                columns[z * ChunkSize + x] = column;
                top = math.max(top, column.Height + column.TreeHeight);
            }
            for (int s = 0; s < sites.Length; s++)
                top = math.max(top, sites[s].Max.y - 1);

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
                // Voxels is write-only here, so a stamped chunk is simply kept whole: it's almost never one block.
                if (sites.Length > 0 && StampPlacement.Apply(stamps, sites, Origin, ChunkSize, columns, Voxels))
                    uniform = false;
                IsUniform.Value = uniform;
            }

            sites.Dispose();
            columns.Dispose();
            sampler.Dispose();
        }
    }
}
