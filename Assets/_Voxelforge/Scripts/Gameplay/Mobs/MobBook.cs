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

        /// <summary>Builds a book in code, for tests and tools.</summary>
        internal static MobBook Create(IEnumerable<MobDefinition> definitions)
        {
            var book = CreateInstance<MobBook>();
            book.mobs = new List<MobDefinition>(definitions);
            return book;
        }
    }
}
