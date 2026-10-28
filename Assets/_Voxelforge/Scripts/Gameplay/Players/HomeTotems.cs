using Unity.Mathematics;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// The rules for Home Totems (Roadmap 3.8): whether one still stands, and where a hero lands when they hearth home.
    /// Plain C#, so it's tested without a scene; <see cref="PlayerHearth"/> uses it on the host.
    /// </summary>
    public static class HomeTotems
    {
        /// <summary>How high above the totem's base a landing spot may be searched for in its own column, as a last resort.</summary>
        const int ColumnSearch = 24;

        // The 8 cells around the totem, sides first: a hero lands beside it, facing it.
        static readonly int2[] Around =
        {
            new int2(0, -1), new int2(1, 0), new int2(0, 1), new int2(-1, 0),
            new int2(1, -1), new int2(1, 1), new int2(-1, 1), new int2(-1, -1),
        };

        /// <summary>
        /// Is a Home Totem at <paramref name="cell"/>? Read from the blocks when its chunk is loaded, otherwise from the
        /// <paramref name="deltas"/>: a totem is always a player's edit, so the log knows about far-away ones too.
        /// </summary>
        public static bool IsStanding(VoxelWorld world, VoxelDeltaLog deltas, int3 cell)
        {
            if (world != null && world.TryGetChunk(VoxelMath.WorldToChunk(cell), out _))
                return world.GetVoxel(cell).BlockId == BlockIds.HomeTotem;
            return deltas != null && deltas.TryGet(cell, out VoxelDelta delta) && delta.Current.BlockId == BlockIds.HomeTotem;
        }

        /// <summary>True once the blocks around the totem have generated, so a landing spot can be found.</summary>
        public static bool IsReady(VoxelWorld world, int3 totem)
        {
            if (world == null)
                return false;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dy = -1; dy <= 2; dy++)
                    {
                        if (!world.TryGetChunk(VoxelMath.WorldToChunk(totem + new int3(dx, dy, dz)), out _))
                            return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Where a hero with a body of <paramref name="halfExtents"/> lands beside the totem at <paramref name="totem"/>:
        /// the first of the 8 cells around it with room to stand and ground underneath, at the totem's level or a block
        /// above or below; failing that, on top of the totem's own column. <paramref name="yaw"/> faces the totem.
        /// </summary>
        public static bool TryFindLanding(VoxelWorld world, int3 totem, float3 halfExtents, out float3 feet, out float yaw)
        {
            feet = default;
            yaw = 0f;
            if (world == null)
                return false;

            foreach (int dy in new[] { 0, 1, -1 })
            {
                foreach (int2 offset in Around)
                {
                    var candidate = new float3(totem.x + offset.x + 0.5f, totem.y + dy, totem.z + offset.y + 0.5f);
                    if (CanStand(world, candidate, halfExtents))
                    {
                        feet = candidate;
                        yaw = math.degrees(math.atan2(-offset.x, -offset.y));
                        return true;
                    }
                }
            }

            // Walled in: stand on whatever is on top of the totem.
            for (int y = totem.y + 1; y <= totem.y + ColumnSearch; y++)
            {
                var candidate = new float3(totem.x + 0.5f, y, totem.z + 0.5f);
                if (CanStand(world, candidate, halfExtents))
                {
                    feet = candidate;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Room for the whole body at <paramref name="feet"/>, and something solid right under it.</summary>
        public static bool CanStand(VoxelWorld world, float3 feet, float3 halfExtents)
        {
            float3 centre = feet + new float3(0f, halfExtents.y + 0.001f, 0f);
            if (VoxelBodyMotor.Overlaps(centre, halfExtents, world))
                return false;
            var floor = new float3(halfExtents.x, 0.05f, halfExtents.z);
            return VoxelBodyMotor.Overlaps(feet - new float3(0f, 0.06f, 0f), floor, world);
        }
    }
}
