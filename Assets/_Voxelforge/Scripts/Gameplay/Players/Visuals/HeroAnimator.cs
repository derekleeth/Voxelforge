using Unity.Mathematics;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Makes a hero look alive on every PC. Sits on the hero model (the player's <see cref="PlayerController.VisualRoot"/>)
    /// and only <i>reads</i> what the host decided: the synced transform and <see cref="PlayerController.Grounded"/>.
    /// Nothing about animation is sent over the network.
    /// <list type="bullet">
    /// <item>Feeds the Animator <c>Speed</c> (ground speed, m/s), <c>VerticalSpeed</c> (m/s) and <c>Grounded</c>.</item>
    /// <item>Glides over auto-steps: the motor lifts the body a whole block in one tick, so while grounded this
    /// model soaks up any height change and eases it back out over about a tenth of a second. Cameras follow this
    /// model, so they glide too.</item>
    /// </list>
    /// </summary>
    public sealed class HeroAnimator : MonoBehaviour
    {
        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int VerticalSpeedId = Animator.StringToHash("VerticalSpeed");
        static readonly int GroundedId = Animator.StringToHash("Grounded");

        [SerializeField] PlayerController player;

        [Tooltip("The hero's Animator. Optional: without one, only the step glide runs.")]
        [SerializeField] Animator animator;

        [Tooltip("How fast the model catches up after a step (per second). 25 settles in about 0.12 s.")]
        [SerializeField, Min(1f)] float stepCatchUp = 25f;

        [Tooltip("Seconds of smoothing on the Speed parameter, so walk/run blends don't twitch.")]
        [SerializeField, Min(0f)] float speedDamping = 0.1f;

        float3 _lastRootPosition;
        bool _wasGrounded;
        float _stepOffset;
        bool _hasLastPosition;

        /// <summary>Ground speed in m/s, measured from how the player actually moved.</summary>
        public float Speed { get; private set; }

        public float VerticalSpeed { get; private set; }

        void Awake()
        {
            if (player == null)
                player = GetComponentInParent<PlayerController>();
            if (animator != null)
                animator.applyRootMotion = false;
        }

        void OnEnable() => _hasLastPosition = false;

        void LateUpdate()
        {
            float3 root = player.transform.position;
            float dt = Time.deltaTime;
            bool grounded = player.Grounded;
            if (!_hasLastPosition || dt <= 0f)
            {
                _lastRootPosition = root;
                _wasGrounded = grounded;
                _hasLastPosition = true;
                return;
            }

            float3 moved = root - _lastRootPosition;
            _lastRootPosition = root;

            // Grounded bodies only change height by auto-stepping; soak that up and ease it out.
            if (grounded && _wasGrounded)
                _stepOffset -= moved.y;
            _wasGrounded = grounded;
            _stepOffset = math.clamp(_stepOffset * math.exp(-stepCatchUp * dt), -1.5f, 1.5f);
            transform.localPosition = new Vector3(0f, _stepOffset, 0f);

            Speed = math.length(moved.xz) / dt;
            VerticalSpeed = grounded ? 0f : moved.y / dt;
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                animator.SetFloat(SpeedId, Speed, speedDamping, dt);
                animator.SetFloat(VerticalSpeedId, VerticalSpeed);
                animator.SetBool(GroundedId, grounded);
            }
        }
    }
}
