using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Keeps heroes out of their own way in tight spaces (Roadmap 3.7). In a tunnel the camera slides right up to the
    /// hero (<see cref="PlayerCameraRig"/>), and their body would fill the view. While a player camera is that close to a
    /// hero, or close with the hero right in the middle of its view (a 1-wide tunnel), the hero (body and held item) is
    /// drawn as a shadow only, <b>for that camera alone</b>: in split-screen the other view still sees them. Over the
    /// shoulder in a wider tunnel (<see cref="PlayerCameraRig"/>) the hero is off to the side, so they stay visible.
    /// Presentation only, on this PC.
    /// <para>Owned by <see cref="SplitScreenDirector"/>, which shares its rig and player arrays, so nothing is global.</para>
    /// </summary>
    public sealed class HeroCameraFade : IDisposable
    {
        /// <summary>A camera closer than this to a hero's body hides them (meters).</summary>
        public const float HideDistance = 1.1f;
        /// <summary>A hidden hero shows again once the camera is further than this, so they don't flicker at the edge.</summary>
        public const float ShowDistance = 1.5f;
        /// <summary>Within this distance, a hero in the middle of the view hides too, since they'd block what you aim at.</summary>
        public const float BlockingDistance = 2.6f;
        /// <summary>How close to the middle of the view (the camera's forward ray) counts as blocking it, in meters.</summary>
        public const float BlockingRadius = 0.45f;
        /// <summary>Extra distance and radius before a hero hidden for blocking the view shows again.</summary>
        const float BlockingSlack = 0.4f, BlockingRadiusSlack = 0.15f;
        /// <summary>The part of the hero the camera measures to: from the hips to the top of the head, above the feet.</summary>
        const float BodyBottom = 0.5f, BodyTop = 1.75f;

        readonly PlayerCameraRig[] _rigs;
        readonly PlayerController[] _heroes;
        readonly bool[,] _hidden;
        readonly List<Renderer> _found = new List<Renderer>();
        readonly List<(Renderer Renderer, ShadowCastingMode Mode)> _changed = new List<(Renderer, ShadowCastingMode)>();

        /// <param name="rigs">Player cameras by local slot (entries may be null).</param>
        /// <param name="heroes">Heroes by local slot, null when not playing. Read every frame, so the caller keeps it current.</param>
        public HeroCameraFade(PlayerCameraRig[] rigs, PlayerController[] heroes)
        {
            _rigs = rigs;
            _heroes = heroes;
            _hidden = new bool[rigs.Length, heroes.Length];
            RenderPipelineManager.beginCameraRendering += BeforeCamera;
            RenderPipelineManager.endCameraRendering += AfterCamera;
        }

        /// <summary>
        /// Whether a hero should be hidden from a camera <paramref name="distance"/> meters from their body, given whether
        /// they were hidden last frame.
        /// </summary>
        /// <param name="offCentre">How far the hero's body is from the middle of the view (the camera's forward ray).</param>
        public static bool ShouldHide(bool wasHidden, float distance, float offCentre)
        {
            if (wasHidden)
                return distance < ShowDistance ||
                       (distance < BlockingDistance + BlockingSlack && offCentre < BlockingRadius + BlockingRadiusSlack);
            return distance < HideDistance || (distance < BlockingDistance && offCentre < BlockingRadius);
        }

        /// <summary>How far <paramref name="camera"/> is from the body of a hero standing at <paramref name="feet"/>.</summary>
        public static float DistanceToBody(float3 camera, float3 feet)
        {
            float y = math.clamp(camera.y, feet.y + BodyBottom, feet.y + BodyTop);
            return math.distance(camera, new float3(feet.x, y, feet.z));
        }

        /// <summary>
        /// How far the body of a hero standing at <paramref name="feet"/> is from a ray from <paramref name="camera"/> along
        /// <paramref name="forward"/> (normalized): the closest of a few points from hips to head. Infinite if they're behind.
        /// </summary>
        public static float DistanceOffCentre(float3 camera, float3 forward, float3 feet)
        {
            float closest = float.PositiveInfinity;
            for (int i = 0; i < 3; i++)
            {
                float3 point = feet + new float3(0f, math.lerp(BodyBottom, BodyTop, i * 0.5f), 0f);
                float along = math.dot(point - camera, forward);
                if (along > 0f)
                    closest = math.min(closest, math.distance(point, camera + forward * along));
            }
            return closest;
        }

        /// <summary>True if the hero in <paramref name="heroSlot"/> is hidden from the camera of <paramref name="cameraSlot"/>.</summary>
        public bool IsHidden(int cameraSlot, int heroSlot) => _hidden[cameraSlot, heroSlot];

        void BeforeCamera(ScriptableRenderContext context, Camera camera)
        {
            int cameraSlot = SlotOf(camera);
            if (cameraSlot < 0)
                return;

            float3 position = camera.transform.position;
            float3 forward = camera.transform.forward;
            for (int hero = 0; hero < _heroes.Length; hero++)
            {
                PlayerController controller = _heroes[hero];
                float3 feet = controller != null ? (float3)controller.VisualRoot.position : default;
                bool hide = controller != null && ShouldHide(_hidden[cameraSlot, hero], DistanceToBody(position, feet),
                    DistanceOffCentre(position, forward, feet));
                _hidden[cameraSlot, hero] = hide;
                if (!hide)
                    continue;

                // Looked up each time: the held item's renderer comes and goes with what's in hand.
                controller.VisualRoot.GetComponentsInChildren(false, _found);
                foreach (Renderer renderer in _found)
                {
                    if (!renderer.enabled || renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
                        continue;
                    _changed.Add((renderer, renderer.shadowCastingMode));
                    if (renderer.shadowCastingMode == ShadowCastingMode.Off)
                        renderer.forceRenderingOff = true;
                    else
                        renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                }
            }
        }

        int SlotOf(Camera camera)
        {
            for (int slot = 0; slot < _rigs.Length; slot++)
            {
                if (_rigs[slot] != null && _rigs[slot].Camera == camera)
                    return slot;
            }
            return -1;
        }

        void AfterCamera(ScriptableRenderContext context, Camera camera) => Restore();

        void Restore()
        {
            foreach ((Renderer renderer, ShadowCastingMode mode) in _changed)
            {
                if (renderer == null)
                    continue;
                if (mode == ShadowCastingMode.Off)
                    renderer.forceRenderingOff = false;
                else
                    renderer.shadowCastingMode = mode;
            }
            _changed.Clear();
        }

        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= BeforeCamera;
            RenderPipelineManager.endCameraRendering -= AfterCamera;
            Restore();
        }
    }
}
