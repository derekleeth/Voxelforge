using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// One corner of a chunk mesh quad (40 bytes). Every vertex sits in a single interleaved stream.
    /// <list type="bullet">
    /// <item><c>Position</c>: chunk-local, 0-32 on each axis.</item>
    /// <item><c>Normal</c>: which way the face points.</item>
    /// <item><c>Color</c>: the block's placeholder <see cref="BlockInfo.MapColor"/>.</item>
    /// <item><c>UV</c>: measured in blocks, so a 5x3 merged quad runs 0-5 by 0-3 and textures repeat once per block.</item>
    /// <item><c>BlockId</c>: lets the Roadmap 1.5 shader pick the right texture.</item>
    /// </list>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ChunkVertex
    {
        public float3 Position;
        public float3 Normal;
        public Color32 Color;
        public float2 UV;
        public float BlockId;

        public const int AttributeCount = 5;

        /// <summary>
        /// Describes this struct's layout to the GPU. Field order must match the struct above.
        /// Burst-safe, so jobs can call it with <see cref="Allocator.Temp"/>.
        /// </summary>
        public static NativeArray<VertexAttributeDescriptor> CreateLayout(Allocator allocator)
        {
            var layout = new NativeArray<VertexAttributeDescriptor>(AttributeCount, allocator, NativeArrayOptions.UninitializedMemory);
            layout[0] = new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3);
            layout[1] = new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3);
            layout[2] = new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4);
            layout[3] = new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2);
            layout[4] = new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 1);
            return layout;
        }
    }
}
