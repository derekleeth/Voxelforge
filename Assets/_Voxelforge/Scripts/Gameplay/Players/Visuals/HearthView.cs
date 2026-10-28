using UnityEngine;
using UnityEngine.Rendering;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Draws hearthing home on every PC (Roadmap 3.8): green motes spiralling up around the hero and a growing green
    /// light while they cast, then a burst of motes and a flash where they land (or a puff if it's cancelled).
    /// Presentation only; <see cref="PlayerHearth"/> on the host decides everything.
    /// </summary>
    [RequireComponent(typeof(PlayerHearth))]
    public sealed class HearthView : MonoBehaviour
    {
        [Tooltip("Additive glow material for the motes (Materials/ManaBolt.mat).")]
        [SerializeField] Material glowMaterial;

        [Tooltip("The Home Totem's green.")]
        [SerializeField] Color glow = new Color(0.35f, 1f, 0.45f);

        [Tooltip("How many motes rise per second while casting.")]
        [SerializeField, Range(10f, 200f)] float motesPerSecond = 70f;

        [Tooltip("Brightest the hero's light gets at the end of the cast.")]
        [SerializeField, Range(0f, 10f)] float lightIntensity = 3f;

        const float FlashSeconds = 0.6f;

        PlayerHearth _hearth;
        PlayerController _player;
        ParticleSystem _motes;
        Light _light;
        float _emitCarry;
        float _flashUntil = float.NegativeInfinity;
        Vector3 _flashAt;

        void Awake()
        {
            _hearth = GetComponent<PlayerHearth>();
            _player = GetComponent<PlayerController>();
        }

        void OnEnable()
        {
            _hearth.CastEnded += OnCastEnded;
        }

        void OnDisable()
        {
            _hearth.CastEnded -= OnCastEnded;
        }

        void OnDestroy()
        {
            if (_motes != null)
                Destroy(_motes.gameObject);
            if (_light != null)
                Destroy(_light.gameObject);
        }

        void LateUpdate()
        {
            bool casting = _hearth.IsCasting;
            bool flashing = Time.time < _flashUntil;
            if (!casting && !flashing)
            {
                if (_light != null)
                    _light.enabled = false;
                return;
            }
            EnsureCreated();

            Vector3 feet = _player.VisualRoot.position;
            if (casting)
            {
                float progress = _hearth.CastProgress;
                _emitCarry += motesPerSecond * (0.4f + progress) * Time.deltaTime;
                for (; _emitCarry >= 1f; _emitCarry -= 1f)
                    EmitRising(feet, progress);
                _light.transform.position = feet + Vector3.up * 1f;
                _light.intensity = lightIntensity * progress;
                _light.range = 3f + 3f * progress;
            }
            else
            {
                float left = (_flashUntil - Time.time) / FlashSeconds;
                _light.transform.position = _flashAt + Vector3.up * 1f;
                _light.intensity = lightIntensity * 1.6f * left;
                _light.range = 7f;
            }
            _light.enabled = true;
        }

        void OnCastEnded(bool arrived, Vector3 at)
        {
            EnsureCreated();
            int count = arrived ? 60 : 15;
            for (int i = 0; i < count; i++)
            {
                Vector3 direction = Random.onUnitSphere;
                direction.y = Mathf.Abs(direction.y);
                Emit(at + Vector3.up * Random.Range(0.1f, 1.6f), direction * Random.Range(1f, arrived ? 4f : 1.5f),
                    Random.Range(0.06f, 0.14f), Random.Range(0.4f, 0.9f));
            }
            if (arrived)
            {
                _flashAt = at;
                _flashUntil = Time.time + FlashSeconds;
            }
        }

        /// <summary>One mote on a ring around the hero, rising and swirling; the ring tightens as the cast completes.</summary>
        void EmitRising(Vector3 feet, float progress)
        {
            float angle = Random.value * Mathf.PI * 2f;
            float radius = Mathf.Lerp(0.9f, 0.45f, progress);
            var around = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 swirl = Vector3.Cross(Vector3.up, around) * 1.2f;
            Emit(feet + around * radius + Vector3.up * Random.Range(0f, 0.4f), Vector3.up * Random.Range(1.2f, 2.4f) + swirl,
                Random.Range(0.05f, 0.11f), Random.Range(0.7f, 1.1f));
        }

        void Emit(Vector3 position, Vector3 velocity, float size, float lifetime)
        {
            var emit = new ParticleSystem.EmitParams
            {
                applyShapeToPosition = false,
                position = position,
                velocity = velocity,
                startSize = size,
                startLifetime = lifetime,
                startColor = Color.Lerp(glow, Color.white, Random.Range(0f, 0.4f)),
            };
            _motes.Emit(emit, 1);
        }

        void EnsureCreated()
        {
            if (_motes != null)
                return;

            var motesObject = new GameObject($"{name} Hearth Motes");
            _motes = motesObject.AddComponent<ParticleSystem>();
            _motes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = _motes.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.05f;
            main.maxParticles = 400;
            ParticleSystem.EmissionModule emission = _motes.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = _motes.shape;
            shape.enabled = false;
            ParticleSystem.SizeOverLifetimeModule size = _motes.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0f)));
            var renderer = motesObject.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = glowMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _motes.Play();

            _light = new GameObject($"{name} Hearth Light").AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = glow;
            _light.shadows = LightShadows.None;
            _light.enabled = false;
        }
    }
}
