namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Permanent item IDs (Roadmap 3.5). Every block is also an item with its block ID (<see cref="Voxels.BlockIds"/>),
    /// so items that aren't blocks start at <see cref="FirstNonBlock"/>. These numbers are saved in player bags and sent
    /// over the network, so never change or reuse one. Each must match the ID on its <see cref="ItemDefinition"/> asset.
    /// </summary>
    public static class ItemIds
    {
        /// <summary>IDs below this are blocks; at or above it, tools and other items.</summary>
        public const ushort FirstNonBlock = 1000;

        public const ushort WoodenPickaxe = 1000;
        public const ushort StonePickaxe = 1001;
        public const ushort StoneAxe = 1002;
        /// <summary>Kian's axe on one side and pick on the other: the best tool for mining and chopping.</summary>
        public const ushort PickOfDestiny = 1003;

        /// <summary>True if <paramref name="itemId"/> is a block, so it can be placed.</summary>
        public static bool IsBlock(ushort itemId) => itemId < FirstNonBlock;
    }
}
