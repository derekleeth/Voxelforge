using Unity.Collections;
using Unity.Mathematics;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// Where herbs grow (Roadmap 5.7, D-038). <see cref="Decorator"/> uses it to draw them and the host uses it to check
    /// a gatherer's request, so the two always agree and nothing about a herb needs saving or syncing except that it was
    /// picked (<c>HerbField</c>).
    /// <para>
    /// Like <see cref="TreePlacement"/>, the world is cut into <see cref="CellSize"/>-block squares, each with one
    /// <b>spot</b> picked by hash, never on a square's last row or column, so herbs are spread out and never touch. A herb
    /// grows on its spot when the block there is open to the sky (or a cave), one of the herb's surface blocks, and the
    /// roll comes in under the herb's <see cref="DecorationInfo.Density"/> times its weight in that biome.
    /// </para>
    /// Burst-safe and integer-only apart from the roll, so every machine places the same herbs.
    /// </summary>
    public static class HerbPlacement
    {
        /// <summary>The size of the squares that each get one herb spot, in blocks.</summary>
        public const int CellSize = 12;

        const uint HerbSalt = 13;

        /// <summary>The per-world number every herb hash starts from.</summary>
        public static uint SeedHash(WorldSeed seed) => seed.Hash(HerbSalt);

        /// <summary>True if (<paramref name="x"/>, <paramref name="z"/>) is its square's one herb spot.</summary>
        public static bool IsSpot(uint seedHash, int x, int z)
        {
            int cellX = FloorDiv(x, CellSize);
            int cellZ = FloorDiv(z, CellSize);
            const uint span = CellSize - 1;
            int offsetX = (int)(Mix(math.hash(new uint4((uint)cellX, (uint)cellZ, seedHash, 1))) % span);
            int offsetZ = (int)(Mix(math.hash(new uint4((uint)cellX, (uint)cellZ, seedHash, 2))) % span);
            return x - cellX * CellSize == offsetX && z - cellZ * CellSize == offsetZ;
        }

        /// <summary>
        /// Which herb (its decoration type, an index into <paramref name="decorations"/>) grows on the spot
        /// (<paramref name="x"/>, <paramref name="z"/>) on top of <paramref name="surface"/> in biome
        /// <paramref name="biome"/>, or -1 for none. Only call it for a spot (<see cref="IsSpot"/>) with air above.
        /// </summary>
        public static int Choose(uint seedHash, int x, int z, ushort surface, int biome, int biomeCount,
            NativeArray<DecorationInfo> decorations, NativeArray<float> biomeWeights)
        {
            float roll = (Mix(math.hash(new uint4((uint)x, (uint)z, seedHash, 3))) >> 8) * (1f / 16777216f);
            float total = 0f;
            for (int type = 0; type < decorations.Length; type++)
            {
                DecorationInfo info = decorations[type];
                if (info.Kind != DecorationKind.Herb || !info.GrowsOn(surface))
                    continue;
                total += info.Density * biomeWeights[type * biomeCount + biome];
                if (roll < total)
                    return type;
            }
            return -1;
        }

        /// <summary>A free number per herb, for where in its block it stands, its turn and its size.</summary>
        public static uint Variant(uint seedHash, int x, int z) => Mix(math.hash(new uint4((uint)x, (uint)z, seedHash, 4)));

        /// <summary>The block a herb drawn at <paramref name="position"/> stands in (the one above its surface block).</summary>
        public static int3 CellOf(float3 position) => (int3)math.floor(position + new float3(0f, 0.01f, 0f));

        /// <summary>
        /// Stirs a <c>math.hash</c> result (MurmurHash3's finaliser). <c>math.hash</c> of a vector is only a weighted sum
        /// of its parts, so nearby spots got rolls that went up and down together and far too many grew herbs.
        /// </summary>
        static uint Mix(uint h)
        {
            h ^= h >> 16;
            h *= 0x85EBCA6Bu;
            h ^= h >> 13;
            h *= 0xC2B2AE35u;
            h ^= h >> 16;
            return h;
        }

        /// <summary>Division that rounds down for negative numbers too (-1 / 12 = -1, not 0).</summary>
        static int FloorDiv(int value, int divisor) => (value >= 0 ? value : value - divisor + 1) / divisor;
    }
}
