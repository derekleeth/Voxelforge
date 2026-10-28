using Unity.Mathematics;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// One local player's camera. <see cref="SplitScreenDirector"/> makes one per slot by copying the overview camera
    /// (so every view shares its render settings), and each frame tells it where to look and where to draw: straight
    /// to the screen, to a rectangle of it, or into a texture for the Voronoi composite.
    /// <para>
    /// The camera never goes inside a hill, and never loses its player behind one. It keeps a clear line from a
    /// <i>pivot</i> that is always in open air (the player's head, or the middle of a shared view when both players
    /// can see it) to where it wants to be. If blocks are in the way, it slides in along that line right away,
    /// then eases back out once the view is clear. The line is checked at the centre and at the 4 corners of the
    /// camera's near clip plane, so the edges of the view can't cut into a wall either.
    /// </para>
    /// <para>
    /// The look-at point itself may sit inside a hill: split-screen moves it towards the other player so each
    /// player is centred on their own side of the line (<see cref="SplitScreenMath.SplitFocus"/>). That's why the
    /// line starts at the pivot, not at the look-at point.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class PlayerCameraRig : MonoBehaviour
    {
        /// <summary>How far in front of a block the camera stops.</summary>
        const float WallGap = 0.3f;
        /// <summary>Closest the camera gets to its pivot, even when squeezed against a wall.</summary>
        const float MinDistance = 0.2f;
        /// <summary>Extra room around the near clip plane's corners.</summary>
        const float NearPlaneMargin = 1.25f;
        /// <summary>How quickly the camera eases back out after the view clears (per second).</summary>
        const float EaseOutRate = 4f;

        Camera _camera;
        RenderTexture _texture;
        float _clearFraction = 1f; // How far from the pivot to the wanted spot the camera may go, 0-1.

        public byte LocalSlot { get; private set; }

        public Camera Camera => _camera;

        /// <summary>What this camera drew this frame, while drawing into a texture. Null otherwise.</summary>
        public RenderTexture Texture => _camera.targetTexture;

        /// <summary>The player's own view angle (degrees). While two players are close, the shared angle wins instead.</summary>
        public float OwnYaw { get; set; }

        /// <summary>The player's own camera tilt (degrees down from level).</summary>
        public float OwnPitch { get; set; }

        internal void Initialize(byte localSlot, float yaw, float pitch)
        {
            _camera = GetComponent<Camera>();
            LocalSlot = localSlot;
            OwnYaw = yaw;
            OwnPitch = pitch;
            name = $"Player Camera (slot {localSlot})";
        }

        /// <summary>
        /// Orbits the camera <paramref name="distance"/> meters back from <paramref name="focus"/>, looking at it.
        /// With a <paramref name="world"/>, it slides towards <paramref name="pivot"/> when blocks are between the
        /// two. <paramref name="pivot"/> must be in open air: the player's head (see the class summary).
        /// </summary>
        public void SetPose(Vector3 focus, float yaw, float pitch, float distance, VoxelWorld world, Vector3 pivot)
        {
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 wanted = focus + rotation * Vector3.back * distance;
            float fraction = world != null ? ClearFraction(pivot, wanted, rotation, world) : 1f;

            // Snap in at once so the view never shows the inside of a hill, but ease back out.
            if (fraction < _clearFraction)
                _clearFraction = fraction;
            else
                _clearFraction = math.lerp(_clearFraction, fraction, 1f - math.exp(-EaseOutRate * Time.unscaledDeltaTime));
            transform.SetPositionAndRotation(Vector3.Lerp(pivot, wanted, _clearFraction), rotation);
        }

        /// <summary>
        /// How far along the line from <paramref name="pivot"/> to <paramref name="wanted"/> (0-1) the camera can go
        /// and still have its whole near clip plane in front of every visible block.
        /// </summary>
        float ClearFraction(float3 pivot, float3 wanted, Quaternion rotation, VoxelWorld world)
        {
            float length = math.distance(pivot, wanted);
            if (length < 1e-4f)
                return 1f;

            float halfHeight = _camera.nearClipPlane * math.tan(math.radians(_camera.fieldOfView) * 0.5f) * NearPlaneMargin;
            float3 up = rotation * Vector3.up * halfHeight;
            float3 right = rotation * Vector3.right * (halfHeight * _camera.aspect);
            float allowed = length;
            for (int corner = 0; corner < 5; corner++)
            {
                // The centre, then the 4 corners of the near plane at the wanted spot.
                float3 offset = corner == 0 ? float3.zero : (corner % 2 == 0 ? right : -right) + (corner < 3 ? up : -up);
                float3 ray = wanted + offset - pivot;
                float rayLength = math.length(ray);
                if (VoxelQueries.Raycast(world, pivot, ray, rayLength + WallGap, out VoxelHit hit, opaqueOnly: true))
                    allowed = math.min(allowed, (hit.Distance - WallGap) * length / rayLength);
            }
            return math.saturate(math.max(allowed, math.min(MinDistance, length)) / length);
        }

        /// <summary>Draws straight to the screen, in <paramref name="rect"/> (0-1, origin bottom-left).</summary>
        public void ShowOnScreen(Rect rect)
        {
            _camera.targetTexture = null;
            _camera.rect = rect;
            _camera.enabled = true;
        }

        /// <summary>Draws the whole view into this rig's own texture, (re)made at the given size.</summary>
        public void ShowInTexture(int width, int height)
        {
            if (_texture == null || _texture.width != width || _texture.height != height)
            {
                ReleaseTexture();
                _texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { name = $"{name} View" };
                _texture.Create();
            }
            _camera.targetTexture = _texture;
            _camera.rect = new Rect(0f, 0f, 1f, 1f);
            _camera.enabled = true;
        }

        /// <summary>Stops drawing. The texture is kept, so walking back and forth across the merge distance doesn't remake it.</summary>
        public void Hide() => _camera.enabled = false;

        void ReleaseTexture()
        {
            if (_texture == null)
                return;
            if (_camera.targetTexture == _texture)
                _camera.targetTexture = null;
            _texture.Release();
            Destroy(_texture);
            _texture = null;
        }

        void OnDestroy() => ReleaseTexture();
    }
}
