using System;
using System.Collections.Generic;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>How much XP one kind of mob gives (Roadmap 5.6).</summary>
    [Serializable]
    public struct MobXp
    {
        public MobId Mob;
        [Min(0)] public int Xp;

        public MobXp(MobId mob, int xp)
        {
            Mob = mob;
            Xp = xp;
        }
    }

    /// <summary>
    /// Every number about levels (Roadmap 5.6, D-037): the cap, the XP each level needs, what a level adds, how far XP
    /// is shared, and what each mob is worth. <c>LevelBuilder</c> (Voxelforge &gt; Build Levels) writes it; don't edit
    /// it by hand. <see cref="LevelRules"/> does the sums.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxelforge/Level Book", fileName = "LevelBook")]
    public sealed class LevelBook : ScriptableObject
    {
        [Tooltip("The highest level.")]
        [SerializeField, Range(2, 100)] int maxLevel = 20;

        [Tooltip("Entry i: the XP needed to go from level i + 1 to i + 2. One fewer than the max level.")]
        [SerializeField] int[] xpToNext = Array.Empty<int>();

        [SerializeField, Min(0f)] float healthPerLevel = 10f;

        [SerializeField, Min(0f)] float manaPerLevel = 5f;

        [Tooltip("Every hero this close to a defeated mob gets its full XP, in metres.")]
        [SerializeField, Min(1f)] float shareRadius = 40f;

        [SerializeField] List<MobXp> mobXp = new List<MobXp>();

        public int MaxLevel => maxLevel;
        public float HealthPerLevel => healthPerLevel;
        public float ManaPerLevel => manaPerLevel;
        public float ShareRadius => shareRadius;
        public IReadOnlyList<MobXp> Mobs => mobXp;

        /// <summary>The XP to go from <paramref name="level"/> to the next, or 0 at (or past) the cap.</summary>
        public int XpToNext(int level)
        {
            if (level < 1 || level >= maxLevel || level - 1 >= xpToNext.Length)
                return 0;
            return xpToNext[level - 1];
        }

        public bool TryGetMobXp(MobId mob, out int xp)
        {
            foreach (MobXp entry in mobXp)
            {
                if (entry.Mob == mob)
                {
                    xp = entry.Xp;
                    return true;
                }
            }
            xp = 0;
            return false;
        }

        /// <summary>Returns every problem with the book. An empty list means it's valid.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            if (xpToNext.Length != maxLevel - 1)
                errors.Add($"It needs {maxLevel - 1} XP steps for a cap of {maxLevel}, but has {xpToNext.Length}.");
            for (int i = 0; i < xpToNext.Length; i++)
            {
                if (xpToNext[i] <= 0)
                    errors.Add($"Level {i + 1} needs {xpToNext[i]} XP to the next; it must be more than 0.");
                else if (i > 0 && xpToNext[i] < xpToNext[i - 1])
                    errors.Add($"Level {i + 1} needs less XP than level {i}; each level should need at least as much.");
            }
            var seen = new HashSet<MobId>();
            foreach (MobXp entry in mobXp)
            {
                if (entry.Mob == MobId.None)
                    errors.Add("A mob XP entry names no mob.");
                else if (!seen.Add(entry.Mob))
                    errors.Add($"{entry.Mob} has more than one XP entry.");
            }
            return errors;
        }

        /// <summary>Builds a book in code, for tests and tools.</summary>
        internal static LevelBook Create(int maxLevel, int[] xpToNext, float healthPerLevel = 10f, float manaPerLevel = 5f,
            float shareRadius = 40f, IEnumerable<MobXp> mobXp = null)
        {
            var book = CreateInstance<LevelBook>();
            book.maxLevel = maxLevel;
            book.xpToNext = xpToNext;
            book.healthPerLevel = healthPerLevel;
            book.manaPerLevel = manaPerLevel;
            book.shareRadius = shareRadius;
            book.mobXp = new List<MobXp>(mobXp ?? Array.Empty<MobXp>());
            return book;
        }
    }
}
