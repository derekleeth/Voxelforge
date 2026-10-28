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

        /// <summary>The world position of the feet (bottom centre of the box).</summary>
        public float3 FeetPosition(in MotorSettings settings) => Position - new float3(0f, settings.HalfExtents.y, 0f);
    }

    /// <summary>
    /// Moves a <see cref="VoxelBody"/> through the block grid: gravity, walking, jumping and box-vs-block
    /// collision. Plain C# with no Unity physics, so the host can run it for every player and tests can check it.
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

            body.Velocity.x = move.x * settings.WalkSpeed;
            body.Velocity.z = move.y * settings.WalkSpeed;
            if (input.Jump && body.Grounded)
                body.Velocity.y = settings.JumpSpeed;
            body.Velocity.y = math.max(body.Velocity.y - settings.Gravity * dt, -settings.MaxFallSpeed);

            float3 delta = body.Velocity * dt;
            int subSteps = math.max(1, (int)math.ceil(math.cmax(math.abs(delta)) / MaxSubStep));
            float3 stepDelta = delta / subSteps;

            body.Grounded = false;
            for (int i = 0; i < subSteps; i++)
            {
                // Vertical first, so landing is settled before sliding along walls.
                MoveAxis(ref body, 1, stepDelta.y, settings.HalfExtents, world);
                MoveAxis(ref body, 0, stepDelta.x, settings.HalfExtents, world);
                MoveAxis(ref body, 2, stepDelta.z, settings.HalfExtents, world);
            }
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
