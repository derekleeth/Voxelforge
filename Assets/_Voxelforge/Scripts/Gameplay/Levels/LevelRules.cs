using System.Collections.Generic;
using Unity.Mathematics;
using Voxelforge.Core;

namespace Voxelforge.Gameplay
{
    /// <summary>A hero somewhere in the world, for <see cref="LevelRules.Sharers"/>.</summary>
    public readonly struct HeroAt
    {
        public readonly PlayerId Id;
        public readonly float3 Position;

        public HeroAt(PlayerId id, float3 position)
        {
            Id = id;
            Position = position;
        }
    }

    /// <summary>
    /// The sums behind levels (Roadmap 5.6, D-037), as plain C# so tests can drive them; the numbers are the
    /// <see cref="LevelBook"/>'s. The host's <see cref="PlayerLevel"/> and <see cref="ExperienceAwards"/> use them.
    /// </summary>
    public static class LevelRules
    {
        /// <summary>
        /// <paramref name="state"/> after earning <paramref name="xp"/>: XP left over after a level carries on, several
        /// levels can come at once, and at the cap XP stops (and stays 0). <paramref name="levelsGained"/> says how many.
        /// </summary>
        public static LevelState Gain(LevelState state, int xp, LevelBook book, out int levelsGained)
        {
            levelsGained = 0;
            int level = state.EffectiveLevel;
            if (level >= book.MaxLevel)
                return new LevelState(book.MaxLevel, 0);
            long total = (long)state.Xp + System.Math.Max(0, xp);
            while (level < book.MaxLevel)
            {
                int need = book.XpToNext(level);
                if (need <= 0 || total < need)
                    break;
                total -= need;
                level++;
                levelsGained++;
            }
            if (level >= book.MaxLevel)
                total = 0;
            return new LevelState(level, (int)System.Math.Min(total, int.MaxValue));
        }

        /// <summary>Max health a hero's level adds on top of their class, race and gear.</summary>
        public static float BonusHealth(int level, LevelBook book) =>
            book == null ? 0f : math.max(0, level - 1) * book.HealthPerLevel;

        /// <summary>Max mana a hero's level adds.</summary>
        public static float BonusMana(int level, LevelBook book) =>
            book == null ? 0f : math.max(0, level - 1) * book.ManaPerLevel;

        /// <summary>How far through this level a hero is, 0-1 (1 at the cap), for the XP bar.</summary>
        public static float Progress(LevelState state, LevelBook book)
        {
            int need = book != null ? book.XpToNext(state.EffectiveLevel) : 0;
            return need <= 0 ? 1f : math.saturate(state.Xp / (float)need);
        }

        /// <summary>
        /// Everyone who shares a defeated mob's XP (cleared first): every hero within <paramref name="radius"/> metres of
        /// where it fell, fighting or not. Each gets the full amount.
        /// </summary>
        public static void Sharers(float3 mobAt, IReadOnlyList<HeroAt> heroes, float radius, List<PlayerId> into)
        {
            into.Clear();
            float limit = radius * radius;
            foreach (HeroAt hero in heroes)
            {
                if (hero.Id.IsValid && math.distancesq(hero.Position, mobAt) <= limit && !into.Contains(hero.Id))
                    into.Add(hero.Id);
            }
        }
    }
}
