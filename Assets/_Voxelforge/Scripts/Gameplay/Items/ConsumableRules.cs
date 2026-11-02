using System;
using System.Collections.Generic;
using Unity.Netcode;

namespace Voxelforge.Gameplay
{
    /// <summary>What a <see cref="AbilityDelivery.Drink"/> does (Roadmap 5.8).</summary>
    public enum DrinkKind : byte
    {
        None,
        HealthPotion,
        ManaPotion,
        Elixir,
    }

    /// <summary>The elixir a hero is under (Roadmap 5.8), as the host shares it. Empty when <see cref="AbilityId"/> is none.</summary>
    public struct ElixirState : INetworkSerializeByMemcpy, IEquatable<ElixirState>
    {
        public ushort AbilityId;
        /// <summary>Seconds left when the host set this. Each PC counts down from when it heard.</summary>
        public float SecondsLeft;

        public bool Equals(ElixirState other) => AbilityId == other.AbilityId && SecondsLeft.Equals(other.SecondsLeft);
        public override bool Equals(object obj) => obj is ElixirState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(AbilityId, SecondsLeft);
    }

    /// <summary>
    /// The rules for potions and elixirs (Roadmap 5.8, D-039). Plain C#, so it's tested without a scene;
    /// <see cref="PlayerConsumables"/> runs them on the host.
    /// </summary>
    public static class ConsumableRules
    {
        /// <summary>Action bar slot 7 drinks the best health potion owned (0-based).</summary>
        public const int HealthPotionSlot = 6;

        /// <summary>Action bar slot 8 drinks the best mana potion owned (0-based).</summary>
        public const int ManaPotionSlot = 7;

        // Within this of full counts as full, so rounding can't waste a potion.
        const float FullMargin = 0.5f;

        public static DrinkKind KindOf(AbilityDefinition ability)
        {
            if (ability == null || ability.Delivery != AbilityDelivery.Drink)
                return DrinkKind.None;
            if (!ability.Buff.IsZero)
                return DrinkKind.Elixir;
            return ability.HealAmount > 0f ? DrinkKind.HealthPotion : ability.ManaAmount > 0f ? DrinkKind.ManaPotion : DrinkKind.None;
        }

        /// <summary>Whether the hero can drink <paramref name="ability"/>: they own one, and a potion wouldn't be wasted.</summary>
        public static CastCheck Check(AbilityDefinition ability, int owned, float health, float maxHealth, float mana, float maxMana)
        {
            DrinkKind kind = KindOf(ability);
            if (kind == DrinkKind.None)
                return CastCheck.UnknownAbility;
            if (owned <= 0)
                return CastCheck.NoPotion;
            if (kind == DrinkKind.HealthPotion && health >= maxHealth - FullMargin)
                return CastCheck.FullHealth;
            if (kind == DrinkKind.ManaPotion && mana >= maxMana - FullMargin)
                return CastCheck.FullMana;
            return CastCheck.Ok;
        }

        /// <summary>
        /// What a potion slot shows: the strongest potion of <paramref name="kind"/> in <paramref name="bag"/>, or with none
        /// owned the weakest there is, so the slot still says what belongs there. Null if no such potion exists.
        /// </summary>
        public static AbilityDefinition BestPotion(IReadOnlyList<ItemDefinition> drinks, IReadOnlyList<ItemStack> bag, DrinkKind kind)
        {
            AbilityDefinition best = null;
            AbilityDefinition weakest = null;
            foreach (ItemDefinition item in drinks)
            {
                AbilityDefinition ability = item != null ? item.Drink : null;
                if (KindOf(ability) != kind)
                    continue;
                if (weakest == null || Strength(ability) < Strength(weakest))
                    weakest = ability;
                if (ItemStacks.CountOf(bag, item.Id) > 0 && (best == null || Strength(ability) > Strength(best)))
                    best = ability;
            }
            return best != null ? best : weakest;
        }

        static float Strength(AbilityDefinition ability) => ability.HealAmount + ability.ManaAmount;

        /// <summary><paramref name="slots"/> with the potions in their two slots, over whatever the loadout put there.</summary>
        public static ActionBarSlots WithPotions(ActionBarSlots slots, ushort healthPotion, ushort manaPotion)
        {
            slots[HealthPotionSlot] = healthPotion;
            slots[ManaPotionSlot] = manaPotion;
            return slots;
        }
    }
}
