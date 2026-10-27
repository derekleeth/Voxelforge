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
        /// <summary>Placeholder color used until block textures arrive (Roadmap 1.5).</summary>
        public Color32 MapColor;

        public bool IsRegistered => (Flags & BlockFlags.Registered) != 0;
        public bool IsSolid => (Flags & BlockFlags.Solid) != 0;
        public bool IsTransparent => (Flags & BlockFlags.Transparent) != 0;
    }
}
