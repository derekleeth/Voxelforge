using System.Collections.Generic;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>Every <see cref="LootTable"/> in the game (Roadmap 5.5): one per mob kind, and the ruin cache.</summary>
    [CreateAssetMenu(menuName = "Voxelforge/Loot Book", fileName = "LootBook")]
    public sealed class LootBook : ScriptableObject
    {
        [SerializeField] List<LootTable> tables = new List<LootTable>();

        public IReadOnlyList<LootTable> Tables => tables;

        /// <summary>What a mob of <paramref name="mob"/> drops. False for mobs that drop nothing.</summary>
        public bool TryGetForMob(MobId mob, out LootTable table) => TryFind(LootSource.Mob, mob, out table);

        /// <summary>What each hero finds in a ruin's chest the first time they open it.</summary>
        public bool TryGetRuinCache(out LootTable table) => TryFind(LootSource.RuinCache, MobId.None, out table);

        bool TryFind(LootSource source, MobId mob, out LootTable table)
        {
            foreach (LootTable candidate in tables)
            {
                if (candidate != null && candidate.Source == source && (source != LootSource.Mob || candidate.Mob == mob))
                {
                    table = candidate;
                    return true;
                }
            }
            table = null;
            return false;
        }

        /// <summary>
        /// Returns every problem with the book. An empty list means it's valid. <paramref name="items"/> and
        /// <paramref name="blocks"/> say which item IDs exist.
        /// </summary>
        public List<string> Validate(ItemRegistry items, BlockRegistry blocks)
        {
            var errors = new List<string>();
            var seen = new HashSet<(LootSource, MobId)>();
            for (int i = 0; i < tables.Count; i++)
            {
                LootTable table = tables[i];
                if (table == null)
                {
                    errors.Add($"Entry {i} is empty.");
                    continue;
                }
                string label = table.Label;
                if (table.Source == LootSource.Mob && table.Mob == MobId.None)
                    errors.Add($"'{table.name}' is for a mob but names none.");
                MobId key = table.Source == LootSource.Mob ? table.Mob : MobId.None;
                if (!seen.Add((table.Source, key)))
                    errors.Add($"There's more than one table for {label}.");

                foreach (LootDrop drop in table.Drops)
                {
                    CheckItem(errors, label, drop.Item, items, blocks);
                    if (drop.Chance <= 0f)
                        errors.Add($"{label} drops item {drop.Item} with a chance of 0, so it never drops.");
                    if (drop.Min < 1 || drop.Max < drop.Min)
                        errors.Add($"{label} drops {drop.Min}-{drop.Max} of item {drop.Item}; it needs 1 or more, smallest first.");
                }

                float weights = 0f;
                foreach (LootEntry entry in table.Pool)
                {
                    CheckItem(errors, label, entry.Item, items, blocks);
                    if (entry.Weight <= 0f)
                        errors.Add($"{label}'s pool has item {entry.Item} with a weight of 0, so it never drops.");
                    if (entry.Min < 1 || entry.Max < entry.Min)
                        errors.Add($"{label}'s pool gives {entry.Min}-{entry.Max} of item {entry.Item}; it needs 1 or more, smallest first.");
                    weights += entry.Weight;
                }
                if (table.Rolls > 0 && weights <= 0f)
                    errors.Add($"{label} rolls its pool {table.Rolls} times, but the pool has nothing in it.");
                if (table.Drops.Count == 0 && (table.Rolls == 0 || weights <= 0f))
                    errors.Add($"{label} never drops anything.");
            }
            return errors;
        }

        static void CheckItem(List<string> errors, string label, ushort item, ItemRegistry items, BlockRegistry blocks)
        {
            bool known = ItemIds.IsBlock(item)
                ? item != 0 && (blocks == null || blocks.TryGet(item, out _))
                : items == null || items.TryGet(item, out _);
            if (!known)
                errors.Add($"{label} drops item {item}, which doesn't exist.");
        }

        /// <summary>Builds a book in code, for tests and tools.</summary>
        internal static LootBook Create(IEnumerable<LootTable> lootTables)
        {
            var book = CreateInstance<LootBook>();
            book.tables = new List<LootTable>(lootTables);
            return book;
        }
    }
}
