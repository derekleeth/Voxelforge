using System.Collections.Generic;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>Every <see cref="MobDefinition"/> in the game (Roadmap 5.2).</summary>
    [CreateAssetMenu(menuName = "Voxelforge/Mob Book", fileName = "MobBook")]
    public sealed class MobBook : ScriptableObject
    {
        /// <summary>The nav grid plans for bodies at most this many blocks tall (D-032).</summary>
        public const float MaxBodyHeight = 2f;

        [SerializeField] List<MobDefinition> mobs = new List<MobDefinition>();

        public IReadOnlyList<MobDefinition> Mobs => mobs;

        public bool TryGet(MobId id, out MobDefinition definition)
        {
            foreach (MobDefinition candidate in mobs)
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
            var seen = new HashSet<MobId>();
            for (int i = 0; i < mobs.Count; i++)
            {
                MobDefinition mob = mobs[i];
                if (mob == null)
                {
                    errors.Add($"Entry {i} is empty.");
                    continue;
                }
                if (mob.Id == MobId.None)
                    errors.Add($"'{mob.DisplayName}' has no ID.");
                else if (!seen.Add(mob.Id))
                    errors.Add($"'{mob.DisplayName}' uses ID {mob.Id}, which another mob already uses.");
                if (mob.Prefab == null)
                    errors.Add($"'{mob.DisplayName}' has no prefab.");
                if (mob.Motor.WalkSpeed <= 0f)
                    errors.Add($"'{mob.DisplayName}' can't walk (Walk Speed is 0).");
                if (mob.Motor.HalfExtents.y * 2f > MaxBodyHeight + 0.01f || mob.Motor.HalfExtents.x > 0.5f ||
                    mob.Motor.HalfExtents.z > 0.5f)
                    errors.Add($"'{mob.DisplayName}''s body is bigger than 1 x {MaxBodyHeight} x 1 blocks; the nav grid can't plan for it.");
                if (mob.SplitInto != null)
                {
                    if (mob.SplitCount <= 0)
                        errors.Add($"'{mob.DisplayName}' splits into nothing (Split Count is 0).");
                    if (SplitsInto(mob.SplitInto, mob))
                        errors.Add($"'{mob.DisplayName}' splits into itself, so it would never end.");
                    else if (!mobs.Contains(mob.SplitInto))
                        errors.Add($"'{mob.DisplayName}' splits into '{mob.SplitInto.DisplayName}', which isn't in the book.");
                }
                MobSpawnRule spawn = mob.Spawn;
                if (spawn.Place != MobSpawnPlace.Nowhere)
                {
                    if (spawn.GroupMin < 1 || spawn.GroupMax < spawn.GroupMin)
                        errors.Add($"'{mob.DisplayName}' spawns in groups of {spawn.GroupMin}-{spawn.GroupMax}; it needs 1 or more, smallest first.");
                    if (spawn.Weight <= 0f)
                        errors.Add($"'{mob.DisplayName}' has a spawn place but a weight of 0, so it never spawns.");
                }
                if (mob.Attacks.Length == 0)
                    errors.Add($"'{mob.DisplayName}' has no attacks.");
                foreach (MobAttack attack in mob.Attacks)
                {
                    if (attack.MinRange >= attack.Range)
                        errors.Add($"'{mob.DisplayName}' has a {attack.Kind} attack whose Min Range isn't under its Range.");
                    if (attack.Kind == MobAttackKind.Charge && attack.ChargeDistance < attack.Range)
                        errors.Add($"'{mob.DisplayName}''s charge stops before it reaches as far as it starts from.");
                }
            }
            return errors;
        }

        /// <summary>Whether a mob is only ever made by another one splitting (a Medium or Small slime).</summary>
        public bool IsSplitChild(MobDefinition definition)
        {
            foreach (MobDefinition mob in mobs)
            {
                if (mob != null && mob.SplitInto == definition)
                    return true;
            }
            return false;
        }

        // Whether splitting from `from` ever comes back round to `mob`.
        static bool SplitsInto(MobDefinition from, MobDefinition mob)
        {
            for (int depth = 0; from != null && depth < 16; depth++, from = from.SplitInto)
            {
                if (from == mob)
                    return true;
            }
            return from != null;
        }

        /// <summary>Builds a book in code, for tests and tools.</summary>
        internal static MobBook Create(IEnumerable<MobDefinition> definitions)
        {
            var book = CreateInstance<MobBook>();
            book.mobs = new List<MobDefinition>(definitions);
            return book;
        }
    }
}
