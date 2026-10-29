using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// How one kind of block sounds (Roadmap 3.9): stepping on it, hitting it, breaking it and placing it. Several
    /// blocks share one (Stone, Red Rock and Sandstone all sound like stone); the <see cref="SoundBook"/> says which.
    /// Built by <c>SfxBuilder</c> (Voxelforge &gt; Build Sound Effects).
    /// </summary>
    [CreateAssetMenu(menuName = "Voxelforge/Surface Sound", fileName = "NewSurfaceSound")]
    public sealed class SurfaceSound : ScriptableObject
    {
        [Tooltip("Footsteps. One is picked at random per step. Landing from a jump plays one lower and louder.")]
        [SerializeField] AudioClip[] steps;

        [Tooltip("Mining hits. They rise in pitch as the block cracks (Roadmap 3.2).")]
        [SerializeField] AudioClip[] hits;

        [SerializeField] AudioClip[] breaks;

        [SerializeField] AudioClip[] places;

        [Tooltip("Played over every hit, e.g. the Mana Crystal's chime. Optional.")]
        [SerializeField] AudioClip[] hitExtras;

        [Tooltip("Played over every break, e.g. the Mana Crystal's chord. Optional.")]
        [SerializeField] AudioClip[] breakExtras;

        [SerializeField, Range(0f, 2f)] float volume = 1f;

        [Tooltip("Random pitch change per sound, so repeats don't sound copied (0.08 = ±8%).")]
        [SerializeField, Range(0f, 0.5f)] float pitchJitter = 0.08f;

        [Tooltip("Hit pitch at the first crack stage.")]
        [SerializeField, Range(0.25f, 3f)] float hitPitchFirst = 0.9f;

        [Tooltip("Hit pitch at the last crack stage, just before the block breaks.")]
        [SerializeField, Range(0.25f, 3f)] float hitPitchLast = 1.35f;

        public AudioClip[] Steps => steps;
        public AudioClip[] Hits => hits;
        public AudioClip[] Breaks => breaks;
        public AudioClip[] Places => places;
        public AudioClip[] HitExtras => hitExtras;
        public AudioClip[] BreakExtras => breakExtras;
        public float Volume => volume;
        public float PitchJitter => pitchJitter;
        public float HitPitchFirst => hitPitchFirst;
        public float HitPitchLast => hitPitchLast;

        /// <summary>A random clip from <paramref name="clips"/>, or null if there are none.</summary>
        public static AudioClip Pick(AudioClip[] clips) =>
            clips == null || clips.Length == 0 ? null : clips[Random.Range(0, clips.Length)];

        /// <summary>1 ± <see cref="PitchJitter"/>.</summary>
        public float JitteredPitch(float pitch = 1f) => pitch * (1f + Random.Range(-pitchJitter, pitchJitter));
    }
}
