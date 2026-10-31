using Unity.Mathematics;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Where one player's World Map looks and how close (Roadmap 4.8). It opens on their hero and follows them until they
    /// pan away; recentring follows them again. Zoom steps through <see cref="ZoomLevels"/>. Plain data, so it's easy to test.
    /// </summary>
    public sealed class WorldMapModel
    {
        /// <summary>UI units (1080p pixels) per block, from furthest out to closest in.</summary>
        public static readonly float[] ZoomLevels = { 0.5f, 1f, 2f, 4f };

        /// <summary>The zoom the map opens at: about 1,000 blocks across a full-screen map.</summary>
        public const int DefaultZoom = 1;

        /// <summary>How fast a full push pans the map, in UI units per second, so it feels the same at every zoom.</summary>
        public const float PanSpeed = 700f;

        /// <summary>Pushes smaller than this don't pan, so a resting stick doesn't drift.</summary>
        public const float PanDeadzone = 0.2f;

        public int Zoom { get; private set; } = DefaultZoom;

        public float UnitsPerBlock => ZoomLevels[Zoom];

        /// <summary>The world column (x, z) at the middle of the map.</summary>
        public float2 Center { get; private set; }

        /// <summary>The map keeps the hero in the middle.</summary>
        public bool Following { get; private set; } = true;

        /// <summary>Opens on <paramref name="hero"/>, following them, at the default zoom.</summary>
        public void Open(float2 hero)
        {
            Zoom = DefaultZoom;
            Following = true;
            Center = hero;
        }

        /// <summary>
        /// One frame: pans by <paramref name="stick"/> (x east, y north) for <paramref name="dt"/> seconds, which stops
        /// following; otherwise a following map moves with <paramref name="hero"/>.
        /// </summary>
        public void Tick(float2 stick, float2 hero, float dt)
        {
            if (math.length(stick) > PanDeadzone)
            {
                Following = false;
                Center += stick * (PanSpeed * dt / UnitsPerBlock);
            }
            else if (Following)
            {
                Center = hero;
            }
        }

        /// <summary>Zooms in (+1) or out (-1) a step, keeping the centre. False if it was already as far as it goes.</summary>
        public bool ZoomBy(int steps)
        {
            int zoom = math.clamp(Zoom + steps, 0, ZoomLevels.Length - 1);
            if (zoom == Zoom)
                return false;
            Zoom = zoom;
            return true;
        }

        /// <summary>Back to the hero, following them again.</summary>
        public void Recenter(float2 hero)
        {
            Following = true;
            Center = hero;
        }
    }
}
