using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>A hero's health and mana pools: their class's, plus their race's bonuses (Roadmap 4.7).</summary>
    public readonly struct HeroPools
    {
        public readonly float MaxHealth;
        public readonly float MaxMana;
        public readonly float ManaRegen;

        public HeroPools(float maxHealth, float maxMana, float manaRegen)
        {
            MaxHealth = maxHealth;
            MaxMana = maxMana;
            ManaRegen = manaRegen;
        }
    }

    /// <summary>
    /// How a race changes a hero (Roadmap 4.7). Plain C#, so it's tested without a scene; the host's
    /// <see cref="PlayerClass"/> and <see cref="PlayerHearth"/> use it.
    /// </summary>
    public static class RaceRules
    {
        /// <summary>The class's pools with the race's bonuses added. A missing race adds nothing; pools never go below 1 health or 0 mana.</summary>
        public static HeroPools Pools(ClassDefinition heroClass, RaceDefinition race)
        {
            float health = heroClass != null ? heroClass.MaxHealth : 100f;
            float mana = heroClass != null ? heroClass.MaxMana : 100f;
            float regen = heroClass != null ? heroClass.ManaRegen : 10f;
            if (race != null)
            {
                health += race.BonusHealth;
                mana += race.BonusMana;
                regen += race.BonusManaRegen;
            }
            return new HeroPools(Mathf.Max(1f, health), Mathf.Max(0f, mana), Mathf.Max(0f, regen));
        }

        /// <summary>The class's action bar with the race's racial ability in slot 9 (<see cref="RaceDefinition.RacialSlot"/>).</summary>
        public static ActionBarSlots Bar(ActionBarLoadout loadout, RaceDefinition race)
        {
            ActionBarSlots slots = loadout != null ? loadout.ToSlots() : new ActionBarSlots();
            if (race != null && race.RacialAbility != null)
                slots[RaceDefinition.RacialSlot] = race.RacialAbility.Id;
            return slots;
        }

        /// <summary>How long the Home hearth cast takes for this race: shorter for the Hearthbound.</summary>
        public static float HearthSeconds(float baseSeconds, RaceDefinition race)
        {
            if (race == null || race.Passive != RacialPassive.Hearthbound || race.PassiveValue <= 0f)
                return baseSeconds;
            return baseSeconds * Mathf.Min(race.PassiveValue, 1f);
        }

        /// <summary>
        /// How many herbs a gather gives: <paramref name="baseYield"/>, more with Herb-Sense (Roadmap 5.5 calls this).
        /// The fraction left over is a chance of one more, rolled with <paramref name="roll01"/>.
        /// </summary>
        public static int HerbYield(int baseYield, RaceDefinition race, float roll01)
        {
            if (race == null || race.Passive != RacialPassive.HerbSense || race.PassiveValue <= 0f || baseYield <= 0)
                return baseYield;
            float bonus = baseYield * race.PassiveValue;
            int whole = Mathf.FloorToInt(bonus);
            return baseYield + whole + (roll01 < bonus - whole ? 1 : 0);
        }
    }
}
