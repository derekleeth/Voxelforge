using System;
using UnityEngine;

namespace Voxelforge.Voxels
{
    [Flags]
    public enum BlockFlags : byte
    {
        None = 0,
        /// <summary>Set for every ID that has a definition. Unused IDs in the lookup are all zeros.</summary>
        Registered = 1 << 0,
        /// <summary>Blocks movement and has collision.</summary>
        Solid = 1 << 1,
        /// <summary>Light and neighbouring faces show through (glass, leaves, air).</summary>
        Transparent = 1 << 2,
    }

    /// <summary>
    /// Job-friendly copy of a <see cref="BlockDefinition"/>. <see cref="BlockRegistry.CreateLookup"/>
    /// builds a <c>NativeArray&lt;BlockInfo&gt;</c> indexed by block ID.
    /// </summary>
    public struct BlockInfo
    {
        public ushort Id;
        /// <summary>Total mining damage needed to break this block. 0 means it can't be broken.</summary>
        public ushort MaxDurability;
        /// <summary>Light this block gives off, 0-15.</summary>
        public byte LightEmission;
        public BlockFlags Flags;
        /// <summary>Biome colour for the green parts of this block's faces (Roadmap 2.6).</summary>
        public BiomeTint Tint;
        /// <summary>Flat color, shown on faces with no texture.</summary>
        public Color32 MapColor;

        /// <summary>Texture array layers for each face (see <see cref="BlockRegistry.GetTextureLayers"/>), or <see cref="NoTexture"/>.</summary>
        public ushort TopLayer;
        public ushort SideLayer;
        public ushort BottomLayer;

        /// <summary>Layer value for a face with no texture. The shader draws <see cref="MapColor"/> instead.</summary>
        public const ushort NoTexture = ushort.MaxValue;

        public bool IsRegistered => (Flags & BlockFlags.Registered) != 0;
        public bool IsSolid => (Flags & BlockFlags.Solid) != 0;
        public bool IsTransparent => (Flags & BlockFlags.Transparent) != 0;

        /// <summary>
        /// A registered block you can't see through. The mesher draws these and hides any face
        /// that touches one. Air, glass and unused IDs are not opaque.
        /// </summary>
        public bool IsOpaque => (Flags & (BlockFlags.Registered | BlockFlags.Transparent)) == BlockFlags.Registered;

        /// <summary>The texture layer drawn on a face pointing <paramref name="facing"/>.</summary>
        public ushort LayerFor(BlockFacing facing)
        {
            switch (facing)
            {
                case BlockFacing.Up:
                    return TopLayer;
                case BlockFacing.Down:
                    return BottomLayer;
                default:
                    return SideLayer;
            }
        }
    }
}
