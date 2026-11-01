using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// The level-up celebration on the hero (Roadmap 5.6), on every PC so a partner sees it too: a golden pillar of light
    /// rising out of the ground round them and a warm glow, for a moment. Presentation only; <see cref="PlayerLevel"/> says
    /// when. The words ("LEVEL UP!") are in the hero's own view, and <see cref="PlayerFoley"/> plays the fanfare.
    /// </summary>
    [RequireComponent(typeof(PlayerLevel))]
    public sealed class LevelUpGlow : MonoBehaviour
    {
        [Tooltip("An additive particle material for the pillar (ManaBolt.mat).")]
        [SerializeField] Material beamMaterial;

        [SerializeField] Color colour = new Color(1f, 0.8f, 0.3f);

        [SerializeField, Min(0.1f)] float seconds = 1.6f;

        [SerializeField, Min(0.5f)] float height = 6f;

        [SerializeField, Min(0.1f)] float width = 1.4f;

        PlayerLevel _level;
        LineRenderer _beam;
        Light _light;
        float _startedAt = float.NegativeInfinity;

        void Awake() => _level = GetComponent<PlayerLevel>();

        void OnEnable() => _level.LeveledUp += OnLeveledUp;

        void OnDisable() => _level.LeveledUp -= OnLeveledUp;

        void OnLeveledUp(int level)
        {
            if (_beam == null)
                Make();
            _startedAt = Time.time;
            _beam.enabled = true;
            _light.enabled = true;
        }

        void Make()
        {
            var go = new GameObject("Level Up Glow");
            go.transform.SetParent(transform, false);
            _beam = go.AddComponent<LineRenderer>();
            _beam.sharedMaterial = beamMaterial;
            _beam.positionCount = 2;
            _beam.useWorldSpace = true;
            _beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _beam.receiveShadows = false;
            _light = go.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = colour;
            _light.range = 6f;
            _light.shadows = LightShadows.None;
            go.transform.localPosition = new Vector3(0f, 1f, 0f);
        }

        void LateUpdate()
        {
            if (_beam == null || !_beam.enabled)
                return;
            float t = (Time.time - _startedAt) / seconds;
            if (t >= 1f)
            {
                _beam.enabled = false;
                _light.enabled = false;
                return;
            }
            // Shoots up fast, then fades and narrows.
            float rise = Mathf.Clamp01(t * 4f);
            float fade = 1f - Mathf.SmoothStep(0.4f, 1f, t);
            Vector3 feet = transform.position;
            _beam.SetPosition(0, feet);
            _beam.SetPosition(1, feet + Vector3.up * (height * rise));
            _beam.startWidth = width * (0.6f + 0.4f * fade);
            _beam.endWidth = width * 0.25f * fade;
            _beam.startColor = new Color(colour.r, colour.g, colour.b, 0.9f * fade);
            _beam.endColor = new Color(colour.r, colour.g, colour.b, 0f);
            _light.intensity = 4f * fade;
        }
    }
}
