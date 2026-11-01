using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// A melee weapon swing on the hero's body (Roadmap 5.5 step G), on every PC: the weapon arm winds up over the
    /// shoulder, smashes down in front and comes back, with the body leaning into the blow. The hero has no attack clip, so
    /// it's drawn by turning the arm and spine bones after the Animator each frame; the held weapon
    /// (<see cref="HeldItemView"/>) rides on the hand. Works on every race's body (any humanoid skeleton). Presentation
    /// only: <see cref="PlayerWeapon.Swung"/> says when; the blow itself is the host's.
    /// </summary>
    [RequireComponent(typeof(PlayerWeapon))]
    public sealed class HeroSwingView : MonoBehaviour
    {
        [Tooltip("The hero's humanoid Animator (Visual/Hero).")]
        [SerializeField] Animator animator;

        [Tooltip("How far up and back the arm winds up, in degrees from hanging down.")]
        [SerializeField, Range(60f, 200f)] float windupAngle = 160f;

        [Tooltip("Where the blow ends, in degrees up from hanging down (in front of the hero).")]
        [SerializeField, Range(0f, 120f)] float strikeAngle = 45f;

        [Tooltip("How much the elbow bends during the wind-up, in degrees.")]
        [SerializeField, Range(0f, 120f)] float elbowBend = 70f;

        [Tooltip("How far the body leans back for the wind-up and forward into the blow, in degrees.")]
        [SerializeField, Range(0f, 40f)] float lean = 14f;

        // Share of the swing spent winding up, then striking; the rest recovers.
        const float WindupEnd = 0.38f;
        const float StrikeEnd = 0.58f;

        PlayerWeapon _weapon;
        float _start = float.NegativeInfinity;
        float _seconds = 0.5f;

        void Awake()
        {
            _weapon = GetComponent<PlayerWeapon>();
            if (animator == null && TryGetComponent(out HeroBody body))
                animator = body.Animator;
        }

        void OnEnable() => _weapon.Swung += OnSwung;

        void OnDisable() => _weapon.Swung -= OnSwung;

        void OnSwung(float seconds)
        {
            _start = Time.time;
            _seconds = Mathf.Max(0.2f, seconds);
        }

        /// <summary>
        /// The arm's lift (degrees up from hanging), the elbow's bend and the body's lean (+ forward) at
        /// <paramref name="t"/>, 0-1 through a swing. Plain maths so tests can check its shape.
        /// </summary>
        public static (float arm, float elbow, float bodyLean) Pose(float t, float windup, float strike, float bend, float leanAngle)
        {
            if (t <= 0f || t >= 1f)
                return (0f, 0f, 0f);
            if (t < WindupEnd)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / WindupEnd);
                return (windup * k, bend * k, -leanAngle * k);
            }
            if (t < StrikeEnd)
            {
                // Fast and hard: ease in, so it whips down at the end.
                float k = (t - WindupEnd) / (StrikeEnd - WindupEnd);
                k *= k;
                return (Mathf.Lerp(windup, strike, k), Mathf.Lerp(bend, 0f, k), Mathf.Lerp(-leanAngle, leanAngle, k));
            }
            float r = Mathf.SmoothStep(0f, 1f, (t - StrikeEnd) / (1f - StrikeEnd));
            return (Mathf.Lerp(strike, 0f, r), 0f, Mathf.Lerp(leanAngle, 0f, r));
        }

        // After the Animator has posed the bones this frame, so the swing adds to whatever the legs are doing.
        void LateUpdate()
        {
            float t = (Time.time - _start) / _seconds;
            if (t <= 0f || t >= 1f || animator == null || !animator.isHuman)
                return;
            (float arm, float elbow, float bodyLean) = Pose(t, windupAngle, strikeAngle, elbowBend, lean);
            Vector3 right = transform.right;
            // Turning about the hero's right: a negative angle swings a hanging arm forward and up (left-handed axes).
            Transform spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            if (spine != null)
                spine.rotation = Quaternion.AngleAxis(bodyLean, right) * spine.rotation;
            Transform upper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            if (upper != null)
                upper.rotation = Quaternion.AngleAxis(-arm, right) * upper.rotation;
            Transform lower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            if (lower != null)
                lower.rotation = Quaternion.AngleAxis(-elbow, right) * lower.rotation;
        }
    }
}
