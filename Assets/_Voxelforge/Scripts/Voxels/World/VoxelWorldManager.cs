using UnityEngine;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// The scene's entry point to the voxel world. It owns the <see cref="VoxelWorld"/> data and the
    /// <see cref="ChunkMeshScheduler"/> that draws it, and it drives meshing every frame.
    /// <para>
    /// <b>Not a singleton.</b> Systems that need the world get a reference to this component
    /// (CLAUDE.md Law #1). Roadmap 3.1 adds <c>RequestModifyVoxel(coord, blockId, instigatorId)</c> here, the only
    /// way gameplay code may change blocks.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(-100)] // Create the world before other scripts' Awake/Start use it.
    public sealed class VoxelWorldManager : MonoBehaviour
    {
        [SerializeField] BlockRegistry blockRegistry;

        [Tooltip("Material for every chunk mesh. Placeholder vertex-color shader until Roadmap 1.5.")]
        [SerializeField] Material chunkMaterial;

        [Tooltip("How many chunks may be meshing on worker threads at once.")]
        [SerializeField, Range(1, 256)] int maxJobsInFlight = 32;

        [Tooltip("Log how long the first full meshing pass took.")]
        [SerializeField] bool logMeshingTime = true;

        VoxelWorld _world;
        ChunkMeshScheduler _scheduler;
        float _meshingStartTime = -1f;

        /// <summary>The block data. Only this assembly can edit it.</summary>
        public VoxelWorld World => _world;

        public ChunkMeshScheduler Scheduler => _scheduler;

        void Awake()
        {
            if (blockRegistry == null)
            {
                Debug.LogError($"{nameof(VoxelWorldManager)} needs a Block Registry.", this);
                enabled = false;
                return;
            }

            _world = new VoxelWorld(blockRegistry.CreateLookup(Unity.Collections.Allocator.Persistent));
            _scheduler = new ChunkMeshScheduler(_world, transform, chunkMaterial, maxJobsInFlight);
        }

        void Update()
        {
            if (logMeshingTime && _meshingStartTime < 0f && _world.DirtyCount > 0)
                _meshingStartTime = Time.realtimeSinceStartup;

            _scheduler.Update();

            if (_meshingStartTime >= 0f && _world.DirtyCount == 0 && _scheduler.PendingCount == 0)
            {
                float ms = (Time.realtimeSinceStartup - _meshingStartTime) * 1000f;
                Debug.Log($"[Voxelforge] Meshed {_scheduler.MeshedCount} of {_world.ChunkCount} chunks in {ms:F0} ms " +
                          $"({_scheduler.DiscardedCount} stale results discarded).", this);
                _meshingStartTime = -1f;
                logMeshingTime = false; // Only report the first build; later edits remesh a few chunks at a time.
            }
        }

        void OnDestroy()
        {
            // Jobs read world memory, so stop them before freeing it.
            _scheduler?.Dispose();
            _world?.Dispose();
        }
    }
}
