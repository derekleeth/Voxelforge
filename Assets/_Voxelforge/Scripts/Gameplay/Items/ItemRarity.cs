namespace Voxelforge.Gameplay
{
    /// <summary>
    /// How rare an item is (Roadmap 5.5, D-036). Fixed per item, WoW-style: a Tuskhide Jerkin is always Rare, so a bag
    /// slot still only needs the item's ID. Better versions of a piece are separate items. Saved nowhere.
    /// </summary>
    public enum ItemRarity : byte
    {
        Common = 0,
        Rare = 1,
        Epic = 2,
        Legendary = 3,
    }
}
