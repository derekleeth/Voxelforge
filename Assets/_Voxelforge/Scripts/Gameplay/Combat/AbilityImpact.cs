using System.Collections.Generic;
using Unity.Mathematics;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// The host's damage math for an ability landing (Roadmap 4.2): what a hit should damage, worked out without
    /// touching the world or anything you can see or hear. <see cref="PlayerStaff"/> applies the requests through
    /// <c>VoxelWorldManager.RequestDamageVoxels</c> (CLAUDE.md Law #3) and tells every PC about the hit with an
    /// <see cref="AbilityCue"/>; how it looks and sounds is up to each PC (<see cref="AbilityLook"/>).
    /// <para>
    /// An ability with no AoE radius damages the one block it struck. One with a radius blasts a crater (Roadmap 4.3):
    /// every block whose middle is inside an ellipsoid around the hit point, squashed by
    /// <see cref="AbilityDefinition.CraterFlatten"/>, takes the full damage at the middle down to
    /// <see cref="AbilityDefinition.CraterFalloff"/> of it at the edge. Each block's durability then decides what breaks,
    /// so dirt carves wide and stone or ore only near the middle.
    /// </para>
    /// </summary>
    public static class AbilityImpact
    {
        /// <summary>Largest crater radius, in metres, so a mistyped asset can't ask for millions of blocks.</summary>
        public const float MaxRadius = 8f;

        /// <summary>Adds what <paramref name="end"/> of a bolt of <paramref name="ability"/> damages to <paramref name="into"/>.</summary>
        public static void Resolve(AbilityDefinition ability, in ManaBoltEnd end, List<VoxelDamageRequest> into)
        {
            if (end.HitBlock)
                Resolve(ability, end.Point, end.Hit.Block, end.Damage, into);
        }

        /// <summary>
        /// Adds what <paramref name="ability"/> damages when it lands at <paramref name="point"/> on
        /// <paramref name="hitBlock"/>, with <paramref name="damage"/> at the middle, to <paramref name="into"/>.
        /// </summary>
        public static void Resolve(AbilityDefinition ability, float3 point, int3 hitBlock, float damage,
            List<VoxelDamageRequest> into)
        {
            if (ability == null || !(damage > 0f))
                return;

            float radius = math.min(ability.AoeRadius, MaxRadius);
            if (radius <= 0f)
            {
                into.Add(new VoxelDamageRequest { Block = hitBlock, Amount = damage });
                return;
            }

            // The struck block is always in, even if the hit point is on its far corner.
            into.Add(new VoxelDamageRequest { Block = hitBlock, Amount = damage });
            float flatten = math.clamp(ability.CraterFlatten, 0.2f, 1f);
            float edge = math.saturate(ability.CraterFalloff);
            float height = radius * flatten;
            int3 min = (int3)math.floor(point - new float3(radius, height, radius));
            int3 max = (int3)math.floor(point + new float3(radius, height, radius));
            for (int y = min.y; y <= max.y; y++)
            {
                for (int z = min.z; z <= max.z; z++)
                {
                    for (int x = min.x; x <= max.x; x++)
                    {
                        var cell = new int3(x, y, z);
                        if (math.all(cell == hitBlock))
                            continue;
                        float3 offset = (float3)cell + 0.5f - point;
                        offset.y /= flatten;
                        float t = math.length(offset) / radius;
                        if (t > 1f)
                            continue;
                        into.Add(new VoxelDamageRequest { Block = cell, Amount = damage * math.lerp(1f, edge, t) });
                    }
                }
            }
        }
    }
}
