using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// One playable race (Roadmap 4.7): what it adds to a hero's pools, its racial passive and its racial ability, which
    /// always sits in action bar slot 9. The host applies it (<see cref="PlayerRace"/>, <see cref="PlayerClass"/>), so these
    /// numbers are only ever trusted on the host. Only rules live here (Roadmap 4.2): its body, crest and portrait are in
    /// its <see cref="RaceLook"/>. Create one with Assets &gt; Create &gt; Voxelforge &gt; Race, then add it to the
    /// <see cref="RaceBook"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxelforge/Race", fileName = "NewRace")]
    public sealed class RaceDefinition : ScriptableObject
    {
        /// <summary>The action bar slot (0-8) every race's racial ability goes in: slot 9 on screen.</summary>
        public const int RacialSlot = PlayerActionBar.SlotCount - 1;

        [Tooltip("Permanent ID saved in profiles and sent over the network (RaceId). Never change or reuse one.")]
        [SerializeField] RaceId id;

        [SerializeField] string displayName;

        [Tooltip("What the race window says about them: a little lore, for a player choosing.")]
        [SerializeField, TextArea] string description;

        [Tooltip("Where they come from, shown on their card (a biome's name).")]
        [SerializeField] string homeland;

        [Header("Added to the class's pools")]
        [SerializeField] float bonusHealth;

        [SerializeField] float bonusMana;

        [Tooltip("Extra mana back per second.")]
        [SerializeField] float bonusManaRegen;

        [Header("Racials")]
        [Tooltip("Always in action bar slot 9, whatever the class.")]
        [SerializeField] AbilityDefinition racialAbility;

        [SerializeField] RacialPassive passive;

        [SerializeField] string passiveName;

        [Tooltip("What the race window says the passive does.")]
        [SerializeField, TextArea] string passiveDescription;

        [Tooltip("How strong the passive is; each kind says what it means (RacialPassive).")]
        [SerializeField] float passiveValue;

        public RaceId Id => id;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public string Description => description;
        public string Homeland => homeland;
        public float BonusHealth => bonusHealth;
        public float BonusMana => bonusMana;
        public float BonusManaRegen => bonusManaRegen;
        public AbilityDefinition RacialAbility => racialAbility;
        public RacialPassive Passive => passive;
        public string PassiveName => passiveName;
        public string PassiveDescription => passiveDescription;
        public float PassiveValue => passiveValue;

        /// <summary>Builds a race in code, for tests and tools.</summary>
        internal static RaceDefinition Create(RaceId id, string displayName, float bonusHealth = 0f, float bonusMana = 0f,
            float bonusManaRegen = 0f, AbilityDefinition racialAbility = null, RacialPassive passive = RacialPassive.None,
            float passiveValue = 0f)
        {
            var definition = CreateInstance<RaceDefinition>();
            definition.name = displayName;
            definition.id = id;
            definition.displayName = displayName;
            definition.bonusHealth = bonusHealth;
            definition.bonusMana = bonusMana;
            definition.bonusManaRegen = bonusManaRegen;
            definition.racialAbility = racialAbility;
            definition.passive = passive;
            definition.passiveValue = passiveValue;
            return definition;
        }
    }
}
