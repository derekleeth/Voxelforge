using Unity.Mathematics;
using UnityEngine;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// Keeps terrain loaded around this object's position, e.g. the spawn area so the overview camera shows a world
    /// before anyone joins.
    /// </summary>
    public sealed class ChunkViewerAnchor : MonoBehaviour, IChunkViewerSource
    {
        [SerializeField] ChunkLoader chunkLoader;

        [Tooltip("How far terrain loads around this point, in chunks.")]
        [SerializeField, Range(0, 32)] int radiusChunks = 6;

        [Tooltip("Only load here while nobody else needs terrain (before players join). Off = always.")]
        [SerializeField] bool onlyWhileNoPlayers = true;

        public bool TryGetViewerPosition(out float3 position)
        {
            position = transform.position;
            return true;
        }

        void OnEnable()
        {
            if (chunkLoader != null)
                chunkLoader.Register(this, radiusChunks, onlyWhileNoPlayers);
        }

        void OnDisable()
        {
            if (chunkLoader != null)
                chunkLoader.Unregister(this);
        }
    }
}
