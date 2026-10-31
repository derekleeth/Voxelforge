using System.Collections.Generic;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>Every <see cref="RaceDefinition"/> in the game (Roadmap 4.7), in the order the race window shows them.</summary>
    [CreateAssetMenu(menuName = "Voxelforge/Race Book", fileName = "RaceBook")]
    public sealed class RaceBook : ScriptableObject
    {
        [SerializeField] List<RaceDefinition> races = new List<RaceDefinition>();

        public IReadOnlyList<RaceDefinition> Races => races;

        public bool TryGet(RaceId id, out RaceDefinition definition)
        {
            foreach (RaceDefinition candidate in races)
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

        /// <summary>Where <paramref name="id"/> is in <see cref="Races"/>, or -1.</summary>
        public int IndexOf(RaceId id)
        {
            for (int i = 0; i < races.Count; i++)
            {
                if (races[i] != null && races[i].Id == id)
                    return i;
            }
            return -1;
        }

        /// <summary>Returns every problem with the book. An empty list means it's valid.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            var seen = new HashSet<RaceId>();
            for (int i = 0; i < races.Count; i++)
            {
                RaceDefinition definition = races[i];
                if (definition == null)
                {
                    errors.Add($"Entry {i} is empty.");
                    continue;
                }
                if (!seen.Add(definition.Id))
                    errors.Add($"'{definition.DisplayName}' uses ID {definition.Id}, which another race already uses.");
                if (definition.RacialAbility == null)
                    errors.Add($"'{definition.DisplayName}' has no racial ability.");
            }
            if (!seen.Contains(RaceId.Wayfarer))
                errors.Add("There's no Wayfarer: heroes who haven't chosen yet need it.");
            return errors;
        }

        /// <summary>Builds a book in code, for tests and tools.</summary>
        internal static RaceBook Create(IEnumerable<RaceDefinition> definitions)
        {
            var book = CreateInstance<RaceBook>();
            book.races = new List<RaceDefinition>(definitions);
            return book;
        }
    }
}
