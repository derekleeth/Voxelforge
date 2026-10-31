using UnityEngine;
using UnityEngine.Rendering;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Shows what a player holds (<see cref="PlayerHands.HeldItem"/>, Roadmap 3.5) in their hero's right hand, on every
    /// PC: a tool or prop model where its <see cref="ItemDefinition"/> says, or a small block. While the host says they're
    /// mining (<see cref="PlayerMiner.IsMining"/>) it swings, and when a ranged item shoots (<see cref="PlayerAbilityCues"/>,
    /// Roadmap 3.6) it kicks back. An item with a <see cref="ItemDefinition.HeldLight"/> (a torch) lights up around the hero,
    /// flickering like a placed one. Presentation only.
    /// </summary>
    [RequireComponent(typeof(PlayerHands))]
    public sealed class HeldItemView : MonoBehaviour
    {
        [Tooltip("The hero's humanoid Animator, for the right hand bone.")]
        [SerializeField] Animator animator;

        [Tooltip("Where a held block sits, in the hand bone's space.")]
        [SerializeField] Vector3 blockPosition = new Vector3(0f, 0.12f, 0.04f);

        [Tooltip("How big a held block is, in metres.")]
        [SerializeField, Range(0.05f, 0.5f)] float blockSize = 0.22f;

        [Tooltip("How far the tool swings while mining, in degrees.")]
        [SerializeField, Range(0f, 120f)] float swingAngle = 55f;

        [Tooltip("Swings per second while mining.")]
        [SerializeField, Range(0.5f, 6f)] float swingRate = 2.5f;

        [Tooltip("The axis, in the hand bone's space, the tool swings around: the hero's right, measured on the Wayfarer's idle pose.")]
        [SerializeField] Vector3 swingAxis = new Vector3(0.589f, -0.009f, -0.808f);

        [Tooltip("How far a ranged item kicks up when it shoots, in degrees.")]
        [SerializeField, Range(0f, 45f)] float recoilAngle = 14f;

        [Tooltip("How fast the kick settles: higher is snappier.")]
        [SerializeField, Range(1f, 40f)] float recoilSettle = 14f;

        PlayerHands _hands;
        PlayerMiner _miner;
        PlayerController _player;
        PlayerAbilityCues _cues;
        Transform _hand;
        HeroBody _body;
        Transform _pivot;
        MeshFilter _filter;
        MeshRenderer _renderer;
        Light _light;
        float _flickerSeed;

        static readonly int ClassicDrawId = Shader.PropertyToID("_VoxelforgeClassicDraw");
        ushort _shown = ushort.MaxValue;
        float _swingTime;
        float _swingWeight;
        ItemDefinition _shownDefinition;

        void Awake()
        {
            _hands = GetComponent<PlayerHands>();
            _miner = GetComponent<PlayerMiner>();
            _player = GetComponent<PlayerController>();
            _cues = GetComponent<PlayerAbilityCues>();
            _body = GetComponent<HeroBody>();
        }

        void OnEnable()
        {
            if (_body != null)
                _body.BodyChanged += Fit;
        }

        void OnDisable()
        {
            if (_body != null)
                _body.BodyChanged -= Fit;
        }

        /// <summary>
        /// A race's body (Roadmap 4.7, <see cref="HeroBody"/>): the hand is a new bone (HeroBody has already moved the
        /// pivot onto it), and bigger hands hold bigger things.
        /// </summary>
        void Fit(RaceLook look)
        {
            if (animator != null && animator.isHuman)
                _hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (_pivot != null)
                _pivot.localScale = Vector3.one * (look != null ? look.HeldItemScale : 1f);
        }

        void Start()
        {
            _hand = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
            if (_hand == null)
            {
                Debug.LogWarning($"{name}: no humanoid right hand, so held items aren't shown.", this);
                enabled = false;
                return;
            }

            // A pivot at the grip swings; the model under it sits at its own pose.
            _pivot = new GameObject("Held Item Pivot").transform;
            _pivot.SetParent(_hand, false);
            var model = new GameObject("Held Item");
            model.transform.SetParent(_pivot, false);
            _filter = model.AddComponent<MeshFilter>();
            _renderer = model.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = ShadowCastingMode.On;
            // A property block keeps it out of the GPU Resident Drawer, which only picks up renderer changes once per
            // frame for every camera, so HeroCameraFade can hide it from one camera at a time (Roadmap 3.7).
            var block = new MaterialPropertyBlock();
            block.SetFloat(ClassicDrawId, 1f);
            _renderer.SetPropertyBlock(block);

            // Under the model, so the light's offset is in the model's own space and follows the swing.
            _light = new GameObject("Held Item Light").AddComponent<Light>();
            _light.transform.SetParent(model.transform, false);
            _light.type = LightType.Point;
            _light.shadows = LightShadows.None;
            _light.enabled = false;
            _flickerSeed = Random.Range(0f, 100f);
            model.SetActive(false);
            if (_body != null)
                Fit(_body.Look);
        }

        void OnDestroy()
        {
            if (_pivot != null)
                Destroy(_pivot.gameObject);
        }

        void LateUpdate()
        {
            ushort held = _hands.HeldItem;
            if (held != _shown)
                Show(held);

            // Ease in and out of the swing, so starting and stopping don't snap.
            bool mining = _miner != null && _miner.IsMining;
            _swingWeight = Mathf.MoveTowards(_swingWeight, mining ? 1f : 0f, Time.deltaTime * 6f);
            if (mining)
                _swingTime += Time.deltaTime;
            float swing = Mathf.Sin(_swingTime * swingRate * Mathf.PI * 2f) * 0.5f + 0.5f;
            float sinceShot = _cues != null ? Time.time - _cues.LastShotTime : float.PositiveInfinity;
            float recoil = recoilAngle * Mathf.Exp(-sinceShot * recoilSettle);
            _pivot.localRotation = Quaternion.AngleAxis(-swing * swingAngle * _swingWeight + recoil, swingAxis);

            if (_light.enabled)
            {
                // The same flame wobble as placed torches (PropLights).
                DecorationLight glow = _shownDefinition.HeldLight;
                float t = Time.time;
                float wobble = Mathf.PerlinNoise(t * 6f, _flickerSeed) * 0.7f + Mathf.PerlinNoise(t * 17f, _flickerSeed + 31f) * 0.3f;
                _light.intensity = glow.intensity * (1f + glow.flicker * (wobble - 0.5f));
            }
        }

        /// <summary>The hero's right hand bone, or null before <c>Start</c> or on a hero without one.</summary>
        public Transform Hand => _hand;

        /// <summary>
        /// Where a ranged item's bolt leaves the model in the world (<see cref="ItemDefinition.Muzzle"/>), and which way the
        /// barrel points. False when nothing ranged is shown.
        /// </summary>
        public bool TryGetMuzzle(out Vector3 position, out Vector3 forward)
        {
            position = forward = default;
            if (_filter == null || !_filter.gameObject.activeInHierarchy || _shownDefinition == null || !_shownDefinition.IsRanged)
                return false;
            Transform t = _filter.transform;
            position = t.TransformPoint(_shownDefinition.Muzzle);
            forward = t.TransformDirection(Vector3.up);
            return true;
        }

        void Show(ushort held)
        {
            _shown = held;
            _shownDefinition = null;
            _light.enabled = false;
            VoxelWorld world = _player != null ? _player.World : null;
            GameObject model = _filter.gameObject;
            if (held == BlockIds.Air || !_hands.Looks.TryGet(held, world != null ? world.Blocks : default, out ItemLook look))
            {
                model.SetActive(false);
                return;
            }

            _filter.sharedMesh = look.Mesh;
            _renderer.sharedMaterial = look.Material;
            Transform t = model.transform;
            if (look.IsModel)
            {
                _shownDefinition = look.Definition;
                t.localPosition = look.Definition.HeldPosition;
                t.localRotation = look.Definition.HeldRotation;
                t.localScale = Vector3.one * look.Definition.HeldScale;
                DecorationLight heldLight = look.Definition.HeldLight;
                if (heldLight.enabled && heldLight.intensity > 0f)
                {
                    _light.enabled = true;
                    _light.color = heldLight.color;
                    _light.intensity = heldLight.intensity;
                    // The model is scaled down in the hand; the light's reach shouldn't be.
                    _light.range = heldLight.range;
                    _light.transform.localPosition = heldLight.offset;
                }
            }
            else
            {
                t.localPosition = blockPosition;
                t.localRotation = Quaternion.identity;
                t.localScale = Vector3.one * blockSize;
            }
            model.SetActive(true);
        }
    }
}
