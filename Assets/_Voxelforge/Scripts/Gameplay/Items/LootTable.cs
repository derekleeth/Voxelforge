using System;
using System.Collections.Generic;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>What a <see cref="LootTable"/> is rolled for (Roadmap 5.5).</summary>
    public enum LootSource : byte
    {
        /// <summary>A defeated mob of <see cref="LootTable.Mob"/>, once for each hero who fought it.</summary>
        Mob = 0,
        /// <summary>A ruin's Storage Chest (5.4), once for each hero, the first time they open it.</summary>
        RuinCache = 1,
        /// <summary>
        /// What a gathering node gives each time (Roadmap 5.7): a herb, or a skinned body. Named by its node in the
        /// Profession Book, not in the Loot Book.
        /// </summary>
        Gathering = 2,
    }

    /// <summary>An item that drops on its own chance, whatever else drops: the hides and fangs (Roadmap 5.5).</summary>
    [Serializable]
    public struct LootDrop
    {
        [Tooltip("The item ID (ItemIds), or a block's ID.")]
        public ushort Item;

        [Tooltip("The chance it drops, 0-1. 1 = always.")]
        [Range(0f, 1f)] public float Chance;

        [Tooltip("How many drop, from Min to Max.")]
        [Min(1)] public int Min;

        [Min(1)] public int Max;

        public LootDrop(ushort item, float chance, int min = 1, int max = 1)
        {
            Item = item;
            Chance = chance;
            Min = min;
            Max = max;
        }
    }

    /// <summary>One item in a <see cref="LootTable"/>'s pool: each roll picks one entry (or nothing) by weight.</summary>
    [Serializable]
    public struct LootEntry
    {
        [Tooltip("The item ID (ItemIds), or a block's ID.")]
        public ushort Item;

        [Tooltip("How likely, against the other entries and Nothing Weight. Rarer gear gets smaller weights.")]
        [Min(0f)] public float Weight;

        [Min(1)] public int Min;

        [Min(1)] public int Max;

        public LootEntry(ushort item, float weight, int min = 1, int max = 1)
        {
            Item = item;
            Weight = weight;
            Min = min;
            Max = max;
        }
    }

    /// <summary>
    /// What one hero gets from a mob or a ruin cache (Roadmap 5.5, D-036): every <see cref="Drops"/> entry on its own
    /// chance, then <see cref="Rolls"/> picks from the weighted <see cref="Pool"/> (where gear lives). Rolled on the host
    /// by <see cref="LootRoller"/>, once per hero, so co-op partners never share a drop. <c>LootBuilder</c> writes every
    /// table; don't edit them by hand.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxelforge/Loot Table", fileName = "NewLootTable")]
    public sealed class LootTable : ScriptableObject
    {
        [SerializeField] LootSource source;

        [Tooltip("Which mob drops it (Source = Mob).")]
        [SerializeField] MobId mob;

        [Tooltip("Each drops on its own chance.")]
        [SerializeField] List<LootDrop> drops = new List<LootDrop>();

        [Tooltip("How many times to pick from the pool.")]
        [SerializeField, Min(0)] int rolls = 1;

        [SerializeField] List<LootEntry> pool = new List<LootEntry>();

        [Tooltip("The weight of a pool roll giving nothing at all.")]
        [SerializeField, Min(0f)] float nothingWeight;

        public LootSource Source => source;
        public MobId Mob => mob;
        public IReadOnlyList<LootDrop> Drops => drops;
        public int Rolls => rolls;
        public IReadOnlyList<LootEntry> Pool => pool;
        public float NothingWeight => nothingWeight;

        /// <summary>A readable name for messages: the mob's name, or "Ruin Cache".</summary>
        public string Label => source == LootSource.Mob ? mob.ToString() : source.ToString();

        /// <summary>Builds a table in code, for tests and tools.</summary>
        internal static LootTable Create(LootSource source, MobId mob, IEnumerable<LootDrop> drops, int rolls = 0,
            IEnumerable<LootEntry> pool = null, float nothingWeight = 0f)
        {
            var table = CreateInstance<LootTable>();
            table.name = source == LootSource.Mob ? mob.ToString() : source.ToString();
            table.source = source;
            table.mob = mob;
            table.drops = new List<LootDrop>(drops ?? Array.Empty<LootDrop>());
            table.rolls = rolls;
            table.pool = new List<LootEntry>(pool ?? Array.Empty<LootEntry>());
            table.nothingWeight = nothingWeight;
            return table;
        }
    }
}
