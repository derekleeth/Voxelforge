using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// Job-friendly copy of a <see cref="DecorationDefinition"/>. <see cref="DecorationSet.CreateLookups"/> builds a
    /// <c>NativeArray&lt;DecorationInfo&gt;</c> indexed by decoration type (its position in the set).
    /// </summary>
    public struct DecorationInfo
    {
        /// <summary>Surface blocks are stored as bits of a ulong, so they need IDs below this.</summary>
        public const int MaxSurfaceId = 64;

        public DecorationKind Kind;
        public BiomeTint Tint;
        /// <summary>Bit N set = this decoration can grow on block ID N.</summary>
        public ulong SurfaceMask;
        public float Density;
        public float MinScale;
        public float MaxScale;
        public float ModelTrunkHeight;
        /// <summary>Props: the block this model is drawn on.</summary>
        public ushort PropBlock;
        public PropAttach Attach;

        public bool GrowsOn(ushort blockId) => blockId < MaxSurfaceId && (SurfaceMask & (1ul << blockId)) != 0;
    }

    /// <summary>
    /// One placed decoration (32 bytes). The GPU reads these straight from a buffer, so the layout must match
    /// <c>DecorationInstance</c> in <c>Shaders/Decoration.hlsl</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DecorationInstance
    {
        /// <summary>World position of the bottom centre.</summary>
        public float3 Position;
        /// <summary>Turn around +Y, in radians.</summary>
        public float Yaw;
        public float Scale;
        /// <summary>Index into the <see cref="DecorationSet"/>.</summary>
        public uint Type;
        /// <summary>Colour multiplier, packed RGBA8 (R in the low byte). White = untinted.</summary>
        public uint Tint;
        /// <summary>A free random number per instance, e.g. so plants don't all sway in step.</summary>
        public uint Random;

        public const int Stride = 32;
        public const uint White = 0xFFFFFFFFu;
    }
}
