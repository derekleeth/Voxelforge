using System;
using Unity.Collections;
using Unity.Mathematics;
using static Voxelforge.Voxels.VoxelConstants;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// Biome colours at the block corners of one chunk, for <see cref="GreedyMesher"/> (Roadmap 2.6). Each of the
    /// 33 x 33 corner columns is worked out the first time a tinted face needs it, so chunks with no grass pay
    /// nothing. Colours depend only on (x, z), so neighbouring chunks agree along their shared edges.
    /// </summary>
    public struct ChunkTints : IDisposable
    {
        const int Size = ChunkSize + 1;

        TerrainSampler _sampler;
        int2 _origin;
        NativeArray<float3> _grass;
        NativeArray<float3> _foliage;
        NativeArray<byte> _known; // Bit 0: grass known, bit 1: foliage known.
        bool _enabled;

        /// <summary>No tint: every block is white.</summary>
        public static ChunkTints None => default;

        /// <param name="sampler">Borrowed, not disposed here.</param>
        /// <param name="origin">World position of the chunk's corner.</param>
        public ChunkTints(TerrainSampler sampler, int3 origin, Allocator allocator)
        {
            _sampler = sampler;
            _origin = origin.xz;
            _grass = new NativeArray<float3>(Size * Size, allocator, NativeArrayOptions.UninitializedMemory);
            _foliage = new NativeArray<float3>(Size * Size, allocator, NativeArrayOptions.UninitializedMemory);
            _known = new NativeArray<byte>(Size * Size, allocator);
            _enabled = true;
        }

        /// <summary>The colour of <paramref name="tint"/> at the corner (<paramref name="x"/>, <paramref name="z"/>), each 0-32.</summary>
        public float3 At(int x, int z, BiomeTint tint)
        {
            if (!_enabled || tint == BiomeTint.None)
                return 1f;

            int index = x + z * Size;
            byte bit = tint == BiomeTint.Grass ? (byte)1 : (byte)2;
            NativeArray<float3> cache = tint == BiomeTint.Grass ? _grass : _foliage;
            if ((_known[index] & bit) == 0)
            {
                cache[index] = _sampler.TintAt(new float2(_origin.x + x, _origin.y + z), tint);
                _known[index] = (byte)(_known[index] | bit);
            }
            return cache[index];
        }

        public void Dispose()
        {
            if (!_enabled)
                return;
            _grass.Dispose();
            _foliage.Dispose();
            _known.Dispose();
        }
    }
}
