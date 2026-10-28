using Unity.Mathematics;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Plants a humanoid hero's feet on the blocks under them. Sits next to the hero's <see cref="Animator"/>, whose
    /// base layer needs <b>IK Pass</b> on. Presentation only: it reads the blocks with <see cref="VoxelQueries"/>
    /// and never changes where the host says the player is.
    /// <para>
    /// Block ground comes in whole-meter levels, so a foot is either on the level the body stands on, on a level
    /// less than <see cref="maxReach"/> away (while gliding over a step), or over a drop it can't reach. In that
    /// last case the foot slides back toward the body until it is on the edge of the block, instead of
    /// standing on air. The pelvis drops when a foot has to reach down.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public sealed class HeroFootIK : MonoBehaviour
    {
        [SerializeField] PlayerController player;

        [Tooltip("Height of the ankle above the sole, in meters.")]
        [SerializeField, Min(0f)] float footHeight = 0.2f;

        [Tooltip("Furthest a foot reaches up or down to find ground, in meters.")]
        [SerializeField, Min(0f)] float maxReach = 0.6f;

        [Tooltip("A foot lifted this high by the animation (mid-stride) isn't planted at all.")]
        [SerializeField, Min(0.01f)] float swingHeight = 0.25f;

        [Tooltip("Ground speed (m/s) above which planting fades out, so running looks like the animation.")]
        [SerializeField, Min(0f)] float fadeStartSpeed = 1f;

        [Tooltip("How quickly IK weight and pelvis height follow their targets (per second).")]
        [SerializeField, Min(0f)] float smoothing = 15f;

        Animator _animator;
        HeroAnimator _heroAnimator;
        float _weight;
        float _pelvisOffset;

        void Awake()
        {
            _animator = GetComponent<Animator>();
            if (player == null)
                player = GetComponentInParent<PlayerController>();
            _heroAnimator = GetComponentInParent<HeroAnimator>();
        }

        void OnAnimatorIK(int layerIndex)
        {
            VoxelWorld world = player != null ? player.World : null;
            float speed = _heroAnimator != null ? _heroAnimator.Speed : 0f;
            float targetWeight = world != null && player.Grounded ? 1f - math.saturate((speed - fadeStartSpeed) / 4f) : 0f;
            float t = 1f - math.exp(-smoothing * Time.deltaTime);
            _weight = math.lerp(_weight, targetWeight, t);

            float3 root = transform.position;
            float3 left = _animator.GetIKPosition(AvatarIKGoal.LeftFoot);
            float3 right = _animator.GetIKPosition(AvatarIKGoal.RightFoot);
            float leftWeight = 0f, rightWeight = 0f;
            float lowest = 0f;
            if (_weight > 0.001f)
            {
                leftWeight = PlaceFoot(world, root, ref left, ref lowest);
                rightWeight = PlaceFoot(world, root, ref right, ref lowest);
            }

            // Drop the pelvis so the lower foot can reach; never raise it, or the legs would over-stretch.
            _pelvisOffset = math.lerp(_pelvisOffset, lowest * _weight, t);
            _animator.bodyPosition += new Vector3(0f, _pelvisOffset, 0f);

            SetGoal(AvatarIKGoal.LeftFoot, left, leftWeight * _weight);
            SetGoal(AvatarIKGoal.RightFoot, right, rightWeight * _weight);
        }

        /// <summary>Moves one foot onto the ground and returns how firmly to plant it (0-1).</summary>
        float PlaceFoot(VoxelWorld world, float3 root, ref float3 foot, ref float lowest)
        {
            float lift = math.max(0f, foot.y - (root.y + footHeight));
            float3 probe = new float3(foot.x, root.y, foot.z);
            if (!VoxelQueries.TryGetGroundHeight(world, probe, maxReach, maxReach, out float ground))
            {
                // Over a drop: slide the foot back toward the body until it's on the block.
                bool found = false;
                for (float s = 0.25f; s <= 1f && !found; s += 0.25f)
                {
                    float3 p = math.lerp(probe, new float3(root.x, root.y, root.z), s);
                    if (VoxelQueries.TryGetGroundHeight(world, p, maxReach, maxReach, out ground))
                    {
                        foot.x = p.x;
                        foot.z = p.z;
                        found = true;
                    }
                }
                if (!found)
                    return 0f;
            }

            foot.y = ground + footHeight + lift;
            lowest = math.min(lowest, foot.y - lift - (root.y + footHeight));
            return 1f - math.saturate(lift / swingHeight);
        }

        void SetGoal(AvatarIKGoal goal, float3 position, float weight)
        {
            _animator.SetIKPositionWeight(goal, weight);
            _animator.SetIKPosition(goal, position);
        }
    }
}
