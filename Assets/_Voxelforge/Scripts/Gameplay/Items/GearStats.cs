using System;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Where a piece of gear is worn (Roadmap 5.5, D-036). <see cref="None"/> for everything that isn't worn gear (bags
    /// have their own sockets, <see cref="EquippedBags"/>). Socket <c>slot - 1</c> of <see cref="EquippedGear"/>.
    /// </summary>
    public enum EquipSlot : byte
    {
        None = 0,
        Head = 1,
        Chest = 2,
        Legs = 3,
        Feet = 4,
        MainHand = 5,
        OffHand = 6,
    }

    /// <summary>
    /// How a melee weapon hits (Roadmap 5.5 step G): worn in the Main Hand, the Mine button swings it at a foe in
    /// <see cref="reach"/> instead of mining. Ranged weapons (the wand) shoot their item's ability instead.
    /// </summary>
    [Serializable]
    public struct WeaponStats
    {
        [Tooltip("Damage each hit does, before Power.")]
        [Min(0f)] public float damage;

        [Tooltip("Seconds between hits.")]
        [Min(0.1f)] public float swingSeconds;

        [Tooltip("How far it reaches, in metres from the hero's eyes.")]
        [Min(0.5f)] public float reach;

        public WeaponStats(float damage, float swingSeconds, float reach)
        {
            this.damage = damage;
            this.swingSeconds = swingSeconds;
            this.reach = reach;
        }

        public bool IsMelee => damage > 0f;
    }

    /// <summary>
    /// What a piece of gear gives while worn (Roadmap 5.5). <see cref="GearRules"/> turns the totals into effects:
    /// Armor cuts mob damage, Stamina adds health, Power strengthens spells and healing.
    /// </summary>
    [Serializable]
    public struct GearStats : IEquatable<GearStats>
    {
        [Tooltip("Cuts the damage mobs deal.")]
        [Min(0)] public int armor;

        [Tooltip("Adds max health.")]
        [Min(0)] public int stamina;

        [Tooltip("Adds to spell damage, healing and shields.")]
        [Min(0)] public int power;

        public GearStats(int armor, int stamina, int power)
        {
            this.armor = armor;
            this.stamina = stamina;
            this.power = power;
        }

        public bool IsZero => armor == 0 && stamina == 0 && power == 0;

        public static GearStats operator +(GearStats a, GearStats b) =>
            new GearStats(a.armor + b.armor, a.stamina + b.stamina, a.power + b.power);

        public static GearStats operator -(GearStats a, GearStats b) =>
            new GearStats(a.armor - b.armor, a.stamina - b.stamina, a.power - b.power);

        /// <summary>The stats of every piece worn in <paramref name="gear"/>, added up.</summary>
        public static GearStats Sum(EquippedGear gear, IItemRules rules)
        {
            GearStats total = default;
            for (int i = 0; i < EquippedGear.SocketCount; i++)
            {
                if (gear[i] != 0)
                    total += rules.StatsOf(gear[i]);
            }
            return total;
        }

        public bool Equals(GearStats other) => armor == other.armor && stamina == other.stamina && power == other.power;
        public override bool Equals(object obj) => obj is GearStats other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(armor, stamina, power);
        public override string ToString() => $"Armor {armor}, Stamina {stamina}, Power {power}";
    }
}
