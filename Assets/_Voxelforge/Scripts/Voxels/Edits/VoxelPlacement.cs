using System.Collections.Generic;
using Unity.Mathematics;

namespace Voxelforge.Voxels
{
    /// <summary>A character's collision box, centred on <see cref="Center"/>.</summary>
    public readonly struct BodyBounds
    {
        public readonly float3 Center;
        public readonly float3 HalfExtents;

        public BodyBounds(float3 center, float3 halfExtents)
        {
            Center = center;
            HalfExtents = halfExtents;
        }

        /// <summary>The box of a character whose feet are at <paramref name="feet"/>.</summary>
        public static BodyBounds FromFeet(float3 feet, float3 halfExtents) =>
            new BodyBounds(feet + new float3(0f, halfExtents.y, 0f), halfExtents);

        // Overlaps thinner than this don't count, so float rounding never turns "touching" into "inside".
        const float Skin = 0.001f;

        /// <summary>True if this box and block <paramref name="cell"/> overlap. Boxes that only touch don't.</summary>
        public bool Overlaps(int3 cell)
        {
            float3 half = HalfExtents - Skin;
            return math.all(Center + half > cell) & math.all(Center - half < (float3)cell + 1f);
        }
    }

    /// <summary>
    /// Where a block may go (Roadmap 3.3, D-016). A block snaps to the open cell against the face you aim at, and only
    /// goes there if the cell is empty and no character stands in it. It doesn't change anything: placement still goes
    /// through <see cref="VoxelWorldManager.RequestModifyVoxel"/>. Plain C# so the host, the ghost-block preview and
    /// tests all use the same rules.
    /// </summary>
    public static class VoxelPlacement
    {
        /// <summary>
        /// Can a block go against the face of <paramref name="hit"/>, without overlapping any of <paramref name="bodies"/>?
        /// </summary>
        /// <param name="cell">The snapped cell, <c>hit.Block + hit.Normal</c>, even when the answer is no.</param>
        public static PlacementResult Check(VoxelWorld world, VoxelHit hit, IReadOnlyList<BodyBounds> bodies, out int3 cell) =>
            Check(world, hit, BlockIds.Air, bodies, out cell);

        /// <summary>
        /// Can <paramref name="blockId"/> go against the face of <paramref name="hit"/>? Like the check above, plus the
        /// block's own rules (Roadmap 3.5): one that needs support needs a solid block below or beside the cell, and
        /// one you can walk through (a torch) may go where a character stands. <see cref="BlockIds.Air"/> = any solid block.
        /// </summary>
        public static PlacementResult Check(VoxelWorld world, VoxelHit hit, ushort blockId, IReadOnlyList<BodyBounds> bodies,
            out int3 cell)
        {
            cell = hit.Block + hit.Normal;
            if (math.all(hit.Normal == 0))
                return PlacementResult.NoFace;
            if (!world.TryGetChunk(VoxelMath.WorldToChunk(cell), out _))
                return PlacementResult.ChunkNotLoaded;
            if (!world.GetVoxel(cell).IsAir)
                return PlacementResult.Occupied;

            bool known = blockId != BlockIds.Air && blockId < world.Blocks.Length;
            if (known && world.Blocks[blockId].NeedsSupport && !VoxelSupport.HasSupport(world, cell))
                return PlacementResult.NoSupport;
            if (known && !world.Blocks[blockId].IsSolid)
                return PlacementResult.Ok;

            if (bodies != null)
            {
                for (int i = 0; i < bodies.Count; i++)
                {
                    if (bodies[i].Overlaps(cell))
                        return PlacementResult.BlocksCharacter;
                }
            }
            return PlacementResult.Ok;
        }
    }
}
