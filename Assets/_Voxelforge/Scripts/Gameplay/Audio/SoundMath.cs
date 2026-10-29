using Unity.Mathematics;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>A slice of the stereo field, in pan units (-1 = left speaker, +1 = right).</summary>
    public readonly struct PanRegion
    {
        public readonly float Center;
        public readonly float HalfWidth;

        public PanRegion(float center, float halfWidth)
        {
            Center = center;
            HalfWidth = halfWidth;
        }

        public static PanRegion FullScreen => new PanRegion(0f, 1f);
    }

    /// <summary>
    /// The sums behind <see cref="SoundRouter"/> (Roadmap 3.9, D-022). Pure functions, so they can be tested without a
    /// scene. Every local player is an "ear": loudness comes from the nearest ear, and left/right from where the sound
    /// is inside that player's part of the screen.
    /// </summary>
    public static class SoundMath
    {
        /// <summary>
        /// How loud a sound <paramref name="distance"/> metres away is, 0-1: full inside <paramref name="minDistance"/>,
        /// then falling off smoothly to silence at <paramref name="maxDistance"/>.
        /// </summary>
        public static float Gain(float distance, float minDistance, float maxDistance)
        {
            if (distance <= minDistance)
                return 1f;
            if (distance >= maxDistance || maxDistance <= minDistance)
                return 0f;
            float t = (distance - minDistance) / (maxDistance - minDistance);
            return (1f - t) * (1f - t);
        }

        /// <summary>
        /// Which part of the stereo field belongs to <paramref name="view"/>: the whole of it for a full-screen view,
        /// the left half for the left half of the screen (Classic), and so on. In the Voronoi split the region leans
        /// to the side of the screen the player is on.
        /// </summary>
        public static PanRegion RegionFor(in PlayerView view)
        {
            if (view.VoronoiSide != 0f)
                return new PanRegion(view.VoronoiSide * view.VoronoiNormal.x * 0.5f, 0.5f);
            return new PanRegion(view.ScreenRect.center.x * 2f - 1f, view.ScreenRect.width);
        }

        /// <summary>
        /// Stereo pan (-1..1) for a sound at <paramref name="sound"/> heard by an ear at <paramref name="ear"/> whose
        /// camera's right is <paramref name="right"/>: hard right of the camera lands on the right edge of the region.
        /// </summary>
        public static float Pan(in PanRegion region, float3 ear, float3 right, float3 sound)
        {
            float3 offset = sound - ear;
            float length = math.length(offset);
            float side = length > 1e-4f ? math.dot(offset / length, math.normalizesafe(right)) : 0f;
            return math.clamp(region.Center + region.HalfWidth * side, -1f, 1f);
        }

        /// <summary>Pitch of a mining hit at crack <paramref name="stage"/> (0 to the last of the 8 stages).</summary>
        public static float HitPitch(int stage, float first, float last)
        {
            float t = math.saturate(stage / (float)(BlockCrackRenderer.StageCount - 1));
            return math.lerp(first, last, t);
        }
    }
}
