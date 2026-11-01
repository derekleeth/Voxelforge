using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// What worn gear's stats do (Roadmap 5.5, D-036). Every number lives here, so the play-test can tune them in one
    /// place. Plain C#; the host applies them: <see cref="PlayerHealth"/> (Armor), <see cref="PlayerClass"/> (Stamina) and
    /// the spell paths (Power).
    /// </summary>
    public static class GearRules
    {
        /// <summary>The Armor that halves damage (if it weren't capped). WoW-style diminishing returns.</summary>
        public const float ArmorForHalf = 100f;

        /// <summary>Armor never cuts more than this share of a hit.</summary>
        public const float MaxDamageCut = 0.6f;

        public const float HealthPerStamina = 2f;

        /// <summary>Each point of Power adds this share to spell damage, healing and shields.</summary>
        public const float PowerPerPoint = 0.01f;

        /// <summary>The share of each hit Armor takes away: armor / (armor + <see cref="ArmorForHalf"/>), capped.</summary>
        public static float DamageCut(int armor) =>
            armor <= 0 ? 0f : Mathf.Min(MaxDamageCut, armor / (armor + ArmorForHalf));

        /// <summary>A hit of <paramref name="amount"/> after Armor.</summary>
        public static float AfterArmor(float amount, int armor) => amount * (1f - DamageCut(armor));

        /// <summary>Max health Stamina adds.</summary>
        public static float BonusHealth(int stamina) => Mathf.Max(0, stamina) * HealthPerStamina;

        /// <summary>What Power multiplies spell damage, healing and shields by: 1 with none.</summary>
        public static float PowerScale(int power) => 1f + Mathf.Max(0, power) * PowerPerPoint;
    }
}
