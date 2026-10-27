using System;
using Unity.Mathematics;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// Position of a chunk in the chunk grid. Chunk (1, 0, 0) starts at world block (32, 0, 0).
    /// Usable as a dictionary key and inside Burst jobs.
    /// </summary>
    public readonly struct ChunkCoord : IEquatable<ChunkCoord>
    {
        public readonly int3 Value;

        public ChunkCoord(int3 value) => Value = value;

        public ChunkCoord(int x, int y, int z) => Value = new int3(x, y, z);

        public int X => Value.x;
        public int Y => Value.y;
        public int Z => Value.z;

        /// <summary>World coordinate of this chunk's (0, 0, 0) corner block.</summary>
        public int3 WorldOrigin => Value << VoxelConstants.ChunkShift;

        public bool Equals(ChunkCoord other) => math.all(Value == other.Value);

        public override bool Equals(object obj) => obj is ChunkCoord other && Equals(other);

        public override int GetHashCode() => (int)math.hash(Value);

        public static bool operator ==(ChunkCoord a, ChunkCoord b) => a.Equals(b);

        public static bool operator !=(ChunkCoord a, ChunkCoord b) => !a.Equals(b);

        public override string ToString() => $"Chunk({Value.x}, {Value.y}, {Value.z})";
    }
}
