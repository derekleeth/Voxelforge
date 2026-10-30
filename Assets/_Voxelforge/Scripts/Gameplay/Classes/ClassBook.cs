using System.Collections.Generic;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>Every <see cref="ClassDefinition"/> in the game (Roadmap 4.6), in the order the class window shows them.</summary>
    [CreateAssetMenu(menuName = "Voxelforge/Class Book", fileName = "ClassBook")]
    public sealed class ClassBook : ScriptableObject
    {
        [SerializeField] List<ClassDefinition> classes = new List<ClassDefinition>();

        public IReadOnlyList<ClassDefinition> Classes => classes;

        public bool TryGet(HeroClassId id, out ClassDefinition definition)
        {
            foreach (ClassDefinition candidate in classes)
            {
                if (candidate != null && candidate.Id == id)
                {
                    definition = candidate;
                    return true;
                }
            }
            definition = null;
            return false;
        }

        /// <summary>Returns every problem with the book. An empty list means it's valid.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            var seen = new HashSet<HeroClassId>();
            for (int i = 0; i < classes.Count; i++)
            {
                ClassDefinition definition = classes[i];
                if (definition == null)
                {
                    errors.Add($"Entry {i} is empty.");
                    continue;
                }
                if (!seen.Add(definition.Id))
                    errors.Add($"'{definition.DisplayName}' uses ID {definition.Id}, which another class already uses.");
                if (definition.Loadout == null)
                    errors.Add($"'{definition.DisplayName}' has no action bar loadout.");
            }
            if (!seen.Contains(HeroClassId.Adventurer))
                errors.Add("There's no Adventurer: new heroes need it.");
            return errors;
        }

        /// <summary>Builds a book in code, for tests and tools.</summary>
        internal static ClassBook Create(IEnumerable<ClassDefinition> definitions)
        {
            var book = CreateInstance<ClassBook>();
            book.classes = new List<ClassDefinition>(definitions);
            return book;
        }
    }
}
