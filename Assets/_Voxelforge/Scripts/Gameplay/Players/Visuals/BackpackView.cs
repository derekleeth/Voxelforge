using UnityEngine;
using UnityEngine.Rendering;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Shows the biggest bag a player wears (<see cref="PlayerBlockBag.Bags"/>, Roadmap 4.5) on their hero's back, on every
    /// PC, so you can see at a glance who's carrying the Delver's Rucksack. It hangs from the upper chest bone so it moves
    /// with the shoulders. Presentation only: what the bag holds is the host's business.
    /// </summary>
    [RequireComponent(typeof(PlayerBlockBag))]
    public sealed class BackpackView : MonoBehaviour
    {
        [Tooltip("The hero's humanoid Animator, for the chest bones.")]
        [SerializeField] Animator animator;

        [Tooltip("Where a bag's back (its model origin) sits, in the upper chest bone's space: on the Wayfarer's back at " +
                 "1.2 m, measured on the idle pose.")]
        [SerializeField] Vector3 backPosition = new Vector3(0.029f, -0.038f, -0.214f);

        [Tooltip("Turns the bag to face the way the hero faces, in the upper chest bone's space (measured on the idle pose).")]
        [SerializeField] Vector3 backRotation = new Vector3(358.2f, 354.8f, 359.4f);

        static readonly int ClassicDrawId = Shader.PropertyToID("_VoxelforgeClassicDraw");

        PlayerBlockBag _bag;
        MeshFilter _filter;
        MeshRenderer _renderer;
        ushort _shown = ushort.MaxValue;

        HeroBody _body;

        void Awake()
        {
            _bag = GetComponent<PlayerBlockBag>();
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

        /// <summary>A race's body (Roadmap 4.7, <see cref="HeroBody"/>): the bag sits a little differently on its back.</summary>
        void Fit(RaceLook look)
        {
            if (_filter == null)
                return;
            Transform t = _filter.transform;
            t.localPosition = backPosition + (look != null ? look.BackpackOffset : Vector3.zero);
            t.localScale = Vector3.one * (look != null ? look.BackpackScale : 1f);
        }

        void Start()
        {
            Transform bone = null;
            if (animator != null && animator.isHuman)
                bone = animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest) ??
                       animator.GetBoneTransform(HumanBodyBones.Spine);
            if (bone == null)
            {
                Debug.LogWarning($"{name}: no humanoid chest, so worn bags aren't shown.", this);
                enabled = false;
                return;
            }

            var model = new GameObject("Worn Bag");
            model.transform.SetParent(bone, false);
            model.transform.localPosition = backPosition;
            model.transform.localRotation = Quaternion.Euler(backRotation);
            _filter = model.AddComponent<MeshFilter>();
            _renderer = model.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = ShadowCastingMode.On;
            // Kept out of the GPU Resident Drawer like the held item, so HeroCameraFade can turn it to a shadow for one
            // squeezed camera at a time (Roadmap 3.7).
            var block = new MaterialPropertyBlock();
            block.SetFloat(ClassicDrawId, 1f);
            _renderer.SetPropertyBlock(block);
            model.SetActive(false);
            if (_body != null)
                Fit(_body.Look);
        }

        void OnDestroy()
        {
            if (_filter != null)
                Destroy(_filter.gameObject);
        }

        void LateUpdate()
        {
            ushort biggest = Biggest(_bag.Bags, _bag.Rules);
            if (biggest != _shown)
                Show(biggest);
        }

        /// <summary>The worn bag with the most slots (the first one on a tie), or 0 when none is worn.</summary>
        public static ushort Biggest(EquippedBags bags, IItemRules rules)
        {
            ushort biggest = 0;
            int most = 0;
            for (int i = 0; i < EquippedBags.SocketCount; i++)
            {
                int slots = bags[i] != 0 ? rules.BagSlotsOf(bags[i]) : 0;
                if (slots > most)
                {
                    most = slots;
                    biggest = bags[i];
                }
            }
            return biggest;
        }

        void Show(ushort itemId)
        {
            _shown = itemId;
            GameObject model = _filter.gameObject;
            ItemRegistry items = _bag.Items;
            if (itemId == 0 || items == null || !items.TryGet(itemId, out ItemDefinition item) || item.Mesh == null)
            {
                model.SetActive(false);
                return;
            }
            _filter.sharedMesh = item.Mesh;
            _renderer.sharedMaterial = item.Material;
            model.SetActive(true);
        }
    }
}
