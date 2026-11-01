using System;
using System.Collections.Generic;
using Voxelforge.Core;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Who gets loot (Roadmap 5.5, D-036), as plain C# so tests can drive it. Every hero who fought a mob (anyone on its
    /// <see cref="ThreatTable"/>: damage, stuns, taunts, and healing or shielding someone it was fighting) gets their own
    /// roll, and so does whoever landed the last hit, as long as they're still in the game. Nobody has to fight over
    /// drops, and a healer who never swung still gets something.
    /// </summary>
    public static class LootShares
    {
        /// <summary>
        /// Fills <paramref name="into"/> (cleared first) with each hero owed a roll, once each, in threat-table order then
        /// the <paramref name="killer"/>. <paramref name="present"/> says who's still in the game.
        /// </summary>
        public static void Fighters(IReadOnlyList<ThreatEntry> threat, PlayerId killer, Func<PlayerId, bool> present,
            List<PlayerId> into)
        {
            into.Clear();
            if (threat != null)
            {
                foreach (ThreatEntry entry in threat)
                    AddOnce(entry.Hero, present, into);
            }
            AddOnce(killer, present, into);
        }

        static void AddOnce(PlayerId hero, Func<PlayerId, bool> present, List<PlayerId> into)
        {
            if (hero.IsValid && hero != PlayerId.World && !into.Contains(hero) && present(hero))
                into.Add(hero);
        }

        /// <summary>The rarest thing in <paramref name="loot"/>, for how its sack glows. Common when empty.</summary>
        public static ItemRarity Best(IReadOnlyList<ItemStack> loot, IItemRules rules)
        {
            ItemRarity best = ItemRarity.Common;
            for (int i = 0; i < loot.Count; i++)
            {
                ItemRarity rarity = rules.RarityOf(loot[i].ItemId);
                if (rarity > best)
                    best = rarity;
            }
            return best;
        }

        /// <summary>
        /// Whether opening a chest should give this hero a ruin cache: the world made the chest (a ruin's, untouched since)
        /// and it hasn't given them theirs yet.
        /// </summary>
        public static bool OwesRuinCache(bool chestIsGenerated, ICollection<Unity.Mathematics.int3> alreadyLooted,
            Unity.Mathematics.int3 chest) =>
            chestIsGenerated && !alreadyLooted.Contains(chest);
    }
}
