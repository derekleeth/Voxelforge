using Unity.Mathematics;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Turns stick / WASD input into a world direction, relative to the way a player's camera faces, and turns
    /// heroes to face where they walk.
    /// </summary>
    public static class MoveDirection
    {
        /// <summary>
        /// Rotates <paramref name="move"/> (x = right, y = forward) by a camera yaw in degrees, measured clockwise from
        /// north (+Z) like <c>Transform.eulerAngles.y</c>. Returns the ground-plane direction (x, z).
        /// </summary>
        public static float2 ToWorld(float2 move, float yawDegrees)
        {
            math.sincos(math.radians(yawDegrees), out float sin, out float cos);
            return new float2(move.x * cos + move.y * sin, move.y * cos - move.x * sin);
        }

        /// <summary>The yaw of a ground-plane direction (x, z), in degrees clockwise from north. The inverse of <see cref="ToWorld"/>.</summary>
        public static float YawOf(float2 worldDirection) => math.degrees(math.atan2(worldDirection.x, worldDirection.y));

        /// <summary>Turns <paramref name="currentYaw"/> toward <paramref name="targetYaw"/> the short way round, by at most <paramref name="maxDegrees"/>.</summary>
        public static float TurnToward(float currentYaw, float targetYaw, float maxDegrees)
        {
            float delta = SplitScreenMath.DeltaAngle(currentYaw, targetYaw);
            return currentYaw + math.clamp(delta, -maxDegrees, maxDegrees);
        }
    }
}
