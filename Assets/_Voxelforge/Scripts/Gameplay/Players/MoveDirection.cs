using Unity.Mathematics;

namespace Voxelforge.Gameplay
{
    /// <summary>Turns stick / WASD input into a world direction, relative to the way a player's camera faces.</summary>
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
    }
}
