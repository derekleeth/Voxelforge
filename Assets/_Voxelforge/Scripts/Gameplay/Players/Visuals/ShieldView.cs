using UnityEngine;
using UnityEngine.Rendering;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Draws a hero's shield on every PC (Roadmap 4.6, Bulwark Rune): a faint golden bubble with two rune rings turning
    /// round the hero while <see cref="PlayerHealth.Shield"/> is up, in the colour of the spell that gave it. It flares
    /// when the shield goes on or soaks up a hit, and fades when it runs out or breaks. Looks only (Roadmap 4.2).
    /// </summary>
    [RequireComponent(typeof(PlayerHealth))]
    public sealed class ShieldView : MonoBehaviour
    {
        [Tooltip("Data/Abilities/Looks/AbilityLookBook.asset: the shield takes its spell's colour.")]
        [SerializeField] AbilityLookBook looks;

        [Tooltip("Art/Materials/Props/ManaBolt.mat: additive glow.")]
        [SerializeField] Material glowMaterial;

        [Tooltip("How far the bubble reaches from the hero's middle, in metres.")]
        [SerializeField, Range(0.5f, 2f)] float radius = 1.05f;

        [SerializeField, Range(0f, 1f)] float bubbleAlpha = 0.14f;

        const float Middle = 0.95f;
        const float FadeSeconds = 0.3f;
        const float FlareSeconds = 0.4f;
        const int RingPoints = 48;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        PlayerHealth _health;
        Transform _root;
        MeshRenderer _bubble;
        LineRenderer[] _rings;
        MaterialPropertyBlock _block;
        Color _glow = new Color(1f, 0.82f, 0.35f);
        float _shown;
        float _flaredAt = float.NegativeInfinity;

        void Awake() => _health = GetComponent<PlayerHealth>();

        void OnEnable() => _health.Changed += OnHealth;

        void OnDisable() => _health.Changed -= OnHealth;

        void OnDestroy()
        {
            if (_root != null)
                Destroy(_root.gameObject);
        }

        void OnHealth(HealthEvent e)
        {
            if (e.Kind != HealthEventKind.Shielded && e.Kind != HealthEventKind.Absorbed)
                return;
            _flaredAt = Time.time;
            if (e.Kind == HealthEventKind.Shielded && looks != null && looks.TryGet(e.AbilityId, out AbilityLook look))
                _glow = look.Glow;
        }

        void Build()
        {
            _root = new GameObject("Shield").transform;
            _root.SetParent(transform, false);
            _root.localPosition = new Vector3(0f, Middle, 0f);

            var bubble = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(bubble.GetComponent<Collider>());
            bubble.name = "Bubble";
            bubble.transform.SetParent(_root, false);
            bubble.transform.localScale = Vector3.one * radius * 2f;
            _bubble = bubble.GetComponent<MeshRenderer>();
            _bubble.sharedMaterial = glowMaterial;
            _bubble.shadowCastingMode = ShadowCastingMode.Off;
            _bubble.receiveShadows = false;
            _block = new MaterialPropertyBlock();

            _rings = new[] { Ring("Rune Ring A", 0.95f), Ring("Rune Ring B", 0.8f) };
            _root.gameObject.SetActive(false);
        }

        LineRenderer Ring(string name, float scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = glowMaterial;
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = RingPoints;
            line.widthMultiplier = 0.05f;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            for (int i = 0; i < RingPoints; i++)
            {
                float a = i * Mathf.PI * 2f / RingPoints;
                // A wavy ring: the bumps read as runes going round.
                float r = radius * scale * (1f + 0.04f * Mathf.Sin(a * 9f));
                line.SetPosition(i, new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
            }
            return line;
        }

        void LateUpdate()
        {
            bool up = _health.Shield > 0f;
            _shown = Mathf.MoveTowards(_shown, up ? 1f : 0f, Time.deltaTime / FadeSeconds);
            if (_shown <= 0f)
            {
                if (_root != null && _root.gameObject.activeSelf)
                    _root.gameObject.SetActive(false);
                return;
            }
            if (_root == null)
                Build();
            if (!_root.gameObject.activeSelf)
                _root.gameObject.SetActive(true);

            float flare = Mathf.Clamp01(1f - (Time.time - _flaredAt) / FlareSeconds);
            float size = 1f + 0.12f * flare;
            _root.localScale = new Vector3(size, size, size);

            Color bubble = _glow * (1f + flare * 2f);
            bubble.a = (bubbleAlpha + flare * 0.25f) * _shown;
            _block.SetColor(BaseColor, bubble * bubble.a);
            _bubble.SetPropertyBlock(_block);

            Color ring = _glow * (2.5f + flare * 3f) * _shown;
            ring.a = _shown;
            float t = Time.time;
            _rings[0].transform.localRotation = Quaternion.Euler(18f, t * 70f, 0f);
            _rings[1].transform.localRotation = Quaternion.Euler(-24f, -t * 55f, 10f);
            foreach (LineRenderer line in _rings)
                line.startColor = line.endColor = ring;
        }
    }
}
