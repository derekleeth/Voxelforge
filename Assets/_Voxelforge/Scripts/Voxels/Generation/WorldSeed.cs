using System;
using Unity.Mathematics;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// The number a whole world grows from. The same seed always builds the same terrain, so saves (Roadmap 3.4)
    /// and joining players (6.2) only need the seed plus the blocks players changed.
    /// Burst-safe. Owned by <see cref="VoxelWorldManager"/>; the host picks it.
    /// </summary>
    public readonly struct WorldSeed : IEquatable<WorldSeed>
    {
        /// <summary>Noise offsets stay within this range, where <c>noise.snoise</c> still has plenty of float precision.</summary>
        const float OffsetRange = 1000f;

        public readonly int Value;

        public WorldSeed(int value) => Value = value;

        /// <summary>A fresh random seed for a new world.</summary>
        public static WorldSeed CreateRandom() => new WorldSeed(Guid.NewGuid().GetHashCode());

        /// <summary>
        /// Where one noise layer samples the noise field. <c>noise.snoise</c> has no seed parameter, so each layer
        /// (temperature, caves, ore...) is shifted by its own seeded offset instead. Integer math only, so it's
        /// identical on every machine.
        /// </summary>
        public float3 NoiseOffset(uint salt)
        {
            var random = Unity.Mathematics.Random.CreateFromIndex(Hash(salt));
            return random.NextFloat3(-OffsetRange, OffsetRange);
        }

        /// <summary>A well-mixed number for this seed and <paramref name="salt"/>, for hash-based randomness.</summary>
        public uint Hash(uint salt) => math.hash(new uint2((uint)Value, salt));

        public bool Equals(WorldSeed other) => Value == other.Value;

        public override bool Equals(object obj) => obj is WorldSeed other && Equals(other);

        public override int GetHashCode() => Value;

        public override string ToString() => Value.ToString();
    }
}
