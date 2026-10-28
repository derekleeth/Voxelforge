using Unity.Mathematics;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// The math behind the Voronoi split-screen (Roadmap 2.3). Plain functions, so it's all unit-tested.
    /// <para>
    /// The key idea: while split, each camera looks at a point between its player and the midpoint, at most half the
    /// merge distance away from its player. Exactly at the merge distance that point <i>is</i> the midpoint, so both
    /// cameras see the same picture as the shared camera and the split appears with no jump.
    /// </para>
    /// </summary>
    public static class SplitScreenMath
    {
        /// <summary>True when two players are far enough apart to need their own halves of the screen.</summary>
        public static bool IsSplit(float separation, float mergeDistance) => separation > mergeDistance;

        /// <summary>Where the camera of the player at <paramref name="self"/> looks while split from <paramref name="other"/>.</summary>
        public static float3 FocusTarget(float3 self, float3 other, float mergeDistance)
        {
            float3 toMidpoint = (other - self) * 0.5f;
            float halfSeparation = math.length(toMidpoint);
            float halfMerge = mergeDistance * 0.5f;
            if (halfSeparation <= halfMerge)
                return self + toMidpoint;
            return self + toMidpoint * (halfMerge / halfSeparation);
        }

        /// <summary>
        /// Where a split camera looks so its player shows up in the middle of their own side of the line, whichever way
        /// that camera has been turned. <paramref name="ownSide"/> is the on-screen direction (length 1) from the line
        /// toward this player's side. <paramref name="cameraRight"/> / <paramref name="cameraUp"/> are this camera's axes.
        /// <paramref name="screenOffset"/> is how far (meters, at the focus) the player sits from the screen center.
        /// <para>
        /// At <paramref name="freedom"/> 0 this is <see cref="FocusTarget"/>, so the split stays seamless. At 1 it's
        /// purely on-screen, so a player who turns their camera right round still sees their hero on their own side.
        /// </para>
        /// </summary>
        public static float3 SplitFocus(float3 self, float3 other, float mergeDistance, float2 ownSide,
            float3 cameraRight, float3 cameraUp, float screenOffset, float freedom)
        {
            float3 world = FocusTarget(self, other, mergeDistance);
            float3 onScreen = self - (ownSide.x * cameraRight + ownSide.y * cameraUp) * screenOffset;
            return math.lerp(world, onScreen, math.saturate(freedom));
        }

        /// <summary>
        /// How far (meters, at the focus) from the screen center to its edge, going in on-screen direction
        /// <paramref name="direction"/> (length 1). <paramref name="halfWidth"/> / <paramref name="halfHeight"/> are half
        /// the view's size at the focus distance.
        /// </summary>
        public static float ScreenEdgeDistance(float2 direction, float halfWidth, float halfHeight)
        {
            float2 reach = math.abs(direction) / new float2(halfWidth, halfHeight);
            return 1f / math.max(math.cmax(reach), 1e-6f);
        }

        /// <summary>
        /// How far the camera sits from its focus point. The shared camera pulls back as the players spread out, so both
        /// stay in frame; once split, every camera keeps the distance it had at the merge point.
        /// </summary>
        public static float ViewDistance(float baseDistance, float pullBackPerMeter, float separation, float mergeDistance) =>
            baseDistance + math.min(separation, mergeDistance) * pullBackPerMeter;

        /// <summary>
        /// How freely each player can turn their own view: 0 at the merge distance (both views locked to the shared
        /// angle) up to 1 at <paramref name="mergeDistance"/> + <paramref name="blendRange"/>.
        /// </summary>
        public static float LookFreedom(float separation, float mergeDistance, float blendRange) =>
            math.smoothstep(mergeDistance, mergeDistance + math.max(blendRange, 0.001f), separation);

        /// <summary>The shortest turn from angle <paramref name="from"/> to <paramref name="to"/>, in degrees (-180 to 180).</summary>
        public static float DeltaAngle(float from, float to)
        {
            float delta = math.fmod(to - from, 360f);
            if (delta > 180f)
                delta -= 360f;
            else if (delta < -180f)
                delta += 360f;
            return delta;
        }

        /// <summary>A view angle part-way from the shared angle to a player's own angle, turning the short way round.</summary>
        public static float BlendAngle(float shared, float own, float freedom) => shared + DeltaAngle(shared, own) * freedom;

        /// <summary>The angle halfway between two angles, the short way round.</summary>
        public static float MidAngle(float a, float b) => a + DeltaAngle(a, b) * 0.5f;

        /// <summary>
        /// The on-screen direction from the player at <paramref name="first"/> to the one at <paramref name="second"/>,
        /// seen by a camera with the given right / up axes. Screen x and y use the same units (aspect-corrected), so
        /// the split line drawn perpendicular to it is a true perpendicular on screen.
        /// </summary>
        public static float2 SplitNormal(float3 first, float3 second, float3 cameraRight, float3 cameraUp)
        {
            float3 separation = second - first;
            float2 onScreen = new float2(math.dot(separation, cameraRight), math.dot(separation, cameraUp));
            float length = math.length(onScreen);
            return length > 1e-4f ? onScreen / length : new float2(1f, 0f);
        }

        /// <summary>
        /// Which side of the split line a screen point is on: negative = the first player's side, positive = the second's.
        /// <paramref name="uv"/> is 0-1 across the screen and <paramref name="aspect"/> is width / height.
        /// The composite shader does the same test per pixel.
        /// </summary>
        public static float SideOfSplit(float2 uv, float2 splitNormal, float aspect) =>
            math.dot((uv - 0.5f) * new float2(aspect, 1f), splitNormal);

        /// <summary>
        /// Grid mode: the screen rectangle (0-1, origin bottom-left) for grid cell <paramref name="index"/>.
        /// One player gets the whole screen. More than one gets a quarter each, reading order: top-left, top-right,
        /// bottom-left, bottom-right.
        /// </summary>
        public static Rect GridRect(int index, int playerCount)
        {
            if (playerCount <= 1)
                return new Rect(0f, 0f, 1f, 1f);
            float x = index % 2 == 0 ? 0f : 0.5f;
            float y = index < 2 ? 0.5f : 0f;
            return new Rect(x, y, 0.5f, 0.5f);
        }
    }
}
