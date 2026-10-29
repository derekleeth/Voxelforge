namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Which tab of the inventory window an item sorts into (Roadmap 4.5, D-028). Also sets how many fit in one slot
    /// unless the item says otherwise (<see cref="ItemRules.DefaultMaxStack"/>). Saved nowhere: only the item ID is.
    /// </summary>
    public enum ItemCategory : byte
    {
        /// <summary>On an <see cref="ItemDefinition"/>: work it out from the ID (blocks are Blocks, the rest Gear).</summary>
        Auto = 0,
        Blocks = 1,
        /// <summary>Tools, weapons, bags and (5.4) armour. One per slot.</summary>
        Gear = 2,
        /// <summary>Potions and food (5.6).</summary>
        Consumables = 3,
        /// <summary>Ores, herbs, hides: things you craft with.</summary>
        Materials = 4,
    }

    /// <summary>What a bag needs to know about items: which tab they go in and how many fit in a slot.</summary>
    public interface IItemRules
    {
        /// <summary>Never <see cref="ItemCategory.Auto"/>.</summary>
        ItemCategory CategoryOf(ushort itemId);

        /// <summary>How many fit in one slot. At least 1.</summary>
        int MaxStackOf(ushort itemId);

        /// <summary>How many slots the item adds when equipped as a bag (Roadmap 4.5), or 0 if it isn't a bag.</summary>
        int BagSlotsOf(ushort itemId);
    }

    /// <summary>The rules for items nobody has described: blocks stack to 999, everything else is one-per-slot gear.</summary>
    public sealed class ItemRules : IItemRules
    {
        /// <summary>Blocks and materials: so building never crowds out loot.</summary>
        public const int BigStack = 999;

        public const int ConsumableStack = 20;

        public static readonly ItemRules Default = new ItemRules();

        public static ItemCategory DefaultCategory(ushort itemId) =>
            ItemIds.IsBlock(itemId) ? ItemCategory.Blocks : ItemCategory.Gear;

        public static int DefaultMaxStack(ItemCategory category) => category switch
        {
            ItemCategory.Gear => 1,
            ItemCategory.Consumables => ConsumableStack,
            _ => BigStack,
        };

        public ItemCategory CategoryOf(ushort itemId) => DefaultCategory(itemId);
        public int MaxStackOf(ushort itemId) => DefaultMaxStack(CategoryOf(itemId));
        public int BagSlotsOf(ushort itemId) => 0;
    }
}
