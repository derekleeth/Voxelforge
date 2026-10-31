using Unity.Mathematics;

namespace Voxelforge.Voxels
{
    /// <summary>A box that walks around the block grid. <see cref="Position"/> is the centre of the box.</summary>
    public struct VoxelBody
    {
        public float3 Position;
        public float3 Velocity;
        /// <summary>True while standing on a solid block.</summary>
        public bool Grounded;
        /// <summary>True while the chunk around the body isn't generated yet. The body is held still until it is.</summary>
        public bool WaitingForWorld;
        /// <summary>
        /// How far auto-stepping moved the body up (+) or down (-) during the last <see cref="VoxelBodyMotor.Step"/>.
        /// Visuals use it to glide over the step instead of popping.
        /// </summary>
        public float StepOffset;

        /// <summary>The world position of the feet (bottom centre of the box).</summary>
        public float3 FeetPosition(in MotorSettings settings) => Position - new float3(0f, settings.HalfExtents.y, 0f);
    }

    /// <summary>
    /// Moves a <see cref="VoxelBody"/> through the block grid: gravity, walking, jumping, auto-stepping and
    /// box-vs-block collision. Plain C# with no Unity physics, so the host can run it for every player and tests can check it.
    /// <para>
    /// Collision reads blocks straight from <see cref="VoxelWorld"/>, so a block edit changes what you can walk
    /// through on the very next tick, with no colliders to rebuild. Chunks that aren't generated yet count as
    /// solid, so nobody falls out of the world while terrain streams in. See DECISIONS.md D-007.
    /// </para>
    /// </summary>
    public static class VoxelBodyMotor
    {
        /// <summary>Gap kept between the box and a block it touches, so the next tick doesn't start inside it.</summary>
        const float Skin = 0.001f;

        /// <summary>Longest move in one sub-step. Under half a block, so a wall can't be skipped.</summary>
        const float MaxSubStep = 0.45f;

        public static void Step(ref VoxelBody body, in MotorInput input, in MotorSettings settings, VoxelWorld world, float dt)
        {
            body.StepOffset = 0f;
            body.WaitingForWorld = !world.TryGetChunk(VoxelMath.WorldToChunk(VoxelMath.WorldPositionToBlock(body.Position)), out _);
            if (body.WaitingForWorld)
            {
                body.Velocity = float3.zero;
                body.Grounded = false;
                return;
            }

            float2 move = input.Move;
            float moveLength = math.length(move);
            if (moveLength > 1f)
                move /= moveLength;

            float speed = input.Speed(settings);
            body.Velocity.x = move.x * speed;
            body.Velocity.z = move.y * speed;
            bool jumped = input.Jump && body.Grounded;
            if (jumped)
                body.Velocity.y = settings.JumpSpeed;
            // Only a body walking on the ground steps up or down; a jump or fall moves freely.
            float stepHeight = body.Grounded && !jumped ? settings.StepHeight : 0f;
            body.Velocity.y = math.max(body.Velocity.y - settings.Gravity * dt, -settings.MaxFallSpeed);

            float3 delta = body.Velocity * dt;
            int subSteps = math.max(1, (int)math.ceil(math.cmax(math.abs(delta)) / MaxSubStep));
            float3 stepDelta = delta / subSteps;

            body.Grounded = false;
            for (int i = 0; i < subSteps; i++)
            {
                // Vertical first, so landing is settled before sliding along walls.
                MoveAxis(ref body, 1, stepDelta.y, settings.HalfExtents, world);
                MoveSideways(ref body, 0, stepDelta.x, settings.HalfExtents, stepHeight, world);
                MoveSideways(ref body, 2, stepDelta.z, settings.HalfExtents, stepHeight, world);
            }

            if (stepHeight > 0f && !body.Grounded)
                SnapDown(ref body, settings.HalfExtents, stepHeight, world);
        }

        /// <summary>True if any solid (or not-yet-generated) block overlaps the box.</summary>
        public static bool Overlaps(float3 position, float3 halfExtents, VoxelWorld world)
        {
            int3 min = (int3)math.floor(position - halfExtents);
            int3 max = (int3)math.floor(position + halfExtents);
            for (int y = min.y; y <= max.y; y++)
            for (int z = min.z; z <= max.z; z++)
            for (int x = min.x; x <= max.x; x++)
            {
                if (IsSolid(new int3(x, y, z), world))
                    return true;
            }
            return false;
        }

        /// <summary>Blocks movement: a solid block, or any block in a chunk that isn't generated yet.</summary>
        public static bool IsSolid(int3 block, VoxelWorld world)
        {
            if (!world.TryGetChunk(VoxelMath.WorldToChunk(block), out VoxelChunk chunk))
                return true;

            ushort id = chunk.GetVoxel(VoxelMath.WorldToLocal(block)).BlockId;
            return id < world.Blocks.Length && world.Blocks[id].IsSolid;
        }

        /// <summary>
        /// Moves along x or z. If a block is in the way and its top is no more than <paramref name="stepHeight"/>
        /// above the feet, and there is head room above it, the body climbs onto it instead of stopping.
        /// </summary>
        static void MoveSideways(ref VoxelBody body, int axis, float delta, float3 halfExtents, float stepHeight, VoxelWorld world)
        {
            if (delta == 0f)
                return;

            float3 moved = body.Position;
            moved[axis] += delta;
            if (stepHeight > 0f && Overlaps(moved, halfExtents, world))
            {
                float feet = body.Position.y - halfExtents.y;
                for (float top = math.floor(feet) + 1f; top - feet <= stepHeight + 2f * Skin; top++)
                {
                    float raisedY = top + halfExtents.y + Skin;
                    float3 raised = moved;
                    raised.y = raisedY;
                    float3 lifted = body.Position;
                    lifted.y = raisedY;
                    // Room to rise where we stand, and room to stand on the step.
                    if (Overlaps(lifted, halfExtents, world) || Overlaps(raised, halfExtents, world))
                        continue;

                    body.StepOffset += raisedY - body.Position.y;
                    body.Position = raised;
                    body.Velocity.y = math.max(body.Velocity.y, 0f);
                    body.Grounded = true;
                    return;
                }
            }

            MoveAxis(ref body, axis, delta, halfExtents, world);
        }

        /// <summary>
        /// Keeps a walking body on the ground down a step: if there's floor within <paramref name="stepHeight"/>
        /// below, drop onto it now rather than falling. Stairs then never flicker into a fall.
        /// </summary>
        static void SnapDown(ref VoxelBody body, float3 halfExtents, float stepHeight, VoxelWorld world)
        {
            VoxelBody probe = body;
            float remaining = stepHeight + 2f * Skin;
            while (remaining > 0f && !probe.Grounded)
            {
                float drop = math.min(remaining, MaxSubStep);
                MoveAxis(ref probe, 1, -drop, halfExtents, world);
                remaining -= drop;
            }
            if (!probe.Grounded)
                return;

            body.StepOffset += probe.Position.y - body.Position.y;
            body.Position = probe.Position;
            body.Velocity.y = 0f;
            body.Grounded = true;
        }

        static void MoveAxis(ref VoxelBody body, int axis, float delta, float3 halfExtents, VoxelWorld world)
        {
            if (delta == 0f)
                return;

            float3 position = body.Position;
            position[axis] += delta;
            if (!Overlaps(position, halfExtents, world))
            {
                body.Position = position;
                return;
            }

            // Sub-steps are shorter than a block, so the box hit the layer of blocks right in front of it.
            // Stop just short of that layer.
            if (delta > 0f)
                position[axis] = math.floor(position[axis] + halfExtents[axis]) - halfExtents[axis] - Skin;
            else
                position[axis] = math.floor(position[axis] - halfExtents[axis]) + 1f + halfExtents[axis] + Skin;

            body.Position = position;
            float3 velocity = body.Velocity;
            velocity[axis] = 0f;
            body.Velocity = velocity;
            if (axis == 1 && delta < 0f)
                body.Grounded = true;
        }
    }
}
