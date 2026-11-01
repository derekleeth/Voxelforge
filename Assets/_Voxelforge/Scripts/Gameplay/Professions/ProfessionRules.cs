using Unity.Mathematics;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// The sums behind the gathering professions (Roadmap 5.7, D-038), as plain C# so tests can drive them; the numbers
    /// are the <see cref="ProfessionBook"/>'s. The host's <see cref="PlayerProfessions"/> uses them.
    /// </summary>
    public static class ProfessionRules
    {
        /// <summary>True if <paramref name="skills"/> are enough to gather <paramref name="node"/>.</summary>
        public static bool CanGather(ProfessionSkills skills, in GatherNode node) =>
            skills.Get(node.Profession) >= node.RequiredSkill;

        /// <summary>How a node needing <paramref name="required"/> compares with <paramref name="skill"/>.</summary>
        public static GatherDifficulty Difficulty(int skill, int required, ProfessionBook book)
        {
            int above = skill - required;
            if (above < 0)
                return GatherDifficulty.TooHard;
            if (above < book.OrangeRange)
                return GatherDifficulty.Orange;
            if (above < book.YellowRange)
                return GatherDifficulty.Yellow;
            if (above < book.GreenRange)
                return GatherDifficulty.Green;
            return GatherDifficulty.Grey;
        }

        /// <summary>The chance (0-1) that gathering a node needing <paramref name="required"/> raises <paramref name="skill"/>.</summary>
        public static float GainChance(int skill, int required, ProfessionBook book)
        {
            if (skill >= book.MaxSkill)
                return 0f;
            return Difficulty(skill, required, book) switch
            {
                GatherDifficulty.Orange => 1f,
                GatherDifficulty.Yellow => book.YellowChance,
                GatherDifficulty.Green => book.GreenChance,
                _ => 0f,
            };
        }

        /// <summary>
        /// After gathering a node needing <paramref name="required"/>: <paramref name="skills"/> with
        /// <paramref name="profession"/> one higher if <paramref name="roll"/> (0 to 1) came in under
        /// <see cref="GainChance"/>. True if it went up.
        /// </summary>
        public static bool TryGain(ProfessionSkills skills, ProfessionId profession, int required, ProfessionBook book,
            double roll, out ProfessionSkills after)
        {
            int skill = skills.Get(profession);
            after = skills;
            if (roll >= GainChance(skill, required, book))
                return false;
            after = skills.With(profession, skill + 1);
            return true;
        }

        /// <summary>
        /// The chance (0-1) of one extra item from a gather: 0 at the node's required skill, rising to the book's most
        /// once the gatherer is <see cref="ProfessionBook.BonusYieldRange"/> points above it.
        /// </summary>
        public static float BonusYieldChance(int skill, int required, ProfessionBook book) =>
            book.MaxBonusYieldChance * math.saturate((skill - required) / (float)book.BonusYieldRange);

        /// <summary>True if <paramref name="skill"/> shows the profession's heat map on the World Map.</summary>
        public static bool HasHeatMap(int skill, ProfessionBook book) => book != null && skill >= book.HeatMapSkill;
    }
}
