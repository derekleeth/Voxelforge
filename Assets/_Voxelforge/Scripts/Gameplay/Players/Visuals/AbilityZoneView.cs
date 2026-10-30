using UnityEngine;
using UnityEngine.Rendering;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Draws an <see cref="AbilityZone"/> on every PC (Roadmap 4.6) from its spell's <see cref="AbilityLook"/>: a glowing
    /// ring on the ground at its edge, an inner ring, motes rising from inside the circle and a soft light, all in the
    /// look's colour. It fades in when placed, flares on each pulse and fades out as its time runs out. Looks only:
    /// the host has already healed whoever stood in it (Roadmap 4.2).
    /// </summary>
    [RequireComponent(typeof(AbilityZone))]
    public sealed class AbilityZoneView : MonoBehaviour
    {
        [Tooltip("Data/Abilities/Looks/AbilityLookBook.asset.")]
        [SerializeField] AbilityLookBook looks;

        [Tooltip("Art/Materials/Props/ManaBolt.mat: additive glow for the rings and motes.")]
        [SerializeField] Material glowMaterial;

        [Tooltip("How much brighter than the look's colour the rings glow.")]
        [SerializeField, Range(1f, 10f)] float glowBoost = 3f;

        const int RingPoints = 72;
        const float RingLift = 0.06f;
        const float FadeIn = 0.35f;
        const float FadeOut = 0.8f;
        const float PulseSeconds = 0.45f;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        AbilityZone _zone;
        LineRenderer _outer;
        LineRenderer _inner;
        ParticleSystem _motes;
        Light _light;
        Color _glow = new Color(1f, 0.85f, 0.4f);
        float _radius = 3f;
        float _duration = 1f;
        float _lightIntensity = 3f;
        float _pulsedAt = float.NegativeInfinity;
        int _pulseSparks = 30;
        bool _built;

        void Awake() => _zone = GetComponent<AbilityZone>();

        void OnEnable() => _zone.Pulsed += OnPulsed;

        void OnDisable() => _zone.Pulsed -= OnPulsed;

        void Build()
        {
            _built = true;
            AbilityDefinition ability = _zone.Ability;
            AbilityLook look = null;
            if (ability != null && looks != null)
                looks.TryGet(ability.Id, out look);
            if (look != null)
            {
                _glow = look.Glow;
                _lightIntensity = look.LightIntensity;
                _pulseSparks = look.ImpactSparks;
            }
            _radius = ability != null ? Mathf.Max(ability.AoeRadius, 0.5f) : _radius;
            _duration = ability != null ? Mathf.Max(ability.Duration, 0.1f) : _duration;

            _outer = Ring("Outer Ring", _radius, 0.16f);
            _inner = Ring("Inner Ring", _radius * 0.55f, 0.08f);

            var light = new GameObject("Light");
            light.transform.SetParent(transform, false);
            light.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            _light = light.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.shadows = LightShadows.None;
            _light.color = _glow;
            _light.range = _radius * 2.2f;

            _motes = Motes(look != null ? look.CastMotesPerSecond : 40f);
        }

        LineRenderer Ring(string name, float radius, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = glowMaterial;
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = RingPoints;
            line.widthMultiplier = width;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.alignment = LineAlignment.TransformZ;
            // Lying flat on the ground: the line faces up.
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            for (int i = 0; i < RingPoints; i++)
            {
                float a = i * Mathf.PI * 2f / RingPoints;
                line.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, -RingLift));
            }
            return line;
        }

        ParticleSystem Motes(float rate)
        {
            var go = new GameObject("Motes");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.1f, 0f);
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var motes = go.AddComponent<ParticleSystem>();
            motes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = motes.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
            main.startColor = _glow * glowBoost;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;
            ParticleSystem.EmissionModule emission = motes.emission;
            emission.rateOverTime = Mathf.Max(rate, 10f) * _radius / 3f;
            ParticleSystem.ShapeModule shape = motes.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = _radius;
            ParticleSystem.ColorOverLifetimeModule colour = motes.colorOverLifetime;
            colour.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
            colour.color = fade;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = glowMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            motes.Play();
            return motes;
        }

        void OnPulsed(AbilityZone zone)
        {
            _pulsedAt = Time.time;
            if (_motes == null)
                return;
            // A burst of sparks up out of the circle with every pulse.
            var emit = new ParticleSystem.EmitParams { applyShapeToPosition = true, startColor = _glow * glowBoost };
            _motes.Emit(emit, _pulseSparks);
        }

        void LateUpdate()
        {
            if (!_zone.IsSpawned)
                return;
            if (!_built)
                Build();
            float age = _zone.Age;
            float strength = Mathf.Clamp01(age / FadeIn) * Mathf.Clamp01((_duration - age) / FadeOut);
            float pulse = Mathf.Clamp01(1f - (Time.time - _pulsedAt) / PulseSeconds);
            float shimmer = 0.85f + 0.15f * Mathf.Sin(Time.time * 4f);

            Color ring = _glow * glowBoost * strength * (shimmer + pulse * 1.5f);
            ring.a = strength;
            _outer.startColor = _outer.endColor = ring;
            _outer.widthMultiplier = 0.16f * (1f + pulse * 0.8f);
            Color inner = ring * 0.6f;
            inner.a = strength * 0.7f;
            _inner.startColor = _inner.endColor = inner;
            // The inner ring turns slowly, like a rune circle.
            _inner.transform.localRotation = Quaternion.Euler(90f, Time.time * 25f, 0f);
            _light.intensity = _lightIntensity * strength * (0.8f + pulse);
        }
    }
}
