using Unity.Mathematics;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>How the screen is shared out this frame.</summary>
    public enum SplitMode : byte
    {
        /// <summary>Nobody has joined yet: the overview camera shows the world.</summary>
        Overview,
        /// <summary>One player, one full-screen camera.</summary>
        Single,
        /// <summary>Two players close together: one shared camera on their midpoint.</summary>
        Merged,
        /// <summary>Two players far apart: an angled line splits the screen between them.</summary>
        Voronoi,
        /// <summary>Three or four players: a rectangle each.</summary>
        Grid,
    }

    /// <summary>The screen layout <see cref="SplitScreenDirector"/> picked this frame.</summary>
    public struct SplitLayout
    {
        public SplitMode Mode;

        /// <summary>Voronoi only: on-screen direction from the first player toward the second (aspect-corrected, length 1).</summary>
        public float2 SplitNormal;

        /// <summary>Voronoi only: how visible the divider line is, 0-1. Fades in just past the merge distance.</summary>
        public float DividerAlpha;

        /// <summary>Distance between the two players in Merged / Voronoi mode, in meters.</summary>
        public float Separation;
    }

    /// <summary>
    /// Where one local player's view is on screen. Per-player HUDs (Roadmap 4.4) anchor to this, never to the whole
    /// screen (CLAUDE.md Law #5).
    /// </summary>
    public struct PlayerView
    {
        public byte LocalSlot;

        /// <summary>The camera drawing this player's view. Both players share one camera while merged.</summary>
        public Camera Camera;

        /// <summary>The player's rectangle in normalized screen space (0-1, origin bottom-left). Full screen unless in Grid mode.</summary>
        public Rect ScreenRect;

        /// <summary>Voronoi only: the split line's normal, the same as <see cref="SplitLayout.SplitNormal"/>.</summary>
        public float2 VoronoiNormal;

        /// <summary>Voronoi only: -1 if this player is on the side behind the normal, +1 if in front, 0 otherwise.</summary>
        public float VoronoiSide;
    }
}
