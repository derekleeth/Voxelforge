using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static Voxelforge.Voxels.VoxelConstants;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// <b>Temporary (replaced by Roadmap 1.4 terrain).</b> Fills a box of chunks with rolling hills of grass,
    /// dirt and stone, sprinkles in Mana Crystal Ore, and carves wavy tunnels, so there's something
    /// to test meshing and culling on.
    /// </summary>
    public sealed class SandboxTestPattern : MonoBehaviour
    {
        [SerializeField] VoxelWorldManager worldManager;

        [Tooltip("How many chunks to fill on each axis. Each chunk is 32 blocks.")]
        [SerializeField] Vector3Int sizeInChunks = new Vector3Int(16, 4, 16);

        [Tooltip("How many chunk jobs to run at once while generating.")]
        [SerializeField, Range(1, 256)] int batchSize = 64;

        void Start()
        {
            if (worldManager == null || worldManager.World == null)
            {
                Debug.LogError($"{nameof(SandboxTestPattern)} needs a working {nameof(VoxelWorldManager)}.", this);
                return;
            }

            float start = Time.realtimeSinceStartup;
            Generate(worldManager.World);
            Debug.Log($"[Voxelforge] Test pattern: {worldManager.World.ChunkCount} chunks generated in " +
                      $"{(Time.realtimeSinceStartup - start) * 1000f:F0} ms.", this);
        }

        void Generate(VoxelWorld world)
        {
            var coords = new List<ChunkCoord>();
            for (int y = 0; y < sizeInChunks.y; y++)
            for (int z = 0; z < sizeInChunks.z; z++)
            for (int x = 0; x < sizeInChunks.x; x++)
                coords.Add(new ChunkCoord(x, y, z));

            int batch = math.max(1, batchSize);
            var voxels = new NativeArray<Voxel>[batch];
            var uniform = new NativeReference<bool>[batch];
            var handles = new NativeArray<JobHandle>(batch, Allocator.Temp);
            for (int i = 0; i < batch; i++)
            {
                voxels[i] = new NativeArray<Voxel>(ChunkVolume, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                uniform[i] = new NativeReference<bool>(Allocator.TempJob);
            }

            try
            {
                for (int first = 0; first < coords.Count; first += batch)
                {
                    int count = math.min(batch, coords.Count - first);
                    for (int i = 0; i < count; i++)
                    {
                        handles[i] = new SandboxFillChunkJob
                        {
                            Origin = coords[first + i].WorldOrigin,
                            Voxels = voxels[i],
                            IsUniform = uniform[i],
                        }.Schedule();
                    }
                    JobHandle.CompleteAll(handles.GetSubArray(0, count));

                    for (int i = 0; i < count; i++)
                    {
                        if (uniform[i].Value)
                            world.CreateChunk(coords[first + i], voxels[i][0]);
                        else
                            world.CreateChunk(coords[first + i]).CopyFrom(voxels[i]);
                    }
                }
            }
            finally
            {
                for (int i = 0; i < batch; i++)
                {
                    voxels[i].Dispose();
                    uniform[i].Dispose();
                }
                handles.Dispose();
            }
        }
    }

    /// <summary>Builds one chunk of the test pattern. Top-level (not nested) so Burst registers it as a job.</summary>
    [BurstCompile]
    public struct SandboxFillChunkJob : IJob
    {
        public int3 Origin;
        public NativeArray<Voxel> Voxels;
        public NativeReference<bool> IsUniform;

        public void Execute()
        {
            var heights = new NativeArray<int>(ChunkArea, Allocator.Temp);
            for (int z = 0; z < ChunkSize; z++)
            for (int x = 0; x < ChunkSize; x++)
                heights[z * ChunkSize + x] = Height(Origin.x + x, Origin.z + z);

            for (int i = 0; i < ChunkVolume; i++)
            {
                int3 local = VoxelMath.FromIndex(i);
                Voxels[i] = new Voxel(BlockAt(Origin + local, heights[local.z * ChunkSize + local.x]));
            }

            bool uniform = true;
            for (int i = 1; i < ChunkVolume && uniform; i++)
                uniform = Voxels[i] == Voxels[0];
            IsUniform.Value = uniform;
        }

        static int Height(int x, int z)
        {
            float2 p = new float2(x, z);
            return (int)(60f + noise.snoise(p * 0.012f) * 16f + noise.snoise(p * 0.05f) * 4f);
        }

        static ushort BlockAt(int3 w, int height)
        {
            if (w.y > height)
                return BlockIds.Air;

            bool tunnel = math.sin(w.x * 0.11f) * math.sin(w.y * 0.17f) * math.sin(w.z * 0.13f) > 0.6f;
            if (tunnel && w.y < height - 2)
                return BlockIds.Air;

            if (w.y == height)
                return BlockIds.Grass;
            if (w.y > height - 4)
                return BlockIds.Dirt;
            return math.hash(w) % 61 == 0 ? BlockIds.ManaCrystalOre : BlockIds.Stone;
        }
    }
}
