using System.Collections.Generic;
using Voxelforge.Core;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Every threat number in one place (Roadmap 5.3), so tuning the tank's job means changing only this file. See
    /// <see cref="ThreatTable"/> for how they're used.
    /// </summary>
    public static class ThreatRules
    {
        /// <summary>A hero in melee must pass the target's threat by this much to pull it (110%).</summary>
        public const float MeleeMargin = 1.1f;

        /// <summary>A hero further away must pass the target's threat by this much to pull it (130%).</summary>
        public const float RangedMargin = 1.3f;

        /// <summary>Being noticed (seen in its aggro radius, or called by its pack) only puts a hero on the table.</summary>
        public const float NoticeThreat = 1f;

        /// <summary>Threat for stunning it (Roadmap 4.7, Hoofquake).</summary>
        public const float StunThreat = 10f;

        /// <summary>Healing and shields make this much threat per point (half), shared among the foes it angers.</summary>
        public const float SupportShare = 0.5f;

        /// <summary>Threat for damage: one per point dealt, times the ability's (and class's) threat scale.</summary>
        public static float Damage(float dealt, float threatScale = 1f) => dealt > 0f ? dealt * threatScale : 0f;

        /// <summary>Threat for healing or shielding <paramref name="amount"/>, before it's shared out.</summary>
        public static float Support(float amount, float threatScale = 1f) => amount > 0f ? amount * SupportShare * threatScale : 0f;

        /// <summary>
        /// <i>WoW</i> style: helping a hero angers every foe already fighting them, so <paramref name="threat"/> is
        /// split evenly among the <paramref name="tables"/> that hold <paramref name="helped"/>, all given to
        /// <paramref name="helper"/>. A hero nobody is fighting makes no threat. Returns how many tables it went to.
        /// </summary>
        public static int ShareSupport(IReadOnlyList<ThreatTable> tables, PlayerId helper, PlayerId helped, float threat)
        {
            if (tables == null || !helper.IsValid || !helped.IsValid || !(threat > 0f))
                return 0;
            int angered = 0;
            foreach (ThreatTable table in tables)
            {
                if (table != null && table.Contains(helped))
                    angered++;
            }
            if (angered == 0)
                return 0;
            float each = threat / angered;
            foreach (ThreatTable table in tables)
            {
                if (table != null && table.Contains(helped))
                    table.Add(helper, each);
            }
            return angered;
        }
    }
}
