using System.Collections.Generic;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>Every <see cref="AbilityDefinition"/> in the game (Roadmap 4.1), looked up by ID.</summary>
    [CreateAssetMenu(menuName = "Voxelforge/Ability Book", fileName = "AbilityBook")]
    public sealed class AbilityBook : ScriptableObject
    {
        [SerializeField] List<AbilityDefinition> abilities = new List<AbilityDefinition>();

        Dictionary<ushort, AbilityDefinition> _byId;

        public IReadOnlyList<AbilityDefinition> Abilities => abilities;

        public bool TryGet(ushort id, out AbilityDefinition ability)
        {
            if (_byId == null)
            {
                _byId = new Dictionary<ushort, AbilityDefinition>();
                foreach (AbilityDefinition definition in abilities)
                {
                    if (definition != null && definition.Id != AbilityIds.None)
                        _byId.TryAdd(definition.Id, definition);
                }
            }
            return _byId.TryGetValue(id, out ability);
        }

        /// <summary>Returns every problem with the book. An empty list means it's valid.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            var seen = new HashSet<ushort>();
            for (int i = 0; i < abilities.Count; i++)
            {
                AbilityDefinition ability = abilities[i];
                if (ability == null)
                {
                    errors.Add($"Entry {i} is empty.");
                    continue;
                }
                errors.AddRange(Check(ability));
                if (ability.Id != AbilityIds.None && !seen.Add(ability.Id))
                    errors.Add($"'{ability.DisplayName}' uses ID {ability.Id}, which another ability already uses.");
            }
            return errors;
        }

        /// <summary>Every problem with one ability, whether or not it's in a book (items check theirs too).</summary>
        public static List<string> Check(AbilityDefinition ability)
        {
            var errors = new List<string>();
            string name = ability.DisplayName;
            if (ability.Id == AbilityIds.None)
                errors.Add($"'{name}' has no ID (0 means none).");
            if (ability.ManaCost < 0f || ability.Cooldown < 0f || ability.CastTime < 0f || ability.AoeRadius < 0f ||
                ability.BlockDamage < 0f || ability.CreatureDamage < 0f)
                errors.Add($"'{name}' has a negative number.");
            if (ability.Delivery == AbilityDelivery.Projectile && !(ability.ProjectileSpeed > 0f && ability.Range > 0f))
                errors.Add($"'{name}' is a projectile, so it needs a speed and range above 0.");
            if (ability.NeedsAimedBlock && ability.Range <= 0f)
                errors.Add($"'{name}' goes off where the caster aims, so it needs a range above 0.");
            if ((ability.Delivery == AbilityDelivery.Zone || ability.Delivery == AbilityDelivery.Totem) &&
                !(ability.Duration > 0f && ability.AoeRadius > 0f))
                errors.Add($"'{name}' leaves a zone, so it needs a duration and radius above 0.");
            if (ability.Delivery == AbilityDelivery.Quake && ability.AoeRadius <= 0f)
                errors.Add($"'{name}' is a stomp, so it needs a radius above 0.");
            if (ability.Delivery == AbilityDelivery.SelfHeal && !(ability.HealAmount > 0f && ability.HealAmount <= 1f))
                errors.Add($"'{name}' heals a share of max health, so its heal amount must be above 0 and at most 1.");
            return errors;
        }

        void OnValidate() => _byId = null;

        /// <summary>Builds a book in code, for tests and tools.</summary>
        internal static AbilityBook Create(IEnumerable<AbilityDefinition> definitions)
        {
            var book = CreateInstance<AbilityBook>();
            book.abilities = new List<AbilityDefinition>(definitions);
            return book;
        }
    }
}
