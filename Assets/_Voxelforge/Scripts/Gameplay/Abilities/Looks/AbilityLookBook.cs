using System.Collections.Generic;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Every <see cref="AbilityLook"/> (Roadmap 4.2), found by its ability's ID, with a fallback for abilities that don't
    /// have one yet. Only the views read it; the host's rules never do.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxelforge/Ability Look Book", fileName = "AbilityLookBook")]
    public sealed class AbilityLookBook : ScriptableObject
    {
        [Tooltip("For abilities that aren't listed below.")]
        [SerializeField] AbilityLook defaultLook;

        [SerializeField] List<AbilityLook> looks = new List<AbilityLook>();

        Dictionary<ushort, AbilityLook> _byId;

        public AbilityLook DefaultLook => defaultLook;

        /// <summary>The look for <paramref name="ability"/>, or the default one. Null only if the book has neither.</summary>
        public AbilityLook For(AbilityDefinition ability) =>
            ability != null && TryGet(ability.Id, out AbilityLook look) ? look : defaultLook;

        public bool TryGet(ushort abilityId, out AbilityLook look)
        {
            if (_byId == null)
            {
                _byId = new Dictionary<ushort, AbilityLook>();
                foreach (AbilityLook entry in looks)
                {
                    if (entry != null && entry.Ability != null && entry.Ability.Id != AbilityIds.None)
                        _byId.TryAdd(entry.Ability.Id, entry);
                }
            }
            return _byId.TryGetValue(abilityId, out look);
        }

        void OnValidate() => _byId = null;

        /// <summary>Builds a book in code, for tests and tools.</summary>
        internal static AbilityLookBook Create(AbilityLook defaultLook, IEnumerable<AbilityLook> looks)
        {
            var book = CreateInstance<AbilityLookBook>();
            book.defaultLook = defaultLook;
            book.looks = new List<AbilityLook>(looks);
            return book;
        }
    }
}
