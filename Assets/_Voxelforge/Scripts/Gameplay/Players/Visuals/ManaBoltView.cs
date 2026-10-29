using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Draws this hero's projectiles on every PC (Roadmap 3.6, 4.2): a muzzle flash, a glowing core with a trail and a
    /// light that travels with it, and a burst of sparks where it lands, each in its ability's <see cref="AbilityLook"/>.
    /// The host moves the real bolt (<see cref="PlayerStaff"/>) and says so with <see cref="AbilityCue"/>s; this one flies
    /// the same line from the start and speed the host sent, starting at the staff's muzzle (or the hand, with nothing
    /// held) and easing onto that line, then ends wherever the host says it ended. Presentation only.
    /// <para>Each hero has its own small pool (a bolt is in the air for under a second), so there's nothing global, and
    /// URP's Forward+ only shades the bolt lights each split-screen camera can see.</para>
    /// </summary>
    [RequireComponent(typeof(PlayerAbilityCues))]
    public sealed class ManaBoltView : MonoBehaviour
    {
        [Tooltip("Additive glow material for the bolt, its trail and the sparks (Materials/ManaBolt.mat).")]
        [SerializeField] Material glowMaterial;

        [Tooltip("Data/Abilities/Looks/AbilityLookBook.asset: how each ability looks.")]
        [SerializeField] AbilityLookBook looks;

        [Tooltip("How bright the core and sparks glow (HDR multiplier on the look's glow colour).")]
        [SerializeField, Range(1f, 16f)] float glowBoost = 5f;

        [Tooltip("Seconds the bolt takes to go from the muzzle onto the host's line.")]
        [SerializeField, Range(0f, 0.5f)] float converge = 0.1f;

        [Tooltip("Seconds the muzzle flash and the impact flash last.")]
        [SerializeField, Range(0.02f, 0.5f)] float flashTime = 0.12f;

        const int PoolSize = 4;
        static readonly Color FallbackGlow = new Color(0.35f, 0.85f, 1f);

        sealed class Bolt
        {
            public GameObject Root;
            public MeshFilter Shape;
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
            public float TrailTime;
            public float LightIntensity;
            public Color Glow;
        }

        readonly List<Bolt> _bolts = new List<Bolt>();
        PlayerAbilityCues _cues;
        PlayerController _player;
        HeldItemView _held;
        ParticleSystem _sparks;
        Light _muzzleLight;
        float _muzzleFlash;
        float _muzzleIntensity;
        Mesh _gemMesh;
        Mesh _orbMesh;
        MaterialPropertyBlock _block;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        void Awake()
        {
            _cues = GetComponent<PlayerAbilityCues>();
            _player = GetComponent<PlayerController>();
            _held = GetComponent<HeldItemView>();
            _block = new MaterialPropertyBlock();
        }

        void OnEnable() => _cues.Received += OnCue;

        void OnDisable() => _cues.Received -= OnCue;

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
            if (_gemMesh != null)
                Destroy(_gemMesh);
            if (_orbMesh != null)
                Destroy(_orbMesh);
        }

        void OnCue(AbilityCue cue, AbilityDefinition ability)
        {
            if (cue.Kind == AbilityCueKind.Launched)
                OnLaunched(cue, ability);
            else if (cue.Kind == AbilityCueKind.Impact || cue.Kind == AbilityCueKind.Fizzled)
                OnEnded(cue, ability);
        }

        AbilityLook LookFor(AbilityDefinition ability) => looks != null ? looks.For(ability) : null;

        /// <summary>Where the projectile appears: the held item's muzzle, else the right hand, else the eyes.</summary>
        Vector3 Muzzle(Vector3 eyes)
        {
            if (_held == null)
                return eyes;
            if (_held.TryGetMuzzle(out Vector3 muzzle, out _))
                return muzzle;
            return _held.Hand != null ? _held.Hand.position : eyes;
        }

        void OnLaunched(AbilityCue shot, AbilityDefinition ability)
        {
            EnsureShared();
            AbilityLook look = LookFor(ability);
            Bolt bolt = Take();
            Vector3 muzzle = Muzzle(shot.Position);
            float size = look != null ? look.CoreSize : 0.22f;

            bolt.Id = shot.ShotId;
            bolt.Flying = true;
            bolt.Origin = shot.Position;
            bolt.Direction = math.normalizesafe((float3)shot.Direction, new float3(0f, 0f, 1f));
            bolt.StartOffset = (float3)muzzle - (float3)shot.Position;
            bolt.Speed = ability.ProjectileSpeed;
            bolt.Range = ability.Range;
            bolt.Age = 0f;
            bolt.Fade = 0f;
            bolt.Glow = look != null ? look.Glow : FallbackGlow;
            bolt.TrailTime = look != null ? look.TrailTime : 0.18f;
            bolt.LightIntensity = look != null ? look.LightIntensity : 4f;

            bolt.Root.transform.position = muzzle;
            bolt.Shape.sharedMesh = look != null && look.CoreShape == AbilityCoreShape.Orb ? _orbMesh : _gemMesh;
            bolt.Shape.transform.localScale = Vector3.one * size;
            bolt.Trail.time = bolt.TrailTime;
            bolt.Trail.widthCurve = new AnimationCurve(new Keyframe(0f, size * 0.8f), new Keyframe(1f, 0f));
            bolt.Trail.Clear();
            bolt.Trail.emitting = true;
            bolt.Trail.startColor = bolt.Glow * glowBoost;
            bolt.Trail.endColor = new Color(bolt.Glow.r, bolt.Glow.g, bolt.Glow.b, 0f);
            _block.SetColor(BaseColor, bolt.Glow * glowBoost);
            bolt.Core.SetPropertyBlock(_block);
            bolt.Core.enabled = true;
            bolt.Light.color = bolt.Glow;
            bolt.Light.intensity = bolt.LightIntensity;
            bolt.Light.range = look != null ? look.LightRange : 7f;
            bolt.Light.enabled = true;
            bolt.Root.SetActive(true);

            // Muzzle flash: a quick light and a puff of sparks out of the barrel.
            _muzzleLight.transform.position = muzzle;
            _muzzleLight.color = bolt.Glow;
            _muzzleLight.range = bolt.Light.range * 0.7f;
            _muzzleIntensity = bolt.LightIntensity;
            _muzzleFlash = flashTime;
            Emit(muzzle, bolt.Direction, bolt.Glow, look != null ? look.MuzzleSparks : 8, 2.5f, 0.6f);
        }

        void OnEnded(AbilityCue end, AbilityDefinition ability)
        {
            EnsureShared();
            AbilityLook look = LookFor(ability);
            bool hitBlock = end.Kind == AbilityCueKind.Impact;
            Bolt bolt = null;
            foreach (Bolt b in _bolts)
            {
                if (b.Flying && b.Id == end.ShotId)
                    bolt = b;
            }
            if (bolt != null)
            {
                bolt.Flying = false;
                bolt.Root.transform.position = end.Position;
                bolt.Core.enabled = false;
                bolt.Trail.emitting = false;
                bolt.Light.intensity = bolt.LightIntensity * (hitBlock ? 2.5f : 1f);
                bolt.Fade = math.max(bolt.TrailTime, flashTime);
            }

            // Sparks in the block's own colour mixed with the spell's, out of the face it hit.
            Color glow = look != null ? look.Glow : FallbackGlow;
            Color sparks = glow;
            VoxelWorld world = _player != null ? _player.World : null;
            if (hitBlock && world != null && end.BlockId < world.Blocks.Length)
                sparks = Color.Lerp(glow, (Color)world.Blocks[end.BlockId].MapColor, 0.45f);
            int burst = look != null ? look.ImpactSparks : 28;
            float speed = look != null ? look.ImpactSpeed : 5f;
            float3 normal = end.Direction;
            Emit((float3)end.Position + normal * 0.05f, normal, sparks, hitBlock ? burst : burst / 3, speed, 0.85f);
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
                        bolt.Fade = bolt.TrailTime;
                    }
                }
                else
                {
                    bolt.Fade -= dt;
                    bolt.Light.intensity = Mathf.MoveTowards(bolt.Light.intensity, 0f, bolt.LightIntensity * 3f * dt / flashTime);
                    if (bolt.Fade <= 0f)
                        bolt.Root.SetActive(false);
                }
            }

            if (_muzzleLight != null)
            {
                _muzzleFlash -= dt;
                bool on = _muzzleFlash > 0f;
                if (on)
                    _muzzleLight.intensity = _muzzleIntensity * 2f * (_muzzleFlash / flashTime);
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
            var shape = core.AddComponent<MeshFilter>();
            shape.sharedMesh = _gemMesh;
            var renderer = core.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = glowMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var trail = root.AddComponent<TrailRenderer>();
            trail.sharedMaterial = glowMaterial;
            trail.minVertexDistance = 0.1f;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.emitting = false;

            Light light = root.AddComponent<Light>();
            light.type = LightType.Point;
            light.shadows = LightShadows.None;
            light.enabled = false;
            return new Bolt { Root = root, Shape = shape, Core = renderer, Trail = trail, Light = light };
        }

        void EnsureShared()
        {
            if (_sparks != null)
                return;

            _gemMesh = BuildGemMesh();
            _orbMesh = BuildOrbMesh();

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
        static Mesh BuildGemMesh()
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

        /// <summary>A low-poly ball of radius 0.5, for orbs.</summary>
        static Mesh BuildOrbMesh()
        {
            const int Rings = 6, Segments = 10;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int r = 0; r <= Rings; r++)
            {
                float polar = Mathf.PI * r / Rings;
                for (int s = 0; s <= Segments; s++)
                {
                    float around = Mathf.PI * 2f * s / Segments;
                    vertices.Add(0.5f * new Vector3(Mathf.Sin(polar) * Mathf.Cos(around), Mathf.Cos(polar),
                        Mathf.Sin(polar) * Mathf.Sin(around)));
                }
            }
            for (int r = 0; r < Rings; r++)
            {
                for (int s = 0; s < Segments; s++)
                {
                    int a = r * (Segments + 1) + s, b = a + Segments + 1;
                    triangles.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
                }
            }
            var mesh = new Mesh { name = "Ability Orb Core" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
