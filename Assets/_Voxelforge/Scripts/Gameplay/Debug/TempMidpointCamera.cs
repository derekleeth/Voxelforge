using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// <b>Temporary (Roadmap 2.1):</b> one camera that watches the midpoint of all players and pulls back as they
    /// spread out. Roadmap 2.3 replaces it with a camera per player and Voronoi split-screen.
    /// </summary>
    public sealed class TempMidpointCamera : MonoBehaviour
    {
        [SerializeField] Camera targetCamera;

        [SerializeField] PlayerRegistry players;

        [Tooltip("Where the camera sits relative to the midpoint, when the players are close together.")]
        [SerializeField] Vector3 offset = new Vector3(0f, 10f, -12f);

        [Tooltip("How quickly the camera catches up. Higher = snappier.")]
        [SerializeField, Min(0.1f)] float followSharpness = 6f;

        void LateUpdate()
        {
            if (targetCamera == null || players == null || players.Players.Count == 0)
                return;

            Bounds bounds = default;
            bool any = false;
            foreach (PlayerController player in players.Players)
            {
                if (player == null || player.IsWaitingForGround)
                    continue;
                if (!any)
                    bounds = new Bounds(player.transform.position, Vector3.zero);
                else
                    bounds.Encapsulate(player.transform.position);
                any = true;
            }
            if (!any)
                return;

            float pullBack = Mathf.Max(1f, bounds.size.magnitude / 8f);
            Vector3 target = bounds.center + offset * pullBack;
            float t = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
            Transform cameraTransform = targetCamera.transform;
            cameraTransform.position = Vector3.Lerp(cameraTransform.position, target, t);
            cameraTransform.rotation = Quaternion.LookRotation(bounds.center + Vector3.up - cameraTransform.position);
        }
    }
}
