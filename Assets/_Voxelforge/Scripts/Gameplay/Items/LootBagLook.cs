using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// How a personal loot sack looks (Roadmap 5.5, D-036), on every PC: dyed its owner's team colour so each couch player
    /// can spot their own, a soft light in the rarity's colour for Rare and up, and a pillar of light for Epic and
    /// Legendary that shows from across a fight. Looks only; <see cref="LootBag"/> holds the rules.
    /// </summary>
    [RequireComponent(typeof(LootBag))]
    public sealed class LootBagLook : MonoBehaviour
    {
        [Tooltip("The sack's renderers, dyed the owner's colour.")]
        [SerializeField] Renderer[] renderers;

        [Tooltip("An additive particle material for the light pillar (ManaBolt.mat).")]
        [SerializeField] Material beamMaterial;

        [Tooltip("How much of the owner's colour the sack takes, 0-1. Not all of it, so the cloth still reads.")]
        [SerializeField, Range(0f, 1f)] float dye = 0.7f;

        [SerializeField, Min(1f)] float epicBeamHeight = 5f;

        [SerializeField, Min(1f)] float legendaryBeamHeight = 9f;

        [SerializeField, Min(0.01f)] float beamWidth = 0.3f;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        LootBag _bag;
        PersonalSack _shown;
        bool _applied;
        MaterialPropertyBlock _block;
        LineRenderer _beam;
        Light _light;
        float _phase;

        void Awake()
        {
            _bag = GetComponent<LootBag>();
            _phase = Random.value * 10f;
        }

        void Update()
        {
            PersonalSack personal = _bag.Personal;
            if (!_applied || !personal.Equals(_shown))
                Apply(personal);
            if (_beam != null && _beam.enabled)
            {
                // A slow shimmer, so it reads as magic and not as a pole.
                float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 3f + _phase);
                Color c = RarityColors.Of(personal.Rarity);
                _beam.startColor = new Color(c.r, c.g, c.b, 0.85f * pulse);
                _beam.endColor = new Color(c.r, c.g, c.b, 0f);
                Vector3 bottom = transform.position;
                _beam.SetPosition(0, bottom);
                _beam.SetPosition(1, bottom + Vector3.up * (personal.Rarity == ItemRarity.Legendary ? legendaryBeamHeight : epicBeamHeight));
            }
            if (_light != null && _light.enabled)
                _light.intensity = 1.6f + 0.5f * Mathf.Sin(Time.time * 2.4f + _phase);
        }

        void Apply(PersonalSack personal)
        {
            _shown = personal;
            _applied = true;
            _block ??= new MaterialPropertyBlock();
            Color tint = personal.IsPersonal ? Color.Lerp(Color.white, (Color)personal.Tint, dye) : Color.white;
            foreach (Renderer r in renderers)
            {
                if (r == null)
                    continue;
                r.GetPropertyBlock(_block);
                _block.SetColor(BaseColor, tint);
                r.SetPropertyBlock(_block);
            }

            bool glows = personal.IsPersonal && personal.Rarity >= ItemRarity.Rare;
            if (glows && _light == null)
            {
                var go = new GameObject("Rarity Light");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(0f, 0.4f, 0f);
                _light = go.AddComponent<Light>();
                _light.type = LightType.Point;
                _light.range = 4f;
                _light.shadows = LightShadows.None;
            }
            if (_light != null)
            {
                _light.enabled = glows;
                _light.color = RarityColors.Of(personal.Rarity);
            }

            bool beams = personal.IsPersonal && personal.Rarity >= ItemRarity.Epic && beamMaterial != null;
            if (beams && _beam == null)
            {
                _beam = gameObject.AddComponent<LineRenderer>();
                _beam.sharedMaterial = beamMaterial;
                _beam.positionCount = 2;
                _beam.useWorldSpace = true;
                _beam.startWidth = beamWidth;
                _beam.endWidth = beamWidth * 0.4f;
                _beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _beam.receiveShadows = false;
            }
            if (_beam != null)
                _beam.enabled = beams;
        }
    }
}
