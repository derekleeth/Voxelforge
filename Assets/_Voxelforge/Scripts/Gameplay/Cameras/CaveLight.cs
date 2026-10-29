using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Makes caves a little darker (after 4.1 play-testing). The sky's ambient light and reflections reach everywhere
    /// equally, so without this a cave is only as dark as the sun's shadow. Before each player camera renders, this
    /// looks up from it through the blocks (<see cref="SkyExposure"/>) and dims ambient light and reflections by how
    /// much sky is hidden, <b>for that camera alone</b>, then puts them back: in split-screen the other view keeps its
    /// own light. It eases between levels so walking into a cave fades rather than snaps. Presentation only, on this PC.
    /// <para>Owned by <see cref="SplitScreenDirector"/>, which shares its rig and player arrays, so nothing is global.</para>
    /// </summary>
    public sealed class CaveLight : IDisposable
    {
        /// <summary>How far up a ray looks for a roof, in metres.</summary>
        public const float Reach = 40f;
        /// <summary>Seconds between new sky measurements for one camera.</summary>
        const float MeasureEvery = 0.1f;
        /// <summary>How quickly the light follows a new measurement (per second).</summary>
        const float FadeRate = 2.5f;

        // Straight up, then rings of 4 at 25° and 50° from straight up: a cone of sky over the camera.
        static readonly float3[] Rays = BuildRays();

        readonly PlayerCameraRig[] _rigs;
        readonly PlayerController[] _heroes;
        readonly float[] _exposure;
        readonly float[] _target;
        readonly float[] _nextMeasure;
        readonly float[] _lastUpdate;
        readonly float _caveAmbient;

        bool _changed;
        SphericalHarmonicsL2 _savedProbe;
        float _savedReflection;

        /// <param name="rigs">Player cameras by local slot (entries may be null).</param>
        /// <param name="heroes">Heroes by local slot, null when not playing. Read every frame, so the caller keeps it current.</param>
        /// <param name="caveAmbient">How much ambient light is left with no sky in sight (0-1).</param>
        public CaveLight(PlayerCameraRig[] rigs, PlayerController[] heroes, float caveAmbient)
        {
            _rigs = rigs;
            _heroes = heroes;
            _exposure = new float[rigs.Length];
            _target = new float[rigs.Length];
            _nextMeasure = new float[rigs.Length];
            _lastUpdate = new float[rigs.Length];
            for (int i = 0; i < rigs.Length; i++)
                _exposure[i] = _target[i] = 1f;
            _caveAmbient = math.saturate(caveAmbient);
            RenderPipelineManager.beginCameraRendering += BeforeCamera;
            RenderPipelineManager.endCameraRendering += AfterCamera;
        }

        /// <summary>
        /// How much open sky <paramref name="from"/> can see: the share of a cone of rays up that reach
        /// <see cref="Reach"/> metres without hitting a block, 0 (deep in a cave) to 1 (out in the open). Unloaded chunks
        /// count as sky. Trees are decorations, not blocks, so they don't count as a roof.
        /// </summary>
        public static float SkyExposure(VoxelWorld world, float3 from)
        {
            if (world == null)
                return 1f;
            int open = 0;
            foreach (float3 ray in Rays)
            {
                if (!VoxelQueries.Raycast(world, from, ray, Reach, out _, opaqueOnly: true))
                    open++;
            }
            return open / (float)Rays.Length;
        }

        /// <summary>The ambient multiplier for a camera that sees <paramref name="exposure"/> of the sky.</summary>
        public static float AmbientScale(float exposure, float caveAmbient) => math.lerp(caveAmbient, 1f, math.saturate(exposure));

        void BeforeCamera(ScriptableRenderContext context, Camera camera)
        {
            int slot = SlotOf(camera);
            PlayerController hero = slot >= 0 ? _heroes[slot] : null;
            if (hero == null)
                return;

            float now = Time.unscaledTime;
            float3 eye = camera.transform.position;
            if (now >= _nextMeasure[slot])
            {
                _nextMeasure[slot] = now + MeasureEvery;
                _target[slot] = SkyExposure(hero.World, eye);
            }
            float dt = math.clamp(now - _lastUpdate[slot], 0f, 0.25f);
            _lastUpdate[slot] = now;
            _exposure[slot] = math.lerp(_exposure[slot], _target[slot], 1f - math.exp(-FadeRate * dt));

            float scale = AmbientScale(_exposure[slot], _caveAmbient);
            if (scale >= 0.999f)
                return;
            _savedProbe = RenderSettings.ambientProbe;
            _savedReflection = RenderSettings.reflectionIntensity;
            _changed = true;
            SphericalHarmonicsL2 dimmed = _savedProbe;
            dimmed *= scale;
            RenderSettings.ambientProbe = dimmed;
            RenderSettings.reflectionIntensity = _savedReflection * scale;
        }

        void AfterCamera(ScriptableRenderContext context, Camera camera) => Restore();

        void Restore()
        {
            if (!_changed)
                return;
            RenderSettings.ambientProbe = _savedProbe;
            RenderSettings.reflectionIntensity = _savedReflection;
            _changed = false;
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

        static float3[] BuildRays()
        {
            var rays = new float3[9];
            rays[0] = new float3(0f, 1f, 0f);
            for (int ring = 0; ring < 2; ring++)
            {
                float tilt = math.radians(ring == 0 ? 25f : 50f);
                for (int i = 0; i < 4; i++)
                {
                    // The outer ring is turned 45° so the 8 tilted rays spread evenly around.
                    float yaw = math.radians(i * 90f + ring * 45f);
                    rays[1 + ring * 4 + i] = new float3(math.sin(tilt) * math.cos(yaw), math.cos(tilt), math.sin(tilt) * math.sin(yaw));
                }
            }
            return rays;
        }

        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= BeforeCamera;
            RenderPipelineManager.endCameraRendering -= AfterCamera;
            Restore();
        }
    }
}
