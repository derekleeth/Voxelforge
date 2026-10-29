using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// One spell or ability template (Roadmap 4.1): what it costs, how long it takes, what it does. The host runs it
    /// (<see cref="AbilityCaster"/>, <see cref="PlayerCaster"/>), so these numbers are only ever trusted on the host.
    /// Only rules live here (Roadmap 4.2): how it looks and sounds is its <see cref="AbilityLook"/>.
    /// Create one with Assets &gt; Create &gt; Voxelforge &gt; Ability, then add it to the <see cref="AbilityBook"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxelforge/Ability", fileName = "NewAbility")]
    public sealed class AbilityDefinition : ScriptableObject
    {
        [Tooltip("Permanent ID sent over the network (AbilityIds). Never change or reuse one. 0 = none.")]
        [SerializeField] ushort id;

        [SerializeField] string displayName;

        [SerializeField, TextArea] string description;

        [SerializeField] AbilityElement element = AbilityElement.Arcane;

        [Header("Cost & timing")]
        [Tooltip("Mana spent when the cast finishes.")]
        [SerializeField, Min(0f)] float manaCost;

        [Tooltip("Seconds before it can be cast again, counted from when the cast finishes.")]
        [SerializeField, Min(0f)] float cooldown;

        [Tooltip("Seconds of casting before it goes off. 0 = instant.")]
        [SerializeField, Min(0f)] float castTime;

        [Tooltip("Off = moving or jumping cancels the cast. Instant abilities ignore this.")]
        [SerializeField] bool castWhileMoving;

        [Header("Reach")]
        [Tooltip("How far it reaches, in metres. A projectile fizzles out here.")]
        [SerializeField, Min(0f)] float range;

        [Tooltip("Radius of the crater it blasts where it lands, in metres (Roadmap 4.3). 0 = only the block it hits.")]
        [SerializeField, Min(0f)] float aoeRadius;

        [Header("Crater (4.3)")]
        [Tooltip("How much of the block damage is left at the crater's edge (0-1). Low = soft blocks carve wide but " +
                 "hard ones only near the middle.")]
        [SerializeField, Range(0f, 1f)] float craterFalloff = 0.25f;

        [Tooltip("How deep the crater is for its width. 1 = a round bowl, 0.4 = wide and shallow.")]
        [SerializeField, Range(0.2f, 1f)] float craterFlatten = 1f;

        [Header("Effect")]
        [SerializeField] AbilityDelivery delivery;

        [Tooltip("How fast a projectile flies, in metres per second.")]
        [SerializeField, Min(0f)] float projectileSpeed;

        [Tooltip("Block damage per hit, like mining damage (Stone has 60 durability). In a crater, this is at the middle.")]
        [SerializeField, Min(0f)] float blockDamage;

        [Tooltip("Damage to creatures. Nothing has health until Phase 5, so it's only kept for now.")]
        [SerializeField, Min(0f)] float creatureDamage;

        public ushort Id => id;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public string Description => description;
        public AbilityElement Element => element;
        public float ManaCost => manaCost;
        public float Cooldown => cooldown;
        public float CastTime => castTime;
        public bool IsInstant => castTime <= 0f;
        public bool CastWhileMoving => castWhileMoving;
        public float Range => range;
        public float AoeRadius => aoeRadius;
        public float CraterFalloff => craterFalloff;
        public float CraterFlatten => craterFlatten;
        public AbilityDelivery Delivery => delivery;
        public float ProjectileSpeed => projectileSpeed;
        public float BlockDamage => blockDamage;
        public float CreatureDamage => creatureDamage;

        /// <summary>Builds a definition in code, for tests and tools.</summary>
        internal static AbilityDefinition Create(ushort id, string displayName, float manaCost = 0f, float cooldown = 0f,
            float castTime = 0f, float range = 24f, float projectileSpeed = 40f, float blockDamage = 45f, float aoeRadius = 0f,
            bool castWhileMoving = false, AbilityElement element = AbilityElement.Arcane,
            AbilityDelivery delivery = AbilityDelivery.Projectile, float craterFalloff = 0.25f, float craterFlatten = 1f)
        {
            var definition = CreateInstance<AbilityDefinition>();
            definition.name = displayName;
            definition.id = id;
            definition.displayName = displayName;
            definition.manaCost = manaCost;
            definition.cooldown = cooldown;
            definition.castTime = castTime;
            definition.range = range;
            definition.projectileSpeed = projectileSpeed;
            definition.blockDamage = blockDamage;
            definition.aoeRadius = aoeRadius;
            definition.castWhileMoving = castWhileMoving;
            definition.element = element;
            definition.delivery = delivery;
            definition.craterFalloff = craterFalloff;
            definition.craterFlatten = craterFlatten;
            return definition;
        }
    }
}
