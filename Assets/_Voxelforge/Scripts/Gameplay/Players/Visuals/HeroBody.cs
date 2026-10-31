using System;
using System.Collections.Generic;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Draws the hero in their race's body (Roadmap 4.7), on every PC, from the synced <see cref="PlayerRace"/>.
    /// Presentation only: the host's rules never see it, and the collision box is the same for every race.
    /// <para>The Animator (with <see cref="HeroFootIK"/>) stays where it is; only its children, the skinned mesh and the
    /// skeleton, are swapped for the race's model, then it takes that model's Humanoid avatar and rebinds, so the shared
    /// <c>Hero.controller</c> plays the same clips. Anything hung on the old skeleton's bones (the held item, the worn
    /// bag) moves to the same bone of the new one, and <see cref="BodyChanged"/> tells the views that fit things to the
    /// body (<see cref="HeldItemView"/>, <see cref="BackpackView"/>, <see cref="CastingView"/>) to look again.</para>
    /// </summary>
    [RequireComponent(typeof(PlayerController), typeof(PlayerRace))]
    public sealed class HeroBody : MonoBehaviour
    {
        [Tooltip("Data/Races/RaceLookBook.asset: every race's body.")]
        [SerializeField] RaceLookBook looks;

        [Tooltip("The Animator on the hero model (Visual/Hero). Its children are the body that gets swapped.")]
        [SerializeField] Animator animator;

        [Tooltip("The race of the body the prefab is built with.")]
        [SerializeField] RaceId prefabRace = RaceId.Wayfarer;

        PlayerController _player;
        PlayerRace _race;
        readonly List<(HumanBodyBones Bone, Transform Thing)> _hung = new List<(HumanBodyBones, Transform)>();
        readonly List<Transform> _old = new List<Transform>();

        /// <summary>The race whose body is showing.</summary>
        public RaceId Showing { get; private set; }

        /// <summary>The look of the body showing, or null while it's the prefab's own and the book has none for it.</summary>
        public RaceLook Look { get; private set; }

        public Animator Animator => animator;

        /// <summary>The body changed: re-find bones, and fit things to <see cref="Look"/>.</summary>
        public event Action<RaceLook> BodyChanged;

        void Awake()
        {
            _player = GetComponent<PlayerController>();
            _race = GetComponent<PlayerRace>();
            Showing = prefabRace;
            if (looks != null && looks.TryGet(prefabRace, out RaceLook look))
                Look = look;
        }

        void OnEnable() => _race.Changed += Show;

        void OnDisable() => _race.Changed -= Show;

        // The race arrives with the spawn on a remote PC, and just after it on the host: check every frame until it's
        // showing, then only on Changed. Cheap: one comparison.
        void Update()
        {
            if (_race.IsSpawned && _race.Race != Showing)
                Show(_race.Race);
        }

        /// <summary>Puts the hero in <paramref name="race"/>'s body. Does nothing if it's already showing.</summary>
        public void Show(RaceId race)
        {
            if (race == Showing || animator == null || looks == null || !looks.TryGet(race, out RaceLook look) ||
                look.Body == null || look.Avatar == null)
                return;

            Transform root = animator.transform;
            // 1. Unhook what's hanging on the old bones (anything on a bone that isn't itself a bone of the model).
            _hung.Clear();
            if (animator.isHuman)
            {
                for (var bone = HumanBodyBones.Hips; bone < HumanBodyBones.LastBone; bone++)
                {
                    Transform t = animator.GetBoneTransform(bone);
                    if (t == null)
                        continue;
                    for (int i = t.childCount - 1; i >= 0; i--)
                    {
                        Transform child = t.GetChild(i);
                        if (child.name.StartsWith("mixamorig:", StringComparison.Ordinal))
                            continue;
                        _hung.Add((bone, child));
                        child.SetParent(root, false);
                    }
                }
            }

            // 2. Take the old body out at once (Destroy waits for the frame's end, and Rebind mustn't find it).
            SkinnedMeshRenderer oldSkin = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var skinSettings = oldSkin != null
                ? (oldSkin.shadowCastingMode, oldSkin.updateWhenOffscreen, oldSkin.renderingLayerMask)
                : (UnityEngine.Rendering.ShadowCastingMode.On, false, 1u);
            _old.Clear();
            foreach (Transform child in root)
            {
                if (!_hung.Exists(h => h.Thing == child))
                    _old.Add(child);
            }
            foreach (Transform child in _old)
            {
                child.gameObject.SetActive(false);
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }

            // 3. Bring in the new body: the model's children, under the same Animator, on the hero's layer.
            GameObject model = Instantiate(look.Body);
            var parts = new List<Transform>();
            foreach (Transform child in model.transform)
                parts.Add(child);
            foreach (Transform child in parts)
            {
                child.SetParent(root, false);
                SetLayer(child, root.gameObject.layer);
            }
            Destroy(model);

            animator.avatar = look.Avatar;
            animator.Rebind();

            // 4. Hang everything back on the same bones of the new skeleton.
            foreach ((HumanBodyBones bone, Transform thing) in _hung)
            {
                Transform t = animator.GetBoneTransform(bone);
                if (t != null)
                    thing.SetParent(t, false);
            }

            SkinnedMeshRenderer skin = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (skin != null)
            {
                skin.shadowCastingMode = skinSettings.Item1;
                skin.updateWhenOffscreen = skinSettings.Item2;
                skin.renderingLayerMask = skinSettings.Item3;
                _player.SetTeamColorRenderer(skin, look.TeamClothSlot);
            }

            Showing = race;
            Look = look;
            BodyChanged?.Invoke(look);
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform child in t)
                SetLayer(child, layer);
        }
    }
}
