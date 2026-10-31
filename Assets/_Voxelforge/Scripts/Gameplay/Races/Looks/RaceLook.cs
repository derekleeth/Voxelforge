using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// How one race looks (Roadmap 4.7): the body every PC draws for a hero of that race (<see cref="HeroBody"/>) and how
    /// what they wear and hold sits on it. Presentation only (Roadmap 4.2): the rules are in its
    /// <see cref="RaceDefinition"/>, and the race window's crest and portrait are in the <see cref="HudSkin"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxelforge/Race Look", fileName = "NewRaceLook")]
    public sealed class RaceLook : ScriptableObject
    {
        [SerializeField] RaceId race;

        [Tooltip("The rigged model (an imported FBX, Humanoid, from a Blender build script). Its children replace the " +
                 "hero's body under their Animator.")]
        [SerializeField] GameObject body;

        [Tooltip("The model's Humanoid avatar, so the shared Hero.controller's clips play on it.")]
        [SerializeField] Avatar avatar;

        [Tooltip("Which material slot of the body's mesh is the team-colour cloth.")]
        [SerializeField, Min(0)] int teamClothSlot = 1;

        [Header("What they wear and hold")]
        [Tooltip("Moves the worn bag on the chest bone, on top of BackpackView's own spot (a broader back sits further out).")]
        [SerializeField] Vector3 backpackOffset;

        [SerializeField, Range(0.5f, 2f)] float backpackScale = 1f;

        [Tooltip("Scales what's in their hand (bigger hands hold bigger tools).")]
        [SerializeField, Range(0.5f, 2f)] float heldItemScale = 1f;

        [Header("Footsteps")]
        [Tooltip("Pitch of their footsteps: below 1 is heavier.")]
        [SerializeField, Range(0.5f, 1.5f)] float footstepPitch = 1f;

        [Tooltip("Loudness of their footsteps.")]
        [SerializeField, Range(0.5f, 2f)] float footstepVolume = 1f;

        public RaceId Race => race;
        public GameObject Body => body;
        public Avatar Avatar => avatar;
        public int TeamClothSlot => teamClothSlot;
        public Vector3 BackpackOffset => backpackOffset;
        public float BackpackScale => backpackScale;
        public float HeldItemScale => heldItemScale;
        public float FootstepPitch => footstepPitch;
        public float FootstepVolume => footstepVolume;
    }
}
