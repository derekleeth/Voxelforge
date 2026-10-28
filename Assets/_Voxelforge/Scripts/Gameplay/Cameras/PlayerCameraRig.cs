using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// One local player's camera. <see cref="SplitScreenDirector"/> makes one per slot by copying the overview camera
    /// (so every view shares its render settings), and each frame tells it where to look and where to draw: straight
    /// to the screen, to a rectangle of it, or into a texture for the Voronoi composite.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class PlayerCameraRig : MonoBehaviour
    {
        Camera _camera;
        RenderTexture _texture;

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

        /// <summary>Orbits the camera <paramref name="distance"/> meters back from <paramref name="focus"/>, looking at it.</summary>
        public void SetPose(Vector3 focus, float yaw, float pitch, float distance)
        {
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            transform.SetPositionAndRotation(focus - rotation * Vector3.forward * distance, rotation);
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
