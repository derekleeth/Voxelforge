using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Plays a mob's clips (Roadmap 5.2 step E) from what every PC already has: the synced transform, <see cref="Mob.State"/>
    /// and <see cref="Mob.CurrentAttack"/>, so nothing more is sent for it. Walking and running blend on how fast it
    /// really moves; the other states cross-fade to their own clip: a boar paws before a charge and rears before a gore,
    /// a wolf howls, and a defeated mob falls over and stays down. Its Animator is built by
    /// <i>Voxelforge &gt; Rebuild Mob Models</i>; a state the controller lacks (a wolf has no Paw) falls back to walking.
    /// A mob that hops (a slime) also stretches tall as it flies up and squashes flat as it lands, from how fast it
    /// really rises and falls, since its hops are real jumps on the host, not a clip.
    /// </summary>
    public sealed class MobAnimator : MonoBehaviour
    {
        public const string Locomotion = "Locomotion", Attack = "Attack", Paw = "Paw", Charge = "Charge", Hit = "Hit",
            Death = "Death", Howl = "Howl";

        static readonly int SpeedId = Animator.StringToHash("Speed");

        [SerializeField] Mob mob;

        [SerializeField] Animator animator;

        [Tooltip("Seconds to blend from one clip into the next.")]
        [SerializeField, Range(0f, 0.5f)] float fade = 0.12f;

        [Tooltip("For a mob that hops: what to squash and stretch (the model's parent).")]
        [SerializeField] Transform squash;

        Vector3 _lastPosition;
        float _speed;
        string _playing;
        MobState _lastState;
        Vector3 _restScale = Vector3.one;
        float _stretch = 1f;

        void Awake()
        {
            if (mob == null)
                mob = GetComponentInParent<Mob>();
            if (animator == null)
                animator = GetComponentInChildren<Animator>();
            if (squash != null)
                _restScale = squash.localScale;
        }

        void OnEnable()
        {
            _lastPosition = mob != null ? mob.transform.position : transform.position;
            _playing = null;
        }

        void Update()
        {
            if (mob == null || animator == null || animator.runtimeAnimatorController == null)
                return;
            float dt = Time.deltaTime;
            Vector3 position = mob.transform.position;
            Vector3 moved = position - _lastPosition;
            _lastPosition = position;
            if (squash != null && mob.Definition != null && mob.Definition.Hops)
                Squash(dt > 0f && moved.magnitude < 2f ? moved.y / dt : 0f, dt);
            moved.y = 0f;
            // Ignore a teleport; smooth out the steps between network updates.
            float speed = dt > 0f && moved.magnitude < 2f ? moved.magnitude / dt : 0f;
            _speed = Mathf.Lerp(_speed, speed, 1f - Mathf.Exp(-dt * 10f));
            animator.SetFloat(SpeedId, _speed);

            MobState state = mob.State;
            // A new windup replays its clip, even straight after the last strike.
            bool restart = state == MobState.Windup && _lastState != MobState.Windup;
            _lastState = state;
            string wanted = Wanted();
            if (wanted == _playing && !restart)
                return;
            // Let a strike or a flinch finish before walking on.
            if (!restart && wanted == Locomotion && (_playing == Attack || _playing == Hit))
            {
                AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);
                if (!animator.IsInTransition(0) && current.normalizedTime < 1f)
                    return;
            }
            if (wanted != Locomotion && !animator.HasState(0, Animator.StringToHash(wanted)))
                wanted = Locomotion;
            if (wanted == _playing && !restart)
                return;
            animator.CrossFadeInFixedTime(wanted, wanted == Death ? 0.08f : fade, 0, 0f);
            _playing = wanted;
        }

        /// <summary>Tall and thin going up, short and wide coming down, keeping its volume.</summary>
        void Squash(float rise, float dt)
        {
            float target = mob.State != MobState.Defeated && Mathf.Abs(rise) > 0.2f
                ? Mathf.Clamp(1f + rise * 0.06f, 0.7f, 1.35f)
                : 1f;
            _stretch = Mathf.Lerp(_stretch, target, 1f - Mathf.Exp(-dt * 18f));
            float wide = 1f / Mathf.Sqrt(_stretch);
            squash.localScale = Vector3.Scale(_restScale, new Vector3(wide, _stretch, wide));
        }

        /// <summary>The clip its state calls for.</summary>
        string Wanted()
        {
            switch (mob.State)
            {
                case MobState.Defeated:
                    return Death;
                case MobState.Howl:
                    return Howl;
                case MobState.Windup:
                    return IsCharge(mob.CurrentAttack) ? Paw : Attack;
                case MobState.Charge:
                    return Charge;
                case MobState.Dazed:
                case MobState.Stunned:
                    return Hit;
                case MobState.Recover:
                    // Still finishing the strike it just made.
                    return _playing == Attack ? Attack : Locomotion;
                default:
                    return Locomotion;
            }
        }

        bool IsCharge(int attack)
        {
            MobAttack[] attacks = mob.Definition != null ? mob.Definition.Attacks : null;
            return attacks != null && attack >= 0 && attack < attacks.Length && attacks[attack].Kind == MobAttackKind.Charge;
        }
    }
}
