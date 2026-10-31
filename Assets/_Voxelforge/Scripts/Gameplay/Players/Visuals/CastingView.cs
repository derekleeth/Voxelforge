using UnityEngine;
using UnityEngine.Rendering;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Draws a hero casting on every PC (Roadmap 4.2): while a spell with a cast time is being cast
    /// (<see cref="PlayerCaster.Casting"/>), motes swirl in around both hands and a light grows, in the spell's
    /// <see cref="AbilityLook"/> colour. When it goes off the hands flash; if it's stopped the motes puff away.
    /// Presentation only: it reads what the host said through <see cref="PlayerAbilityCues"/>.
    /// </summary>
    [RequireComponent(typeof(PlayerCaster), typeof(PlayerAbilityCues))]
    public sealed class CastingView : MonoBehaviour
    {
        [Tooltip("Additive glow material for the motes (Materials/ManaBolt.mat).")]
        [SerializeField] Material glowMaterial;

        [Tooltip("Data/Abilities/Looks/AbilityLookBook.asset: how each ability looks.")]
        [SerializeField] AbilityLookBook looks;

        [Tooltip("The hero's humanoid Animator, for the hand bones.")]
        [SerializeField] Animator animator;

        const float FlashSeconds = 0.25f;

        PlayerCaster _caster;
        PlayerAbilityCues _cues;
        PlayerController _player;
        Transform _leftHand;
        Transform _rightHand;
        HeroBody _body;
        ParticleSystem _motes;
        Light _light;
        float _emitCarry;
        float _flashUntil = float.NegativeInfinity;
        float _flashLight;
        Color _glow = Color.white;

        void Awake()
        {
            _caster = GetComponent<PlayerCaster>();
            _cues = GetComponent<PlayerAbilityCues>();
            _player = GetComponent<PlayerController>();
            _body = GetComponent<HeroBody>();
        }

        void Start() => FindHands(null);

        /// <summary>The hands the motes gather in; found again when the body changes (Roadmap 4.7, <see cref="HeroBody"/>).</summary>
        void FindHands(RaceLook look)
        {
            if (animator != null && animator.isHuman)
            {
                _leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                _rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            }
        }

        void OnEnable()
        {
            _cues.Received += OnCue;
            if (_body != null)
                _body.BodyChanged += FindHands;
        }

        void OnDisable()
        {
            _cues.Received -= OnCue;
            if (_body != null)
                _body.BodyChanged -= FindHands;
        }

        void OnDestroy()
        {
            if (_motes != null)
                Destroy(_motes.gameObject);
            if (_light != null)
                Destroy(_light.gameObject);
        }

        AbilityLook LookFor(AbilityDefinition ability) => looks != null ? looks.For(ability) : null;

        void LateUpdate()
        {
            AbilityDefinition casting = _caster.Casting;
            bool flashing = Time.time < _flashUntil;
            if (casting == null && !flashing)
            {
                if (_light != null && _light.enabled)
                    _light.enabled = false;
                return;
            }
            EnsureCreated();

            if (casting != null)
            {
                AbilityLook look = LookFor(casting);
                if (look == null)
                    return;
                _glow = look.Glow;
                float progress = _caster.CastProgress;
                _emitCarry += look.CastMotesPerSecond * (0.4f + progress) * Time.deltaTime;
                for (; _emitCarry >= 1f; _emitCarry -= 1f)
                    EmitInward(Random.value < 0.5f ? Hand(_leftHand, -1f) : Hand(_rightHand, 1f), progress);
                _light.transform.position = Between();
                _light.color = _glow;
                _light.intensity = look.CastLight * progress;
                _light.range = 2f + 3f * progress;
            }
            else
            {
                float left = (_flashUntil - Time.time) / FlashSeconds;
                _light.transform.position = Between();
                _light.intensity = _flashLight * 2f * left;
                _light.range = 5f;
            }
            _light.enabled = true;
        }

        void OnCue(AbilityCue cue, AbilityDefinition ability)
        {
            // Only spells with a cast time; an instant one's muzzle flash says enough.
            if (ability.IsInstant)
                return;
            AbilityLook look = LookFor(ability);
            if (look == null)
                return;
            if (cue.Kind == AbilityCueKind.Released)
            {
                EnsureCreated();
                _glow = look.Glow;
                _light.color = _glow;
                _flashLight = look.CastLight;
                _flashUntil = Time.time + FlashSeconds;
                Burst(look.Glow, 24, 3f);
            }
            else if (cue.Kind == AbilityCueKind.CastStopped)
            {
                EnsureCreated();
                Burst(look.Glow, 12, 1.2f);
            }
        }

        /// <summary>A hand's position, or a guess beside the chest on a hero without hand bones.</summary>
        Vector3 Hand(Transform bone, float side) =>
            bone != null ? bone.position : _player.VisualRoot.position + Vector3.up * 1.1f + _player.VisualRoot.right * (0.3f * side);

        Vector3 Between() => (Hand(_leftHand, -1f) + Hand(_rightHand, 1f)) * 0.5f;

        /// <summary>One mote on a shell around a hand, drifting in; the shell tightens as the cast completes.</summary>
        void EmitInward(Vector3 hand, float progress)
        {
            Vector3 from = Random.onUnitSphere * Mathf.Lerp(0.45f, 0.2f, progress);
            Vector3 swirl = Vector3.Cross(Vector3.up, from).normalized * 0.6f;
            Emit(hand + from, -from * 2.5f + swirl, Random.Range(0.03f, 0.07f), Random.Range(0.25f, 0.4f), _glow);
        }

        void Burst(Color glow, int count, float speed)
        {
            Vector3 at = Between();
            for (int i = 0; i < count; i++)
                Emit(at, Random.onUnitSphere * Random.Range(0.4f, 1f) * speed, Random.Range(0.04f, 0.09f),
                    Random.Range(0.3f, 0.6f), glow);
        }

        void Emit(Vector3 position, Vector3 velocity, float size, float lifetime, Color glow)
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

            var motesObject = new GameObject($"{name} Cast Motes");
            _motes = motesObject.AddComponent<ParticleSystem>();
            _motes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = _motes.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;
            main.maxParticles = 300;
            ParticleSystem.EmissionModule emission = _motes.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = _motes.shape;
            shape.enabled = false;
            ParticleSystem.SizeOverLifetimeModule size = _motes.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.4f, 1f), new Keyframe(1f, 0f)));
            var renderer = motesObject.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = glowMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _motes.Play();

            _light = new GameObject($"{name} Cast Light").AddComponent<Light>();
            _light.type = LightType.Point;
            _light.shadows = LightShadows.None;
            _light.enabled = false;
        }
    }
}
