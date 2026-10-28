using Unity.Mathematics;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// Where trees grow (Roadmap 2.6). Terrain generation uses it to fill trunks with <see cref="BlockIds.TreeTrunk"/>
    /// voxels, and the decorator uses it to size the tree mesh drawn over them, so the two always agree.
    /// <para>
    /// The world is cut into square cells and each cell has one <b>candidate</b> column, picked by hash. The last row
    /// and column of every cell are never picked, so two trees are always at least 2 blocks apart and never touch.
    /// Whether the candidate really grows a tree is up to its biome (<see cref="BiomeInfo.TreeChance"/>) and the ground.
    /// </para>
    /// Burst-safe and integer-only, so every machine places the same trees.
    /// </summary>
    public static class TreePlacement
    {
        const uint TreeSalt = 8;

        /// <summary>The per-world number every tree hash starts from.</summary>
        public static uint SeedHash(WorldSeed seed) => seed.Hash(TreeSalt);

        /// <summary>True if (<paramref name="x"/>, <paramref name="z"/>) is its cell's one tree spot.</summary>
        public static bool IsCandidate(uint seedHash, int x, int z, int cellSize)
        {
            if (cellSize < 2)
                return false;
            int cellX = FloorDiv(x, cellSize);
            int cellZ = FloorDiv(z, cellSize);
            uint span = (uint)(cellSize - 1);
            int offsetX = (int)(math.hash(new uint4((uint)cellX, (uint)cellZ, seedHash, 1)) % span);
            int offsetZ = (int)(math.hash(new uint4((uint)cellX, (uint)cellZ, seedHash, 2)) % span);
            return x - cellX * cellSize == offsetX && z - cellZ * cellSize == offsetZ;
        }

        /// <summary>A number from 0 (inclusive) to 1 (exclusive) for this column, compared with the biome's tree chance.</summary>
        public static float Roll(uint seedHash, int x, int z)
        {
            return (math.hash(new uint4((uint)x, (uint)z, seedHash, 3)) >> 8) * (1f / 16777216f);
        }

        /// <summary>How many trunk blocks the tree at this column has, from <paramref name="min"/> to <paramref name="max"/>.</summary>
        public static int TrunkHeight(uint seedHash, int x, int z, int min, int max)
        {
            uint range = (uint)math.max(max - min + 1, 1);
            return min + (int)(math.hash(new uint4((uint)x, (uint)z, seedHash, 4)) % range);
        }

        /// <summary>A free number per tree, for picking its look and turning it.</summary>
        public static uint Variant(uint seedHash, int x, int z) => math.hash(new uint4((uint)x, (uint)z, seedHash, 5));

        /// <summary>Division that rounds down for negative numbers too (-1 / 6 = -1, not 0).</summary>
        static int FloorDiv(int value, int divisor) => (value >= 0 ? value : value - divisor + 1) / divisor;
    }
}
