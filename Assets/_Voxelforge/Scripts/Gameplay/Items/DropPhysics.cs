using Unity.Mathematics;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// How things lying in the world fall and settle on blocks (Roadmap 3.2): block drops and, since 4.5, loot bags. A
    /// small box of half-size <c>half</c>: gravity, stopping on solid blocks one axis at a time, sliding to a stop, and
    /// popping up out of a block placed on top of it. Host only; plain C# so tests can drive it.
    /// </summary>
    public static class DropPhysics
    {
        /// <summary>Moves one fixed step. Returns true once it lies still on the ground.</summary>
        public static bool Step(VoxelWorld world, ref float3 position, ref float3 velocity, float half, float gravity, float dt)
        {
            // Pushed out of a block placed on top of it.
            if (IsSolid(world, position))
            {
                position.y = math.floor(position.y) + 1f + half;
                velocity = float3.zero;
            }

            velocity.y -= gravity * dt;
            bool grounded = false;
            for (int axis = 0; axis < 3; axis++)
            {
                float step = velocity[axis] * dt;
                if (step == 0f)
                    continue;
                float3 next = position;
                next[axis] += step;
                float3 edge = next;
                edge[axis] += math.sign(step) * half;
                if (IsSolid(world, edge))
                {
                    if (axis == 1 && step < 0f)
                    {
                        grounded = true;
                        next.y = math.floor(edge.y) + 1f + half; // Sit exactly on the block top.
                    }
                    else
                    {
                        next[axis] = position[axis];
                    }
                    velocity[axis] = 0f;
                }
                position = next;
            }

            if (grounded)
                velocity.xz *= 0.8f; // Slide to a stop.
            return grounded && math.lengthsq(velocity.xz) < 0.01f;
        }

        static bool IsSolid(VoxelWorld world, float3 point) => VoxelBodyMotor.IsSolid((int3)math.floor(point), world);
    }
}
