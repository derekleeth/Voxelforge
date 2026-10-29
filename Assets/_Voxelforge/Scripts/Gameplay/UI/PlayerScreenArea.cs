using Unity.Mathematics;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Where one local player's part of the screen is, shared by the IMGUI <see cref="PlayerHud"/> and the uGUI
    /// <see cref="CombatHud"/> so they always agree (CLAUDE.md Law #5). Everything is in GUI pixels: origin top-left, y down.
    /// </summary>
    public static class PlayerScreenArea
    {
        /// <summary>
        /// The part of the screen that is this player's own: their rectangle in a grid, their side of the line in a
        /// Voronoi split, and in a merged view the half their hero stands in, so two HUDs never sit on top of each other.
        /// </summary>
        public static Rect Own(SplitMode mode, PlayerController player, PlayerView view)
        {
            Rect r = view.ScreenRect;
            var area = new Rect(r.x * Screen.width, (1f - r.y - r.height) * Screen.height, r.width * Screen.width, r.height * Screen.height);
            float2 towards; // Which way, on screen (y up), this player's half lies.
            if (mode == SplitMode.Voronoi && view.VoronoiSide != 0f)
                towards = view.VoronoiNormal * view.VoronoiSide;
            else if (mode == SplitMode.Merged && TryProject(view, player.VisualRoot.position, out Vector2 hero))
                towards = new float2(hero.x < area.center.x ? -1f : 1f, 0f);
            else
                return area;

            if (math.abs(towards.x) >= math.abs(towards.y))
                return towards.x < 0f
                    ? new Rect(area.x, area.y, area.width * 0.5f, area.height)
                    : new Rect(area.center.x, area.y, area.width * 0.5f, area.height);
            return towards.y > 0f
                ? new Rect(area.x, area.y, area.width, area.height * 0.5f)
                : new Rect(area.x, area.center.y, area.width, area.height * 0.5f);
        }

        /// <summary>Where a world point shows up in this player's view, in GUI pixels. False if it's behind the camera.</summary>
        public static bool TryProject(PlayerView view, float3 point, out Vector2 gui)
        {
            Vector3 viewport = view.Camera.WorldToViewportPoint(point);
            Rect rect = view.ScreenRect;
            gui = new Vector2((rect.x + viewport.x * rect.width) * Screen.width,
                (1f - (rect.y + viewport.y * rect.height)) * Screen.height);
            return viewport.z > 0f;
        }
    }
}
