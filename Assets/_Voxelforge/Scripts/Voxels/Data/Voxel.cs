using System;
using System.Runtime.InteropServices;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// One block in the world, packed into 4 bytes so chunks stay small and Burst-friendly.
    /// <list type="bullet">
    /// <item><c>BlockId</c> (2 bytes): which block type, see <see cref="BlockIds"/>. 0 is Air.</item>
    /// <item><c>Light</c> (1 byte): high 4 bits = sunlight 0-15, low 4 bits = block light 0-15.</item>
    /// <item><c>State</c> (1 byte): low 3 bits = <see cref="BlockFacing"/>, high 5 bits reserved.</item>
    /// </list>
    /// Mining damage is not stored here. It lives in a small sparse map, since almost no blocks are damaged at once.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct Voxel : IEquatable<Voxel>
    {
        const int LightBits = 4;
        const byte LightMask = 0x0F;
        const byte FacingMask = 0x07;

        public const byte MaxLight = 15;

        public readonly ushort BlockId;
        public readonly byte Light;
        public readonly byte State;

        public Voxel(ushort blockId, byte light = 0, byte state = 0)
        {
            BlockId = blockId;
            Light = light;
            State = state;
        }

        public static Voxel Air => default;

        public bool IsAir => BlockId == BlockIds.Air;

        public byte SunLight => (byte)(Light >> LightBits);

        public byte BlockLight => (byte)(Light & LightMask);

        public BlockFacing Facing => (BlockFacing)(State & FacingMask);

        /// <summary>Returns a copy with new light levels (each clamped to 0-15).</summary>
        public Voxel WithLight(byte sunLight, byte blockLight)
        {
            int sun = Math.Min((int)sunLight, MaxLight);
            int block = Math.Min((int)blockLight, MaxLight);
            return new Voxel(BlockId, (byte)((sun << LightBits) | block), State);
        }

        /// <summary>Returns a copy facing a new direction, keeping the reserved state bits.</summary>
        public Voxel WithFacing(BlockFacing facing)
        {
            return new Voxel(BlockId, Light, (byte)((State & ~FacingMask) | ((byte)facing & FacingMask)));
        }

        public bool Equals(Voxel other)
        {
            return BlockId == other.BlockId && Light == other.Light && State == other.State;
        }

        public override bool Equals(object obj) => obj is Voxel other && Equals(other);

        public override int GetHashCode() => BlockId | (Light << 16) | (State << 24);

        public static bool operator ==(Voxel a, Voxel b) => a.Equals(b);

        public static bool operator !=(Voxel a, Voxel b) => !a.Equals(b);

        public override string ToString() => $"Voxel(id {BlockId}, sun {SunLight}, light {BlockLight}, {Facing})";
    }
}
