using Unity.Mathematics;
using UnityEngine;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// Which biome colour tints a block or decoration (Roadmap 2.6). Each biome has a grass and a foliage colour,
    /// and the shaders multiply them into the green parts of the texture only, so dirt, bark and petals keep
    /// their own colour.
    /// </summary>
    public enum BiomeTint : byte
    {
        None,
        Grass,
        Foliage,
    }

    public static class BiomeTints
    {
        /// <summary>Packs a 0-1 colour into RGBA8 (R in the low byte), as <see cref="DecorationInstance.Tint"/> stores it.</summary>
        public static uint Pack(float3 colour)
        {
            uint3 c = (uint3)math.round(math.saturate(colour) * 255f);
            return c.x | (c.y << 8) | (c.z << 16) | (255u << 24);
        }

        public static Color32 ToColor32(float3 colour, byte alpha)
        {
            uint3 c = (uint3)math.round(math.saturate(colour) * 255f);
            return new Color32((byte)c.x, (byte)c.y, (byte)c.z, alpha);
        }
    }
}
