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
        /// <summary>Wood from a chopped tree (Roadmap 3.2). Each trunk block drops one.</summary>
        public const ushort Log = 10;
        /// <summary>Stands on a floor or hangs on a wall and lights the dark (Roadmap 3.5). A prop: drawn as a model, not a cube.</summary>
        public const ushort Torch = 11;
        /// <summary>Where tools are crafted (Roadmap 3.5). A solid prop: an invisible full block under the bench model.</summary>
        public const ushort Workbench = 12;
        /// <summary>
        /// A player's home (Roadmap 3.8): a carved totem that glows green. Its owner can hearth back to it. A solid prop,
        /// like the Workbench.
        /// </summary>
        public const ushort HomeTotem = 13;
        /// <summary>
        /// A Storage Chest (Roadmap 4.5): 27 slots kept by the host and saved with the world. A solid prop, its lid drawn
        /// apart so it can open. Breaking it spills what's inside as loot bags.
        /// </summary>
        public const ushort Chest = 14;
        /// <summary>
        /// A Training Dummy (Roadmap 4.6): a straw dummy on a post that spells hurt instead of breaking, and that throws
        /// hay bales back. A blast-proof solid prop; an axe removes it.
        /// </summary>
        public const ushort TrainingDummy = 15;
        /// <summary>
        /// Earth raised by a spell (Roadmap 4.6: Rising Pillar, Earthen Wall): packed slabs with glowing cracks. It
        /// crumbles back to air after a while (<c>EarthWorks</c>) and drops nothing when mined.
        /// </summary>
        public const ushort EarthenRock = 16;
        /// <summary>Weathered stone bricks that old ruins are built of (Roadmap 5.4). Mined with a pick.</summary>
        public const ushort RuinBrick = 17;
        /// <summary>Ruin Brick with moss creeping over it (Roadmap 5.4).</summary>
        public const ushort MossyRuinBrick = 18;
        /// <summary>
        /// Dark slate carved with faintly glowing runes, the boss arena's floor and walls (Roadmap 5.4). Unbreakable and
        /// blast-proof, so nobody digs out of (or into) a boss fight.
        /// </summary>
        public const ushort WardedStone = 19;
        /// <summary>Stone studded with copper (Roadmap 5.7): the first ore tier, Mining 1. Drops Copper Ore.</summary>
        public const ushort CopperOre = 20;
        /// <summary>Stone with rusty iron nuggets (Roadmap 5.7): the second ore tier, Mining 25. Drops Iron Ore.</summary>
        public const ushort IronOre = 21;

        /// <summary>
        /// IDs from here up are items, not blocks (<c>ItemIds.FirstNonBlock</c>). A block can drop one (ore, Roadmap 5.7);
        /// the item registry checks it exists.
        /// </summary>
        public const ushort FirstItem = 1000;
    }
}
