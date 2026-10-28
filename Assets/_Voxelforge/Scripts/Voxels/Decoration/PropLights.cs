using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// Real point lights for props that carry one (torches, Roadmap 3.5; see <see cref="DecorationLight"/>). A world can
    /// hold any number of torches, so instead of a light per torch there is a fixed pool: every frame the torches
    /// nearest to <b>any</b> viewer from the <see cref="ChunkLoader"/> (every player, D-010) get the lights, each with
    /// its own flicker. URP's Forward+ (D-006) then shades only the lights each split-screen camera can see.
    /// <para>Presentation only, and it reads the decoration layer, so it never changes blocks.</para>
    /// </summary>
    public sealed class PropLights : MonoBehaviour
    {
        [SerializeField] VoxelWorldManager world;

        [Tooltip("Where the players are: lights go to the torches nearest any of them.")]
        [SerializeField] ChunkLoader chunkLoader;

        [Tooltip("The most torch lights on at once, across every player.")]
        [SerializeField, Range(1, 128)] int maxLights = 32;

        [Tooltip("Torches further than this from every player get no light, in metres.")]
        [SerializeField, Range(4f, 128f)] float maxDistance = 48f;

        [Tooltip("Lights within this distance of a player cast shadows (costly: keep it small). 0 = none.")]
        [SerializeField, Range(0f, 32f)] float shadowDistance;

        struct Candidate
        {
            public float Distance;
            public float3 Position;
            public DecorationLight Light;
            public uint Random;
        }

        readonly List<Candidate> _candidates = new List<Candidate>();
        readonly List<Light> _pool = new List<Light>();
        static readonly Comparer<Candidate> ByDistance = Comparer<Candidate>.Create((a, b) => a.Distance.CompareTo(b.Distance));

        /// <summary>Torch lights switched on this frame. For the debug overlay.</summary>
        public int ActiveCount { get; private set; }

        void LateUpdate()
        {
            Gather();
            _candidates.Sort(ByDistance);

            int count = math.min(_candidates.Count, maxLights);
            for (int i = 0; i < count; i++)
            {
                Light light = i < _pool.Count ? _pool[i] : CreateLight();
                Candidate candidate = _candidates[i];
                light.transform.position = candidate.Position;
                light.color = candidate.Light.color;
                light.range = candidate.Light.range;
                light.intensity = candidate.Light.intensity * Flicker(candidate);
                light.shadows = candidate.Distance < shadowDistance ? LightShadows.Soft : LightShadows.None;
                if (!light.enabled)
                    light.enabled = true;
            }
            for (int i = count; i < _pool.Count; i++)
            {
                if (_pool[i].enabled)
                    _pool[i].enabled = false;
            }
            ActiveCount = count;
        }

        void Gather()
        {
            _candidates.Clear();
            DecorationLayer layer = world != null ? world.Decorations : null;
            IReadOnlyList<ChunkViewer> viewers = chunkLoader != null ? chunkLoader.Viewers : null;
            if (layer == null || viewers == null || viewers.Count == 0)
                return;

            foreach (DecorationLayer.TypeData type in layer.Types)
            {
                DecorationLight light = type.Definition.Light;
                if (!light.enabled)
                    continue;
                for (int i = 0; i < type.Instances.Length; i++)
                {
                    DecorationInstance instance = type.Instances[i];
                    if (instance.Scale <= 0f)
                        continue; // An empty slot.
                    float3 position = instance.Position + RotateY((float3)light.offset * instance.Scale, instance.Yaw);
                    float nearest = float.MaxValue;
                    foreach (ChunkViewer viewer in viewers)
                        nearest = math.min(nearest, math.distance(position, viewer.Position));
                    if (nearest <= maxDistance)
                        _candidates.Add(new Candidate { Distance = nearest, Position = position, Light = light, Random = instance.Random });
                }
            }
        }

        /// <summary>Turns <paramref name="v"/> around +Y like <c>DecorationRotateY</c> in <c>Decoration.hlsl</c>.</summary>
        static float3 RotateY(float3 v, float yaw)
        {
            math.sincos(yaw, out float s, out float c);
            return new float3(c * v.x + s * v.z, v.y, -s * v.x + c * v.z);
        }

        /// <summary>A brightness multiplier around 1 that wobbles like a flame, out of step for every torch.</summary>
        static float Flicker(Candidate candidate)
        {
            float seed = (candidate.Random & 0xFFFF) * 0.01f;
            float t = Time.time;
            float wobble = Mathf.PerlinNoise(t * 6f, seed) * 0.7f + Mathf.PerlinNoise(t * 17f, seed + 31f) * 0.3f;
            return 1f + candidate.Light.flicker * (wobble - 0.5f);
        }

        Light CreateLight()
        {
            var go = new GameObject($"Torch Light {_pool.Count}");
            go.transform.SetParent(transform, false);
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.renderMode = LightRenderMode.ForcePixel;
            light.shadows = LightShadows.None;
            _pool.Add(light);
            return light;
        }

        void OnDestroy()
        {
            foreach (Light light in _pool)
            {
                if (light != null)
                    Destroy(light.gameObject);
            }
            _pool.Clear();
        }
    }
}
