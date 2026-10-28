using System.Collections.Generic;
using UnityEngine;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// The scene's entry point to the voxel world. It owns the <see cref="VoxelWorld"/> data, the
    /// <see cref="WorldSeed"/>, the <see cref="ChunkGenerationScheduler"/> that builds terrain and the
    /// <see cref="ChunkMeshScheduler"/> that draws it, and it drives both every frame.
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

        [SerializeField] TerrainSettings terrainSettings;

        [Tooltip("The number the whole world grows from. Same seed = same world.")]
        [SerializeField] int worldSeed = 20261027;

        [Tooltip("Pick a new random seed every time Play starts. The seed used is written back above, so you can keep a world you like.")]
        [SerializeField] bool randomizeSeedOnPlay;

        [Tooltip("Material for every chunk mesh. Placeholder vertex-color shader until Roadmap 1.5.")]
        [SerializeField] Material chunkMaterial;

        [Tooltip("How many chunks may be generating on worker threads at once.")]
        [SerializeField, Range(1, 256)] int maxGenerationJobsInFlight = 32;

        [Tooltip("How many chunks may be meshing on worker threads at once.")]
        [SerializeField, Range(1, 256)] int maxJobsInFlight = 32;

        [Tooltip("Log how long the first full meshing pass took.")]
        [SerializeField] bool logMeshingTime = true;

        VoxelWorld _world;
        ChunkGenerationScheduler _generator;
        ChunkMeshScheduler _scheduler;
        float _meshingStartTime = -1f;

        /// <summary>The block data. Only this assembly can edit it.</summary>
        public VoxelWorld World => _world;

        /// <summary>The seed this world was built from. Saves (3.4) and joining players (6.2) need it.</summary>
        public WorldSeed Seed => _generator?.Seed ?? new WorldSeed(worldSeed);

        public ChunkGenerationScheduler Generator => _generator;

        public ChunkMeshScheduler Scheduler => _scheduler;

        void Awake()
        {
            if (blockRegistry == null || terrainSettings == null)
            {
                Debug.LogError($"{nameof(VoxelWorldManager)} needs a Block Registry and Terrain Settings.", this);
                enabled = false;
                return;
            }

            List<string> errors = terrainSettings.Validate(blockRegistry);
            if (errors.Count > 0)
            {
                Debug.LogError($"Terrain settings '{terrainSettings.name}' are invalid:\n{string.Join("\n", errors)}", terrainSettings);
                enabled = false;
                return;
            }

            if (randomizeSeedOnPlay)
                worldSeed = WorldSeed.CreateRandom().Value;

            _world = new VoxelWorld(blockRegistry.CreateLookup(Unity.Collections.Allocator.Persistent));
            _generator = new ChunkGenerationScheduler(_world, new WorldSeed(worldSeed), terrainSettings.ToTerrainParams(),
                terrainSettings.CreateBiomeLookup(Unity.Collections.Allocator.Persistent), maxGenerationJobsInFlight);
            _scheduler = new ChunkMeshScheduler(_world, transform, chunkMaterial, maxJobsInFlight)
            {
                IsChunkIncoming = _generator.IsRequested,
            };
        }

        void Update()
        {
            _generator.Update();

            if (logMeshingTime && _meshingStartTime < 0f && _world.DirtyCount > 0)
                _meshingStartTime = Time.realtimeSinceStartup;

            _scheduler.Update();

            if (_meshingStartTime >= 0f && _generator.IsIdle && _world.DirtyCount == 0 && _scheduler.PendingCount == 0)
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
            _generator?.Dispose();
            _world?.Dispose();
        }
    }
}
