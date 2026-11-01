using System.Text;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// The words the bag uses for gear (Roadmap 5.5): an item's rarity, slot and stats, e.g. "Rare Chest: Armor 14,
    /// Stamina 6". Plain C# so tests can check it.
    /// </summary>
    public static class GearText
    {
        public static string SlotName(EquipSlot slot) => slot switch
        {
            EquipSlot.MainHand => "Main Hand",
            EquipSlot.OffHand => "Off Hand",
            _ => slot.ToString(),
        };

        /// <summary>
        /// What kind of item it is: "Rare Chest: Armor 14, Stamina 6" for worn gear, else its tab ("Materials"), with
        /// its rarity in front unless it's Common.
        /// </summary>
        public static string Kind(IItemRules rules, ushort itemId)
        {
            ItemRarity rarity = rules.RarityOf(itemId);
            string prefix = rarity == ItemRarity.Common ? "" : rarity + " ";
            EquipSlot slot = rules.EquipSlotOf(itemId);
            if (slot == EquipSlot.None)
                return prefix + rules.CategoryOf(itemId);
            string stats = Stats(rules.StatsOf(itemId));
            return stats.Length > 0 ? $"{prefix}{SlotName(slot)}: {stats}" : prefix + SlotName(slot);
        }

        /// <summary>"Armor 14, Stamina 6, Power 3", leaving out the zeros; "" when all are zero.</summary>
        public static string Stats(GearStats stats)
        {
            var text = new StringBuilder();
            Append(text, "Armor", stats.armor);
            Append(text, "Stamina", stats.stamina);
            Append(text, "Power", stats.power);
            return text.ToString();
        }

        static void Append(StringBuilder text, string name, int value)
        {
            if (value == 0)
                return;
            if (text.Length > 0)
                text.Append(", ");
            text.Append(name).Append(' ').Append(value);
        }
    }
}
