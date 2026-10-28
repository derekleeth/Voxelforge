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
    /// The camera never goes inside a hill: if blocks are between the focus and the camera, it moves in front of
    /// them right away, then eases back out once the view is clear.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class PlayerCameraRig : MonoBehaviour
    {
        /// <summary>How far in front of a block the camera stops.</summary>
        const float WallGap = 0.3f;
        /// <summary>Closest the camera gets to its focus, even when squeezed against a wall.</summary>
        const float MinDistance = 0.5f;
        /// <summary>How quickly the camera eases back out after the view clears (per second).</summary>
        const float EaseOutRate = 4f;

        Camera _camera;
        RenderTexture _texture;
        float _clearDistance = float.PositiveInfinity;

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
        /// With a <paramref name="world"/>, it moves closer when blocks are in the way.
        /// </summary>
        public void SetPose(Vector3 focus, float yaw, float pitch, float distance, VoxelWorld world = null)
        {
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 back = rotation * Vector3.back;
            transform.SetPositionAndRotation(focus + back * ClearDistance(focus, back, distance, world), rotation);
        }

        float ClearDistance(float3 focus, float3 back, float distance, VoxelWorld world)
        {
            float allowed = distance;
            if (world != null && VoxelQueries.Raycast(world, focus, back, distance + WallGap, out VoxelHit hit))
                allowed = math.max(MinDistance, hit.Distance - WallGap);

            // Snap in at once so the view never shows the inside of a hill, but ease back out.
            if (allowed < _clearDistance)
                _clearDistance = allowed;
            else
                _clearDistance = math.lerp(_clearDistance, allowed, 1f - math.exp(-EaseOutRate * Time.unscaledDeltaTime));
            return math.min(_clearDistance, distance);
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
