using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Shows the gear a player wears (<see cref="PlayerBlockBag.Gear"/>, Roadmap 5.5) on their hero, on every PC: a helm on
    /// the head, armour on the chest, a thigh guard on each leg and a boot on each foot, the main-hand weapon slung across
    /// the back and the off-hand on the left forearm. Stand-in models until 5.9's art. Presentation only.
    /// <para>Each piece hangs on a bone (so <see cref="HeroBody"/> carries it over to a new race's body). Where it sits is
    /// set as if the hero stood in its T-pose facing +Z (<see cref="Mount"/>), and worked out from that body's own bind
    /// pose, so every race's skeleton gets a fit without being measured.</para>
    /// </summary>
    [RequireComponent(typeof(PlayerBlockBag))]
    public sealed class GearView : MonoBehaviour
    {
        /// <summary>
        /// Where a piece sits, in the hero's own space in their T-pose (facing +Z, metres): <see cref="Offset"/> from the
        /// bone, turned by <see cref="Rotation"/>. Scaled with the race's body (<see cref="RaceLook.BackpackScale"/>).
        /// </summary>
        [Serializable]
        public struct Mount
        {
            public HumanBodyBones Bone;
            public Vector3 Offset;
            public Vector3 Rotation;
            [Min(0.01f)] public float Scale;

            public Mount(HumanBodyBones bone, Vector3 offset, Vector3 rotation, float scale)
            {
                Bone = bone;
                Offset = offset;
                Rotation = rotation;
                Scale = scale;
            }
        }

        [Tooltip("The hero's humanoid Animator (Visual/Hero).")]
        [SerializeField] Animator animator;

        [SerializeField] Mount head = new Mount(HumanBodyBones.Head, new Vector3(0f, 0.2f, 0f), Vector3.zero, 1.25f);
        [SerializeField] Mount chest = new Mount(HumanBodyBones.Spine, new Vector3(0f, -0.09f, 0f), Vector3.zero, 1.1f);
        [Tooltip("Leg armour shows as a guard on each shin: the Wayfarer's robe hides the thighs.")]
        [SerializeField] Mount leftLeg = new Mount(HumanBodyBones.LeftLowerLeg, new Vector3(0f, 0.02f, 0.01f), Vector3.zero, 1.25f);
        [SerializeField] Mount rightLeg = new Mount(HumanBodyBones.RightLowerLeg, new Vector3(0f, 0.02f, 0.01f), Vector3.zero, 1.25f);
        [Tooltip("Big enough to go over the hero's own chunky boots.")]
        [SerializeField] Mount leftFoot = new Mount(HumanBodyBones.LeftFoot, new Vector3(0f, -0.215f, 0.05f), Vector3.zero, 1.45f);
        [SerializeField] Mount rightFoot = new Mount(HumanBodyBones.RightFoot, new Vector3(0f, -0.215f, 0.05f), Vector3.zero, 1.45f);

        [Tooltip("The main-hand weapon slung across the back, its head up over the right shoulder.")]
        [SerializeField] Mount mainHand = new Mount(HumanBodyBones.UpperChest, new Vector3(-0.15f, -0.28f, -0.2f),
            new Vector3(0f, 0f, -30f), 1f);

        [Tooltip("The off-hand on the outside of the left forearm. In the T-pose the arm points left, so its face points up " +
                 "and its top points in toward the elbow: with the arms down, it faces out and stands upright.")]
        [SerializeField] Mount offHand = new Mount(HumanBodyBones.LeftLowerArm, new Vector3(-0.1f, 0.07f, 0f),
            new Vector3(270f, 270f, 0f), 0.8f);

        static readonly int ClassicDrawId = Shader.PropertyToID("_VoxelforgeClassicDraw");

        // One renderer per piece: legs and feet have two (left, right), the rest one.
        sealed class Piece
        {
            public int Socket;
            public Mount Mount;
            public Transform Transform;
            public MeshFilter Filter;
            public MeshRenderer Renderer;
        }

        PlayerBlockBag _bag;
        PlayerWeapon _weapon;
        HeroBody _body;
        Piece[] _pieces;
        EquippedGear _shown;
        bool _shownAny;

        void Awake()
        {
            _bag = GetComponent<PlayerBlockBag>();
            _weapon = GetComponent<PlayerWeapon>();
            _body = GetComponent<HeroBody>();
        }

        void OnEnable()
        {
            if (_body != null)
                _body.BodyChanged += OnBodyChanged;
        }

        void OnDisable()
        {
            if (_body != null)
                _body.BodyChanged -= OnBodyChanged;
        }

        void Start()
        {
            if (animator == null || !animator.isHuman)
            {
                Debug.LogWarning($"{name}: no humanoid hero, so worn gear isn't shown.", this);
                enabled = false;
                return;
            }
            _pieces = new[]
            {
                Make(0, head), Make(1, chest), Make(2, leftLeg), Make(2, rightLeg), Make(3, leftFoot), Make(3, rightFoot),
                Make(4, mainHand), Make(5, offHand),
            };
            Fit(_body != null ? _body.Look : null);
        }

        void OnDestroy()
        {
            if (_pieces == null)
                return;
            foreach (Piece piece in _pieces)
            {
                if (piece.Transform != null)
                    Destroy(piece.Transform.gameObject);
            }
        }

        Piece Make(int socket, Mount mount)
        {
            var model = new GameObject($"Worn {EquippedGear.SlotOf(socket)} ({mount.Bone})");
            var piece = new Piece
            {
                Socket = socket, Mount = mount, Transform = model.transform, Filter = model.AddComponent<MeshFilter>(),
                Renderer = model.AddComponent<MeshRenderer>(),
            };
            piece.Renderer.shadowCastingMode = ShadowCastingMode.On;
            // Kept out of the GPU Resident Drawer like the held item and the worn bag, so HeroCameraFade can turn it to a
            // shadow for one squeezed camera at a time (Roadmap 3.7).
            var block = new MaterialPropertyBlock();
            block.SetFloat(ClassicDrawId, 1f);
            piece.Renderer.SetPropertyBlock(block);
            model.layer = animator.gameObject.layer;
            model.SetActive(false);
            return piece;
        }

        void OnBodyChanged(RaceLook look) => Fit(look);

        /// <summary>Hangs every piece on its bone of the body showing, placed from that body's bind pose.</summary>
        void Fit(RaceLook look)
        {
            if (_pieces == null)
                return;
            float raceScale = look != null ? look.BackpackScale : 1f;
            SkinnedMeshRenderer skin = animator.GetComponentInChildren<SkinnedMeshRenderer>();
            foreach (Piece piece in _pieces)
            {
                Transform bone = animator.GetBoneTransform(piece.Mount.Bone);
                if (bone == null)
                {
                    piece.Transform.gameObject.SetActive(false);
                    continue;
                }
                Matrix4x4 bind = BindPose(skin, bone);
                Vector3 bonePosition = bind.GetColumn(3);
                Quaternion boneRotation = Quaternion.LookRotation(bind.GetColumn(2), bind.GetColumn(1));
                Vector3 target = bonePosition + piece.Mount.Offset * raceScale;
                piece.Transform.SetParent(bone, false);
                piece.Transform.localPosition = Quaternion.Inverse(boneRotation) * (target - bonePosition);
                piece.Transform.localRotation = Quaternion.Inverse(boneRotation) * Quaternion.Euler(piece.Mount.Rotation);
                piece.Transform.localScale = Vector3.one * (piece.Mount.Scale * raceScale);
            }
        }

        /// <summary>
        /// The bone's place and turn in the T-pose, in the hero model's space (the Animator's): from the skin's bind poses,
        /// or where it is now if the skin doesn't use it.
        /// </summary>
        Matrix4x4 BindPose(SkinnedMeshRenderer skin, Transform bone)
        {
            Matrix4x4 toHero = animator.transform.worldToLocalMatrix;
            if (skin != null && skin.sharedMesh != null)
            {
                Transform[] bones = skin.bones;
                Matrix4x4[] binds = skin.sharedMesh.bindposes;
                for (int i = 0; i < bones.Length && i < binds.Length; i++)
                {
                    if (bones[i] == bone)
                        return toHero * skin.transform.localToWorldMatrix * binds[i].inverse;
                }
            }
            return toHero * bone.localToWorldMatrix;
        }

        void LateUpdate()
        {
            EquippedGear gear = _bag.Gear;
            bool fighting = _weapon != null && _weapon.IsAttacking;
            if (_shownAny && gear.Equals(_shown) && fighting == _drawn)
                return;
            _drawn = fighting;
            Show(gear);
        }

        // The main-hand weapon is in the hero's hand while they fight with it (step G), so not on their back.
        bool _drawn;

        /// <summary>Editor checks only: dresses a hero placed outside play mode (still in its T-pose) in <paramref name="gear"/>.</summary>
        internal void Preview(EquippedGear gear)
        {
            if (_bag == null)
                Awake();
            if (_pieces == null)
                Start();
            Show(gear);
        }

        void Show(EquippedGear gear)
        {
            if (_pieces == null)
                return;
            _shown = gear;
            _shownAny = true;
            ItemRegistry items = _bag.Items;
            foreach (Piece piece in _pieces)
            {
                ushort itemId = gear[piece.Socket];
                bool show = itemId != 0 && items != null && items.TryGet(itemId, out ItemDefinition item) && item.WornMesh != null;
                if (show)
                {
                    items.TryGet(itemId, out ItemDefinition definition);
                    piece.Filter.sharedMesh = definition.WornMesh;
                    piece.Renderer.sharedMaterial = definition.Material;
                }
                piece.Transform.gameObject.SetActive(show && !(_drawn && piece.Socket == EquippedGear.SocketOf(EquipSlot.MainHand)));
            }
        }
    }
}
