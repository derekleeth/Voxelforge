using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Draws this hero's Mana Bolts on every PC (Roadmap 3.6): a muzzle flash, a glowing bolt with a trail and a light
    /// that travels with it, and a burst of sparks where it lands. The host moves the real bolt (<see cref="PlayerStaff"/>);
    /// this one flies the same line from the start and speed the host sent, starting at the staff's muzzle and easing onto
    /// that line, then ends wherever the host says it ended. Presentation only.
    /// <para>Each hero has its own small pool (a bolt is in the air for under a second), so there's nothing global, and
    /// URP's Forward+ only shades the bolt lights each split-screen camera can see.</para>
    /// </summary>
    [RequireComponent(typeof(PlayerStaff))]
    public sealed class ManaBoltView : MonoBehaviour
    {
        [Tooltip("Additive glow material for the bolt, its trail and the sparks (Materials/ManaBolt.mat).")]
        [SerializeField] Material glowMaterial;

        [Tooltip("How big the bolt's glowing core is, in metres.")]
        [SerializeField, Range(0.05f, 0.6f)] float boltSize = 0.22f;

        [Tooltip("How bright the core and sparks glow (HDR multiplier on the item's glow colour).")]
        [SerializeField, Range(1f, 16f)] float glowBoost = 5f;

        [Tooltip("Seconds the trail takes to fade.")]
        [SerializeField, Range(0.05f, 1f)] float trailTime = 0.18f;

        [Tooltip("The light that travels with the bolt.")]
        [SerializeField, Range(0f, 20f)] float boltLightIntensity = 4f;

        [SerializeField, Range(1f, 20f)] float boltLightRange = 7f;

        [Tooltip("Seconds the bolt takes to go from the muzzle onto the host's line.")]
        [SerializeField, Range(0f, 0.5f)] float converge = 0.1f;

        [Tooltip("Seconds the muzzle flash and the impact flash last.")]
        [SerializeField, Range(0.02f, 0.5f)] float flashTime = 0.12f;

        [Tooltip("Sparks in one impact burst.")]
        [SerializeField, Range(0, 80)] int burstSparks = 28;

        [Tooltip("Sparks out of the muzzle per shot.")]
        [SerializeField, Range(0, 30)] int muzzleSparks = 8;

        const int PoolSize = 4;

        sealed class Bolt
        {
            public GameObject Root;
            public MeshRenderer Core;
            public TrailRenderer Trail;
            public Light Light;
            public ushort Id;
            public bool Flying;
            public float3 Origin;
            public float3 Direction;
            public float3 StartOffset;
            public float Speed;
            public float Range;
            public float Age;
            /// <summary>Seconds left of the fade after it ended (the trail and the impact flash).</summary>
            public float Fade;
            public Color Glow;
        }

        readonly List<Bolt> _bolts = new List<Bolt>();
        PlayerStaff _staff;
        PlayerController _player;
        HeldItemView _held;
        ParticleSystem _sparks;
        Light _muzzleLight;
        float _muzzleFlash;
        Mesh _coreMesh;
        MaterialPropertyBlock _block;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        /// <summary>Every PC: a bolt left the muzzle here. For the 3.7 audio router.</summary>
        public event Action<Vector3> Fired;

        /// <summary>Every PC: a bolt hit a block (true) or fizzled (false) here. For the 3.7 audio router.</summary>
        public event Action<Vector3, bool> Impacted;

        void Awake()
        {
            _staff = GetComponent<PlayerStaff>();
            _player = GetComponent<PlayerController>();
            _held = GetComponent<HeldItemView>();
            _block = new MaterialPropertyBlock();
        }

        void OnEnable()
        {
            _staff.Fired += OnFired;
            _staff.Ended += OnEnded;
        }

        void OnDisable()
        {
            _staff.Fired -= OnFired;
            _staff.Ended -= OnEnded;
        }

        void OnDestroy()
        {
            foreach (Bolt bolt in _bolts)
            {
                if (bolt.Root != null)
                    Destroy(bolt.Root);
            }
            if (_sparks != null)
                Destroy(_sparks.gameObject);
            if (_muzzleLight != null)
                Destroy(_muzzleLight.gameObject);
            if (_coreMesh != null)
                Destroy(_coreMesh);
        }

        void OnFired(ManaBolt shot, ProjectileStats stats)
        {
            EnsureShared();
            Bolt bolt = Take();
            Vector3 muzzle = (Vector3)shot.Position;
            if (_held == null || !_held.TryGetMuzzle(out muzzle, out _))
                muzzle = shot.Position;

            bolt.Id = shot.Id;
            bolt.Flying = true;
            bolt.Origin = shot.Position;
            bolt.Direction = shot.Direction;
            bolt.StartOffset = (float3)muzzle - shot.Position;
            bolt.Speed = shot.Speed;
            bolt.Range = shot.Remaining;
            bolt.Age = 0f;
            bolt.Fade = 0f;
            bolt.Glow = stats.glow.a > 0f ? stats.glow : new Color(0.35f, 0.85f, 1f);

            bolt.Root.transform.position = muzzle;
            bolt.Trail.Clear();
            bolt.Trail.emitting = true;
            bolt.Trail.startColor = bolt.Glow * glowBoost;
            bolt.Trail.endColor = new Color(bolt.Glow.r, bolt.Glow.g, bolt.Glow.b, 0f);
            _block.SetColor(BaseColor, bolt.Glow * glowBoost);
            bolt.Core.SetPropertyBlock(_block);
            bolt.Core.enabled = true;
            bolt.Light.color = bolt.Glow;
            bolt.Light.intensity = boltLightIntensity;
            bolt.Light.enabled = true;
            bolt.Root.SetActive(true);

            // Muzzle flash: a quick light and a puff of sparks out of the barrel.
            _muzzleLight.transform.position = muzzle;
            _muzzleLight.color = bolt.Glow;
            _muzzleFlash = flashTime;
            Emit(muzzle, shot.Direction, bolt.Glow, muzzleSparks, 2.5f, 0.6f);
            Fired?.Invoke(muzzle);
        }

        void OnEnded(ManaBoltEnd end, ushort blockId)
        {
            EnsureShared();
            Bolt bolt = null;
            foreach (Bolt b in _bolts)
            {
                if (b.Flying && b.Id == end.Id)
                    bolt = b;
            }
            Color glow = bolt != null ? bolt.Glow : new Color(0.35f, 0.85f, 1f);
            if (bolt != null)
            {
                bolt.Flying = false;
                bolt.Root.transform.position = end.Point;
                bolt.Core.enabled = false;
                bolt.Trail.emitting = false;
                bolt.Light.intensity = boltLightIntensity * (end.HitBlock ? 2.5f : 1f);
                bolt.Fade = math.max(trailTime, flashTime);
            }

            // Sparks in the block's own colour mixed with mana, out of the face it hit.
            Color sparks = glow;
            VoxelWorld world = _player != null ? _player.World : null;
            if (end.HitBlock && world != null && blockId < world.Blocks.Length)
                sparks = Color.Lerp(glow, (Color)world.Blocks[blockId].MapColor, 0.45f);
            Emit(end.Point + end.Normal * 0.05f, end.Normal, sparks, end.HitBlock ? burstSparks : burstSparks / 3, 5f, 0.85f);
            Impacted?.Invoke(end.Point, end.HitBlock);
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            foreach (Bolt bolt in _bolts)
            {
                if (!bolt.Root.activeSelf)
                    continue;
                if (bolt.Flying)
                {
                    bolt.Age += dt;
                    // Never past its range: the host's word on how it ended is on its way.
                    float travelled = math.min(bolt.Age * bolt.Speed, bolt.Range);
                    float pull = converge > 0f ? math.saturate(1f - bolt.Age / converge) : 0f;
                    bolt.Root.transform.position = bolt.Origin + bolt.Direction * travelled + bolt.StartOffset * pull;
                    // If the host's message is lost, don't hang in the air for ever.
                    if (bolt.Age > bolt.Range / math.max(bolt.Speed, 0.01f) + 1f)
                    {
                        bolt.Flying = false;
                        bolt.Core.enabled = false;
                        bolt.Trail.emitting = false;
                        bolt.Fade = trailTime;
                    }
                }
                else
                {
                    bolt.Fade -= dt;
                    bolt.Light.intensity = Mathf.MoveTowards(bolt.Light.intensity, 0f, boltLightIntensity * 3f * dt / flashTime);
                    if (bolt.Fade <= 0f)
                        bolt.Root.SetActive(false);
                }
            }

            if (_muzzleLight != null)
            {
                _muzzleFlash -= dt;
                bool on = _muzzleFlash > 0f;
                if (on)
                    _muzzleLight.intensity = boltLightIntensity * 2f * (_muzzleFlash / flashTime);
                if (_muzzleLight.enabled != on)
                    _muzzleLight.enabled = on;
            }
        }

        Bolt Take()
        {
            foreach (Bolt bolt in _bolts)
            {
                if (!bolt.Root.activeSelf)
                    return bolt;
            }
            if (_bolts.Count < PoolSize)
            {
                Bolt created = CreateBolt();
                _bolts.Add(created);
                return created;
            }
            // All busy: reuse the oldest.
            Bolt oldest = _bolts[0];
            foreach (Bolt bolt in _bolts)
            {
                if (bolt.Age > oldest.Age)
                    oldest = bolt;
            }
            return oldest;
        }

        Bolt CreateBolt()
        {
            var root = new GameObject("Mana Bolt");
            root.SetActive(false);
            var core = new GameObject("Core");
            core.transform.SetParent(root.transform, false);
            core.transform.localScale = Vector3.one * boltSize;
            core.AddComponent<MeshFilter>().sharedMesh = _coreMesh;
            var renderer = core.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = glowMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var trail = root.AddComponent<TrailRenderer>();
            trail.sharedMaterial = glowMaterial;
            trail.time = trailTime;
            trail.minVertexDistance = 0.1f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, boltSize * 0.8f), new Keyframe(1f, 0f));
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.emitting = false;

            Light light = root.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = boltLightRange;
            light.shadows = LightShadows.None;
            light.enabled = false;
            return new Bolt { Root = root, Core = renderer, Trail = trail, Light = light };
        }

        void EnsureShared()
        {
            if (_sparks != null)
                return;

            _coreMesh = BuildCoreMesh();

            var sparksObject = new GameObject($"{name} Mana Sparks");
            _sparks = sparksObject.AddComponent<ParticleSystem>();
            _sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = _sparks.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 0.45f;
            main.startSize = 0.07f;
            main.gravityModifier = 0.8f;
            main.maxParticles = 256;
            ParticleSystem.EmissionModule emission = _sparks.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = _sparks.shape;
            shape.enabled = false;
            ParticleSystem.SizeOverLifetimeModule size = _sparks.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f)));
            var renderer = sparksObject.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = glowMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _sparks.Play();

            _muzzleLight = new GameObject($"{name} Muzzle Flash").AddComponent<Light>();
            _muzzleLight.type = LightType.Point;
            _muzzleLight.range = boltLightRange * 0.7f;
            _muzzleLight.shadows = LightShadows.None;
            _muzzleLight.enabled = false;
        }

        void Emit(float3 at, float3 normal, Color colour, int count, float speed, float spread)
        {
            if (_sparks == null || count <= 0)
                return;
            float3 n = math.normalizesafe(normal, new float3(0f, 1f, 0f));
            var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                float3 random = UnityEngine.Random.insideUnitSphere;
                float3 dir = math.normalizesafe(n + random * spread * 1.6f, n);
                emit.position = at;
                emit.velocity = dir * speed * UnityEngine.Random.Range(0.4f, 1f);
                // Particle colours are 0-1, so brightness comes from the additive material, not an HDR boost.
                emit.startColor = Color.Lerp(colour, Color.white, UnityEngine.Random.Range(0f, 0.35f));
                emit.startSize = UnityEngine.Random.Range(0.04f, 0.1f);
                _sparks.Emit(emit, 1);
            }
        }

        /// <summary>A small faceted gem (an octahedron), matching the mana crystals on the staff.</summary>
        static Mesh BuildCoreMesh()
        {
            Vector3[] v =
            {
                new Vector3(0f, 0.5f, 0f), new Vector3(0f, -0.5f, 0f), new Vector3(0.5f, 0f, 0f),
                new Vector3(-0.5f, 0f, 0f), new Vector3(0f, 0f, 0.5f), new Vector3(0f, 0f, -0.5f),
            };
            int[] t = { 0, 4, 2, 0, 2, 5, 0, 5, 3, 0, 3, 4, 1, 2, 4, 1, 5, 2, 1, 3, 5, 1, 4, 3 };
            var mesh = new Mesh { name = "Mana Bolt Core", vertices = v, triangles = t };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
