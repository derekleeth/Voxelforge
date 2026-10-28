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
        /// <summary>
        /// Drawn as a model by the decoration layer instead of as a cube (torches, workbenches, Roadmap 3.5). Aim rays
        /// stop at it even when it isn't solid, so it can be mined. Props should also be transparent.
        /// </summary>
        Prop = 1 << 3,
        /// <summary>Must rest on a solid block below or beside it, and breaks when that block goes (torches).</summary>
        NeedsSupport = 1 << 4,
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
        /// <summary>The block that drops when this one is mined or chopped (Roadmap 3.2).</summary>
        public ushort DropId;
        /// <summary>How many of <see cref="DropId"/> drop. 0 means nothing drops.</summary>
        public byte DropCount;
        /// <summary>Light this block gives off, 0-15.</summary>
        public byte LightEmission;
        public BlockFlags Flags;
        /// <summary>Which tool mines this block fastest (Roadmap 3.5).</summary>
        public HarvestKind Harvest;
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
        public bool IsProp => (Flags & BlockFlags.Prop) != 0;
        public bool NeedsSupport => (Flags & BlockFlags.NeedsSupport) != 0;

        /// <summary>Aim rays stop here, so it can be mined and built against: solid blocks and props.</summary>
        public bool IsTargetable => (Flags & (BlockFlags.Solid | BlockFlags.Prop)) != 0;

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
