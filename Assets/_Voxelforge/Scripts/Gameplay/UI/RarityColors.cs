using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// The colour of each <see cref="ItemRarity"/> (Roadmap 5.5), the way RPG players expect: white, blue, purple and
    /// orange. Presentation only: the rules never see a colour.
    /// </summary>
    public static class RarityColors
    {
        // Common is a warm parchment white so it reads on the bag's dark slots.
        static readonly Color Common = new Color(0.96f, 0.93f, 0.86f);
        static readonly Color Rare = new Color(0.25f, 0.6f, 1f);
        static readonly Color Epic = new Color(0.72f, 0.4f, 1f);
        static readonly Color Legendary = new Color(1f, 0.55f, 0.1f);

        public static Color Of(ItemRarity rarity) => rarity switch
        {
            ItemRarity.Rare => Rare,
            ItemRarity.Epic => Epic,
            ItemRarity.Legendary => Legendary,
            _ => Common,
        };

        /// <summary>The colour as TextMeshPro rich-text hex, e.g. "#4099FF".</summary>
        public static string Hex(ItemRarity rarity) => "#" + ColorUtility.ToHtmlStringRGB(Of(rarity));

        /// <summary><paramref name="text"/> wrapped in the rarity's colour, for TextMeshPro.</summary>
        public static string Wrap(string text, ItemRarity rarity) => $"<color={Hex(rarity)}>{text}</color>";
    }
}
