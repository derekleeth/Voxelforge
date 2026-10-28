using UnityEngine;
using UnityEngine.Rendering;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Shows what a player holds (<see cref="PlayerHands.HeldItem"/>, Roadmap 3.5) in their hero's right hand, on every
    /// PC: a tool or prop model where its <see cref="ItemDefinition"/> says, or a small block. While the host says they're
    /// mining (<see cref="PlayerMiner.IsMining"/>) it swings. Presentation only.
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

        PlayerHands _hands;
        PlayerMiner _miner;
        PlayerController _player;
        Transform _hand;
        Transform _pivot;
        MeshFilter _filter;
        MeshRenderer _renderer;
        ushort _shown = ushort.MaxValue;
        float _swingTime;
        float _swingWeight;

        void Awake()
        {
            _hands = GetComponent<PlayerHands>();
            _miner = GetComponent<PlayerMiner>();
            _player = GetComponent<PlayerController>();
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
            model.SetActive(false);
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
            _pivot.localRotation = Quaternion.AngleAxis(-swing * swingAngle * _swingWeight, swingAxis);
        }

        void Show(ushort held)
        {
            _shown = held;
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
                t.localPosition = look.Definition.HeldPosition;
                t.localRotation = look.Definition.HeldRotation;
                t.localScale = Vector3.one * look.Definition.HeldScale;
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
