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

        // Dark enough to read on the parchment.
        const string Better = "#2E7D32";
        const string Worse = "#B3261E";

        /// <summary>
        /// How <paramref name="candidate"/> compares with what's worn in its slot (Roadmap 5.5): "vs Bristlehide Cap: +5
        /// Armor, -3 Power", gains in green and losses in red (TextMeshPro rich text). "Nothing worn there" names no item;
        /// "" for things that aren't worn gear.
        /// </summary>
        public static string Compare(IItemRules rules, ushort candidate, EquippedGear gear, System.Func<ushort, string> nameOf)
        {
            int socket = EquippedGear.SocketOf(rules.EquipSlotOf(candidate));
            if (socket < 0)
                return "";
            ushort worn = gear[socket];
            if (worn == candidate)
                return "same as worn";
            string diff = Difference(rules.StatsOf(candidate) - rules.StatsOf(worn));
            string against = worn == 0 ? "nothing worn there" : $"vs {nameOf?.Invoke(worn) ?? $"Item {worn}"}";
            return diff.Length > 0 ? $"{against}: {diff}" : $"{against}: no change";
        }

        /// <summary>"+5 Armor, -3 Power" with gains green and losses red, leaving out the zeros.</summary>
        public static string Difference(GearStats diff)
        {
            var text = new StringBuilder();
            AppendChange(text, "Armor", diff.armor);
            AppendChange(text, "Stamina", diff.stamina);
            AppendChange(text, "Power", diff.power);
            return text.ToString();
        }

        static void AppendChange(StringBuilder text, string name, int change)
        {
            if (change == 0)
                return;
            if (text.Length > 0)
                text.Append(", ");
            text.Append("<color=").Append(change > 0 ? Better : Worse).Append('>')
                .Append(change > 0 ? "+" : "").Append(change).Append(' ').Append(name).Append("</color>");
        }

        /// <summary>The worn total for the bag window: "Armor 57 (-36% damage)\nStamina 41 · Power 40".</summary>
        public static string Summary(GearStats total) =>
            $"Armor {total.armor} (-{UnityEngine.Mathf.RoundToInt(GearRules.DamageCut(total.armor) * 100f)}% dmg)\n" +
            $"Stamina {total.stamina}  Power {total.power}";

        /// <summary>A short name for an empty socket: "Head", "Main", "Off".</summary>
        public static string ShortSlotName(EquipSlot slot) => slot switch
        {
            EquipSlot.MainHand => "Main",
            EquipSlot.OffHand => "Off",
            _ => slot.ToString(),
        };

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
