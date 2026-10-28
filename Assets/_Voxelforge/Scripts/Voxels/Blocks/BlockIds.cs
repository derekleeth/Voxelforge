namespace Voxelforge.Voxels
{
    /// <summary>
    /// Permanent block IDs. These numbers are written into save files and sent over the network,
    /// so never change or reuse one. New blocks get the next unused number.
    /// Each must match the ID on its <see cref="BlockDefinition"/> asset.
    /// </summary>
    public static class BlockIds
    {
        public const ushort Air = 0;
        public const ushort Grass = 1;
        public const ushort Dirt = 2;
        public const ushort Stone = 3;
        public const ushort ManaCrystalOre = 4;
        public const ushort Sand = 5;
        public const ushort Snow = 6;
        public const ushort Sandstone = 7;
        public const ushort RedRock = 8;
        /// <summary>Invisible but solid: fills a tree's trunk so the tree mesh (Roadmap 2.6) blocks and collides.</summary>
        public const ushort TreeTrunk = 9;
    }
}
