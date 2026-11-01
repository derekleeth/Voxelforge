using System;
using System.Collections.Generic;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Rolls a <see cref="LootTable"/> (Roadmap 5.5, D-036). <b>Host only</b> in the game (CLAUDE.md Law #2): each hero
    /// gets their own roll. Plain C# with the caller's <see cref="Random"/>, so tests can seed it.
    /// </summary>
    public static class LootRoller
    {
        /// <summary>
        /// Fills <paramref name="into"/> (cleared first) with one hero's loot: each drop on its own chance, then the pool
        /// rolls. Several of the same item come out as one stack, in the order they first dropped.
        /// </summary>
        public static void Roll(LootTable table, Random random, List<ItemStack> into)
        {
            into.Clear();
            if (table == null)
                return;
            foreach (LootDrop drop in table.Drops)
            {
                // NextDouble is below 1, so a chance of 1 always drops and 0 never does.
                if (drop.Item != 0 && random.NextDouble() < drop.Chance)
                    Add(into, drop.Item, Amount(random, drop.Min, drop.Max));
            }

            IReadOnlyList<LootEntry> pool = table.Pool;
            float total = Math.Max(0f, table.NothingWeight);
            foreach (LootEntry entry in pool)
                total += Math.Max(0f, entry.Weight);
            if (total <= 0f)
                return;
            for (int roll = 0; roll < table.Rolls; roll++)
            {
                double pick = random.NextDouble() * total;
                foreach (LootEntry entry in pool)
                {
                    float weight = Math.Max(0f, entry.Weight);
                    if (pick < weight)
                    {
                        if (entry.Item != 0)
                            Add(into, entry.Item, Amount(random, entry.Min, entry.Max));
                        break;
                    }
                    pick -= weight;
                }
                // Whatever's left over is the nothing weight: that roll gives nothing.
            }
        }

        // Min to Max, both included, however they're set.
        static int Amount(Random random, int min, int max)
        {
            min = Math.Max(1, min);
            max = Math.Max(min, max);
            return random.Next(min, max + 1);
        }

        static void Add(List<ItemStack> into, ushort item, int amount)
        {
            for (int i = 0; i < into.Count; i++)
            {
                if (into[i].ItemId == item)
                {
                    into[i] = new ItemStack(item, (ushort)Math.Min(ushort.MaxValue, into[i].Count + amount));
                    return;
                }
            }
            into.Add(new ItemStack(item, (ushort)Math.Min(ushort.MaxValue, amount)));
        }
    }
}
