using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Makes a stand-in blocky mob (Roadmap 5.2 step B) look alive until the real models and clips arrive in steps E and
    /// F. It only <i>reads</i> what every PC already has, the synced transform and <see cref="Mob.State"/>, and moves its
    /// <see cref="body"/>: a trot bob while it moves, a shake for the telegraph, a lean for a charge, a wobble when dazed
    /// or stunned, and toppling over when defeated. Nothing about it is sent over the network.
    /// </summary>
    public sealed class MobStandInView : MonoBehaviour
    {
        [SerializeField] Mob mob;

        [Tooltip("The model to move: a child of the mob, with its pivot at the feet.")]
        [SerializeField] Transform body;

        [Tooltip("How high it bobs while moving, in metres.")]
        [SerializeField, Range(0f, 0.3f)] float bobHeight = 0.08f;

        [Tooltip("Bobs per metre moved.")]
        [SerializeField, Range(0.2f, 4f)] float bobsPerMetre = 1.2f;

        Vector3 _restPosition;
        Quaternion _restRotation;
        Vector3 _lastPosition;
        float _travelled;
        float _fallen;
        float _lean;

        void Awake()
        {
            if (mob == null)
                mob = GetComponentInParent<Mob>();
            if (body == null)
                body = transform;
            _restPosition = body.localPosition;
            _restRotation = body.localRotation;
        }

        void OnEnable() => _lastPosition = mob != null ? mob.transform.position : transform.position;

        void LateUpdate()
        {
            if (mob == null)
                return;
            float dt = Time.deltaTime;
            float time = Time.time;
            Vector3 position = mob.transform.position;
            Vector3 moved = position - _lastPosition;
            _lastPosition = position;
            moved.y = 0f;
            float distance = moved.magnitude;
            if (distance < 2f) // Ignore a teleport.
                _travelled += distance;

            MobState state = mob.State;
            float bob = Mathf.Abs(Mathf.Sin(_travelled * bobsPerMetre * Mathf.PI)) * bobHeight *
                        Mathf.Clamp01(distance / Mathf.Max(dt, 1e-4f) / 2f);
            float pitch = 0f, roll = 0f, yaw = 0f;
            Vector3 shake = Vector3.zero;
            switch (state)
            {
                case MobState.Windup:
                    // Pawing the ground: rears back a little and shudders, the "here it comes" warning.
                    pitch = -8f;
                    shake = new Vector3(Mathf.Sin(time * 70f), 0f, Mathf.Sin(time * 53f)) * 0.04f;
                    break;
                case MobState.Dazed:
                case MobState.Stunned:
                    yaw = Mathf.Sin(time * 9f) * 18f;
                    roll = Mathf.Sin(time * 6f) * 8f;
                    break;
            }
            _lean = Mathf.MoveTowards(_lean, state == MobState.Charge ? 14f : 0f, dt * 90f);
            _fallen = Mathf.MoveTowards(_fallen, state == MobState.Defeated ? 1f : 0f, dt * 2.5f);

            body.localPosition = _restPosition + new Vector3(0f, bob, 0f) + shake;
            body.localRotation = _restRotation * Quaternion.Euler(pitch + _lean, yaw, roll + 90f * _fallen * _fallen);
        }
    }
}
