using Unity.Mathematics;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Where things go on a north-up map (Roadmap 4.8): the minimap and the World Map both put north at the top, so a
    /// world column's z is the map's y. Map positions are in UI units from the map's centre.
    /// </summary>
    public static class MapMath
    {
        /// <summary>Where world column <paramref name="world"/> (x, z) lands on a map centred on <paramref name="center"/>.</summary>
        public static float2 ToMap(float2 world, float2 center, float unitsPerBlock) => (world - center) * unitsPerBlock;

        /// <summary>The world column (x, z) under map position <paramref name="map"/>.</summary>
        public static float2 ToWorld(float2 map, float2 center, float unitsPerBlock) => center + map / unitsPerBlock;

        /// <summary>
        /// Pulls <paramref name="position"/> back onto a circle of <paramref name="radius"/> if it's outside. True if it
        /// was, so the marker becomes an edge arrow pointing the way to go.
        /// </summary>
        public static bool ClampToCircle(ref float2 position, float radius)
        {
            float length = math.length(position);
            if (length <= radius)
                return false;
            position *= radius / length;
            return true;
        }

        /// <summary>
        /// Pulls <paramref name="position"/> back inside a rectangle of <paramref name="halfSize"/> along the line from
        /// the centre, so the arrow on the edge still points straight at it. True if it was outside.
        /// </summary>
        public static bool ClampToRect(ref float2 position, float2 halfSize)
        {
            float2 over = math.abs(position) / math.max(halfSize, 1e-4f);
            float most = math.cmax(over);
            if (most <= 1f)
                return false;
            position /= most;
            return true;
        }

        /// <summary>
        /// The UI rotation (degrees, anticlockwise) that turns an upward-pointing arrow to point along
        /// <paramref name="direction"/> (x right, y up).
        /// </summary>
        public static float ArrowRotation(float2 direction) =>
            math.lengthsq(direction) < 1e-8f ? 0f : math.degrees(math.atan2(-direction.x, direction.y));

        /// <summary>The UI rotation for a hero facing <paramref name="yaw"/> (degrees clockwise from north): UI turns anticlockwise.</summary>
        public static float HeadingRotation(float yaw) => -yaw;
    }
}
