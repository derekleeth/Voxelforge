using System.Collections.Generic;
using Unity.Mathematics;

namespace Voxelforge.Gameplay
{
    /// <summary>One block an ability wants to damage, and by how much.</summary>
    public struct VoxelDamageRequest
    {
        public int3 Block;
        public float Amount;
    }

    /// <summary>
    /// The host's damage math for an ability landing (Roadmap 4.2): what a hit should damage, worked out without
    /// touching the world or anything you can see or hear. <see cref="PlayerStaff"/> applies the requests through
    /// <c>VoxelWorldManager.RequestDamageVoxel</c> (CLAUDE.md Law #3) and tells every PC about the hit with an
    /// <see cref="AbilityCue"/>; how it looks and sounds is up to each PC (<see cref="AbilityLook"/>).
    /// <para>For now a hit damages the one block it struck. 4.3 adds craters here, using the ability's AoE radius.</para>
    /// </summary>
    public static class AbilityImpact
    {
        /// <summary>Adds what <paramref name="end"/> of a bolt of <paramref name="ability"/> damages to <paramref name="into"/>.</summary>
        public static void Resolve(AbilityDefinition ability, in ManaBoltEnd end, List<VoxelDamageRequest> into)
        {
            if (ability == null || !end.HitBlock || end.Damage <= 0f)
                return;
            into.Add(new VoxelDamageRequest { Block = end.Hit.Block, Amount = end.Damage });
        }
    }
}
