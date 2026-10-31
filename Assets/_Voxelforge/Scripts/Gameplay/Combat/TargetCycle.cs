using System.Collections.Generic;
using Unity.Mathematics;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Which creature a Target press picks (Roadmap 5.2): the nearest one in range, and on each press after that the next
    /// one further away, wrapping round to the nearest. Plain C#, so it's tested without a scene; <see cref="PlayerTargeting"/>
    /// uses it on the host.
    /// </summary>
    public static class TargetCycle
    {
        /// <summary>
        /// The creature to target after <paramref name="current"/> (0 for none) from <paramref name="from"/>: creatures
        /// that can be hit within <paramref name="range"/> metres, nearest first. Returns 0 if none is in range.
        /// <paramref name="scratch"/> is reused so nothing is made per press.
        /// </summary>
        public static ulong Next(IReadOnlyList<ICombatCreature> creatures, float3 from, float range, ulong current,
            List<(ulong id, float distance)> scratch)
        {
            scratch.Clear();
            if (creatures == null)
                return 0;
            foreach (ICombatCreature creature in creatures)
            {
                if (creature == null || !creature.IsTargetable)
                    continue;
                float distance = math.distance(creature.Feet, from);
                if (distance <= range)
                    scratch.Add((creature.CreatureId, distance));
            }
            if (scratch.Count == 0)
                return 0;
            // Nearest first; ties by ID, so the order is the same press after press.
            scratch.Sort((a, b) => a.distance != b.distance ? a.distance.CompareTo(b.distance) : a.id.CompareTo(b.id));
            for (int i = 0; i < scratch.Count; i++)
            {
                if (scratch[i].id == current)
                    return scratch[(i + 1) % scratch.Count].id;
            }
            return scratch[0].id;
        }
    }
}
