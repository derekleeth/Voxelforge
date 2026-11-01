using UnityEngine;
using UnityEngine.Rendering;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// What a mob's fights look like (Roadmap 5.2 step G), on every PC, from the cues the host sends (<see cref="Mob.Cued"/>)
    /// and its synced <see cref="Mob.State"/>:
    /// <list type="bullet">
    /// <item>A red flash over its whole body each time it's hurt.</item>
    /// <item>The charge telegraph: a red strip on the ground along the line it's about to run, pulsing, and dust kicked
    /// up by its pawing forefoot, so a hero has the windup to step aside. Dust trails behind it while it charges.</item>
    /// <item>A puff of blocky dust when a defeated mob leaves the world; a slime bursts into droplets as it splits.</item>
    /// </list>
    /// Presentation only: nothing here is sent. The particles are square, like the blocks. Voxelforge &gt; Rebuild Mob
    /// Models makes its materials and sets its colours.
    /// </summary>
    public sealed class MobEffects : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] Mob mob;

        [Tooltip("Art/Materials/MobFx/MobDust.mat: alpha-blended unlit particles, for the dust and droplets.")]
        [SerializeField] Material effectMaterial;

        [Tooltip("Art/Materials/MobFx/ChargeMark.mat: see-through unlit, for the charge strip on the ground.")]
        [SerializeField] Material markMaterial;

        [Tooltip("The colour it flashes when hurt.")]
        [SerializeField] Color hurtFlash = new Color(1f, 0.25f, 0.2f, 1f);

        [SerializeField, Range(0.02f, 0.5f)] float flashSeconds = 0.14f;

        [Tooltip("Its dust: kicked up by its feet and the puff it leaves when it goes.")]
        [SerializeField] Color dust = new Color(0.62f, 0.55f, 0.46f, 0.9f);

        [Tooltip("A slime's splash when it splits or dies. Clear (alpha 0) for no splash.")]
        [SerializeField] Color splash = new Color(0f, 0f, 0f, 0f);

        [Tooltip("The charge strip's colour.")]
        [SerializeField] Color chargeMark = new Color(1f, 0.12f, 0.08f, 0.5f);

        [Tooltip("How far ahead the charge strip reaches, in metres.")]
        [SerializeField, Min(1f)] float chargeMarkLength = 9f;

        Renderer[] _renderers;
        Color[][] _baseColors;
        MaterialPropertyBlock _block;
        float _flashUntil = float.NegativeInfinity;
        bool _flashing;
        ParticleSystem _dust;
        Transform _mark;
        Renderer _markRenderer;
        float _nextDust;

        void Awake()
        {
            if (mob == null)
                mob = GetComponentInParent<Mob>();
            _renderers = GetComponentsInChildren<Renderer>(true);
            _baseColors = new Color[_renderers.Length][];
            for (int i = 0; i < _renderers.Length; i++)
            {
                Material[] materials = _renderers[i].sharedMaterials;
                _baseColors[i] = new Color[materials.Length];
                for (int m = 0; m < materials.Length; m++)
                    _baseColors[i][m] = materials[m] != null && materials[m].HasProperty(BaseColorId)
                        ? materials[m].GetColor(BaseColorId)
                        : Color.white;
            }
            _block = new MaterialPropertyBlock();
        }

        void OnEnable()
        {
            if (mob == null)
                return;
            mob.Cued += OnCue;
            mob.Vanishing += OnVanishing;
        }

        void OnDisable()
        {
            if (mob != null)
            {
                mob.Cued -= OnCue;
                mob.Vanishing -= OnVanishing;
            }
            ShowFlash(false);
        }

        void OnCue(Mob _, MobCueKind kind, int attack)
        {
            switch (kind)
            {
                case MobCueKind.Hurt:
                case MobCueKind.Defeated:
                    _flashUntil = Time.time + flashSeconds;
                    if (kind == MobCueKind.Defeated && splash.a > 0f)
                        Burst(Centre(), splash, 28, 3.5f, 0.9f);
                    break;
                case MobCueKind.Dazed:
                    Burst(Front(0.6f), dust, 10, 2f, 0.6f);
                    break;
            }
        }

        void OnVanishing(Mob _) => Burst(Centre(), dust, 26, 1.6f, 0.9f);

        void Update()
        {
            if (mob == null)
                return;
            bool flash = Time.time < _flashUntil;
            if (flash != _flashing)
                ShowFlash(flash);

            MobState state = mob.State;
            bool telegraph = state == MobState.Windup && IsCharge(mob.CurrentAttack);
            ShowMark(telegraph);
            if (telegraph && Time.time >= _nextDust)
            {
                // The pawing forefoot (front right) kicks dust up and back.
                _nextDust = Time.time + 0.12f;
                Emit(transform.TransformPoint(new Vector3(0.2f, 0.05f, 0.35f)),
                    (-transform.forward * 1.2f + Vector3.up * 1.4f), dust, 3, 0.06f, 0.11f, 0.5f);
            }
            else if (state == MobState.Charge && Time.time >= _nextDust)
            {
                _nextDust = Time.time + 0.06f;
                Emit(transform.position + Vector3.up * 0.05f, Vector3.up * 0.8f - transform.forward * 0.5f, dust, 2, 0.08f, 0.16f, 0.6f);
            }
        }

        bool IsCharge(int attack)
        {
            MobAttack[] attacks = mob.Definition != null ? mob.Definition.Attacks : null;
            return attacks != null && attack >= 0 && attack < attacks.Length && attacks[attack].Kind == MobAttackKind.Charge;
        }

        Vector3 Centre() => (Vector3)mob.Feet + Vector3.up * mob.HalfExtents.y;

        Vector3 Front(float ahead) => transform.position + transform.forward * ahead + Vector3.up * 0.5f;

        void ShowFlash(bool on)
        {
            _flashing = on;
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer renderer = _renderers[i];
                if (renderer == null || renderer == _markRenderer || renderer is ParticleSystemRenderer)
                    continue;
                for (int m = 0; m < _baseColors[i].Length; m++)
                {
                    _block.Clear();
                    if (on)
                    {
                        Color base_ = _baseColors[i][m];
                        Color tint = Color.Lerp(base_, hurtFlash, 0.7f);
                        tint.a = base_.a;
                        _block.SetColor(BaseColorId, tint);
                    }
                    renderer.SetPropertyBlock(_block, m);
                }
            }
        }

        /// <summary>The red strip along the charge's line, pulsing; hidden when not winding up a charge.</summary>
        void ShowMark(bool on)
        {
            if (!on)
            {
                if (_mark != null && _mark.gameObject.activeSelf)
                    _mark.gameObject.SetActive(false);
                return;
            }
            if (_mark == null)
            {
                if (markMaterial == null)
                    return;
                GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Charge Mark";
                Destroy(quad.GetComponent<Collider>());
                _mark = quad.transform;
                _mark.SetParent(transform, false);
                _mark.localRotation = Quaternion.Euler(90f, 0f, 0f);
                _mark.localScale = new Vector3(1.1f, chargeMarkLength, 1f);
                _mark.localPosition = new Vector3(0f, 0.06f, chargeMarkLength * 0.5f + 0.4f);
                _markRenderer = quad.GetComponent<Renderer>();
                _markRenderer.sharedMaterial = markMaterial;
                _markRenderer.shadowCastingMode = ShadowCastingMode.Off;
                _markRenderer.receiveShadows = false;
            }
            if (!_mark.gameObject.activeSelf)
                _mark.gameObject.SetActive(true);
            Color colour = chargeMark;
            colour.a *= 0.6f + 0.4f * Mathf.Sin(Time.time * 14f);
            _block.Clear();
            _block.SetColor(BaseColorId, colour);
            _markRenderer.SetPropertyBlock(_block);
        }

        /// <summary>A burst of square puffs flying out from <paramref name="at"/>, left behind in the world.</summary>
        void Burst(Vector3 at, Color colour, int count, float speed, float lifetime)
        {
            ParticleSystem system = MakeSystem("Mob Puff", null);
            if (system == null)
                return;
            for (int i = 0; i < count; i++)
            {
                Vector3 direction = Random.onUnitSphere;
                direction.y = Mathf.Abs(direction.y) * 0.8f + 0.2f;
                system.Emit(new ParticleSystem.EmitParams
                {
                    applyShapeToPosition = false,
                    position = at + direction * 0.2f,
                    velocity = direction * speed * Random.Range(0.4f, 1f),
                    startSize = Random.Range(0.1f, 0.24f),
                    startLifetime = lifetime * Random.Range(0.6f, 1f),
                    startColor = Color.Lerp(colour, Color.white, Random.Range(0f, 0.25f)),
                    rotation = Random.Range(0f, 90f),
                }, 1);
            }
            Destroy(system.gameObject, lifetime + 0.2f);
        }

        void Emit(Vector3 at, Vector3 velocity, Color colour, int count, float minSize, float maxSize, float lifetime)
        {
            _dust ??= MakeSystem("Mob Dust", transform);
            if (_dust == null)
                return;
            for (int i = 0; i < count; i++)
            {
                _dust.Emit(new ParticleSystem.EmitParams
                {
                    applyShapeToPosition = false,
                    position = at + Random.insideUnitSphere * 0.1f,
                    velocity = velocity + Random.insideUnitSphere * 0.6f,
                    startSize = Random.Range(minSize, maxSize),
                    startLifetime = lifetime * Random.Range(0.6f, 1f),
                    startColor = colour,
                }, 1);
            }
        }

        /// <summary>A world-space particle system that only emits when told to; it outlives the mob if not parented.</summary>
        ParticleSystem MakeSystem(string name, Transform parent)
        {
            if (effectMaterial == null)
                return null;
            var go = new GameObject($"{this.name} {name}");
            if (parent != null)
                go.transform.SetParent(parent, false);
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = system.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.6f;
            main.maxParticles = 200;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;
            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(0.2f, 1f), new Keyframe(1f, 0.1f)));
            ParticleSystem.ColorOverLifetimeModule colour = system.colorOverLifetime;
            colour.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            colour.color = fade;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = effectMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            system.Play();
            return system;
        }
    }
}
