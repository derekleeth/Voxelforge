using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// One playable class (Roadmap 4.6): what a hero of this class can take and spend, and the spells on their action bar.
    /// The host applies it (<see cref="PlayerClass"/>), so these numbers are only ever trusted on the host. Only rules
    /// live here (Roadmap 4.2): its colour and crest are in <see cref="HudSkin"/>. Create one with Assets &gt; Create &gt;
    /// Voxelforge &gt; Class, then add it to the <see cref="ClassBook"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxelforge/Class", fileName = "NewClass")]
    public sealed class ClassDefinition : ScriptableObject
    {
        [Tooltip("Permanent ID saved in profiles and sent over the network (HeroClassId). Never change or reuse one.")]
        [SerializeField] HeroClassId id;

        [SerializeField] string displayName;

        [Tooltip("What the class window says about it: a sentence or two, for a player choosing.")]
        [SerializeField, TextArea] string description;

        [Header("Pools")]
        [SerializeField, Min(1f)] float maxHealth = 100f;

        [SerializeField, Min(0f)] float maxMana = 100f;

        [Tooltip("Mana back per second.")]
        [SerializeField, Min(0f)] float manaRegen = 10f;

        [Header("Threat (5.3)")]
        [Tooltip("Times the threat everything this class does makes (ThreatRules). A tank's is high so mobs stay on them.")]
        [SerializeField, Range(0.25f, 5f)] float threatMultiplier = 1f;

        [Tooltip("The class's passive, named in the class window, e.g. \"Runeforged Oath\". Empty = none.")]
        [SerializeField] string passiveName;

        [Tooltip("What the passive does, a short sentence for the class window.")]
        [SerializeField, TextArea] string passiveDescription;

        [Header("Spells")]
        [Tooltip("The action bar a hero of this class gets. Its spells from slot 1 on (up to 4) are shown in the class window; " +
                 "slot 9 is the race's racial.")]
        [SerializeField] ActionBarLoadout loadout;

        public HeroClassId Id => id;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public string Description => description;
        public float MaxHealth => maxHealth;
        public float MaxMana => maxMana;
        public float ManaRegen => manaRegen;
        public ActionBarLoadout Loadout => loadout;
        public float ThreatMultiplier => threatMultiplier;
        public string PassiveName => passiveName;
        public string PassiveDescription => passiveDescription;

        /// <summary>Builds a class in code, for tests and tools.</summary>
        internal static ClassDefinition Create(HeroClassId id, string displayName, float maxHealth = 100f, float maxMana = 100f,
            float manaRegen = 10f, ActionBarLoadout loadout = null, float threatMultiplier = 1f)
        {
            var definition = CreateInstance<ClassDefinition>();
            definition.name = displayName;
            definition.id = id;
            definition.displayName = displayName;
            definition.maxHealth = maxHealth;
            definition.maxMana = maxMana;
            definition.manaRegen = manaRegen;
            definition.loadout = loadout;
            definition.threatMultiplier = threatMultiplier;
            return definition;
        }
    }
}
