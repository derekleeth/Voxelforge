using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using Voxelforge.Core;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// The scene's entry point to the voxel world. It owns the <see cref="VoxelWorld"/> data, the
    /// <see cref="WorldSeed"/>, the <see cref="ChunkGenerationScheduler"/> that builds terrain and the
    /// <see cref="ChunkMeshScheduler"/> that draws it, and it drives both every frame.
    /// <para>
    /// <b>Not a singleton.</b> Systems that need the world get a reference to this component
    /// (CLAUDE.md Law #1). <see cref="RequestModifyVoxel"/> is the only way gameplay code may change blocks (Law #3).
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

        [Header("Decorations (Roadmap 2.6)")]
        [Tooltip("Grass, flowers, bushes and trees. Empty = a bare world.")]
        [SerializeField] DecorationSet decorationSet;

        [Tooltip("Shaders/DecorationCull.compute: culls decorations for each camera on the GPU.")]
        [SerializeField] ComputeShader decorationCullShader;

        [Tooltip("Which way the wind blows, as a compass angle (0 = north/+Z, 90 = east/+X).")]
        [SerializeField, Range(0f, 360f)] float windAngle = 60f;

        [Tooltip("How far plant tips bend in the wind, in metres.")]
        [SerializeField, Range(0f, 1f)] float windStrength = 0.12f;

        [Tooltip("How quickly plants sway.")]
        [SerializeField, Range(0f, 5f)] float windSpeed = 1.6f;

        VoxelWorld _world;
        ChunkGenerationScheduler _generator;
        ChunkMeshScheduler _scheduler;
        DecorationLayer _decorations;
        DecorationRenderer _decorationRenderer;
        VoxelWorldEditor _editor;
        float _meshingStartTime = -1f;

        /// <summary>The block data. Only this assembly can edit it.</summary>
        public VoxelWorld World => _world;

        /// <summary>The seed this world was built from. Saves (3.4) and joining players (6.2) need it.</summary>
        public WorldSeed Seed => _generator?.Seed ?? new WorldSeed(worldSeed);

        public ChunkGenerationScheduler Generator => _generator;

        public ChunkMeshScheduler Scheduler => _scheduler;

        /// <summary>Every block players have changed. Saves (3.4) and joining players (6.2) need it.</summary>
        public VoxelDeltaLog DeltaLog => _editor?.DeltaLog;

        /// <summary>Host only: raised for every block that changes, e.g. for drops (3.2) or sending edits to clients (6.3).</summary>
        public event System.Action<VoxelEdit> EditApplied;

        /// <summary>Every loaded chunk's decorations, or null when there is no decoration set.</summary>
        public DecorationLayer Decorations => _decorations;

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
            _scheduler.SetBiomeTint(_generator.Seed, _generator.Terrain, _generator.Biomes);
            _editor = new VoxelWorldEditor(_world, new VoxelDeltaLog(), _scheduler);
            _editor.EditApplied += edit => EditApplied?.Invoke(edit);
            _generator.DeltaLog = _editor.DeltaLog;
            CreateDecorations();
        }

        /// <summary>
        /// <b>Host only.</b> Sets the block at <paramref name="coord"/> to <paramref name="blockId"/> on behalf of
        /// <paramref name="instigator"/>, logs it in the <see cref="DeltaLog"/> and remeshes. The only way gameplay code
        /// may change blocks (CLAUDE.md Law #3). Clients ask the host with a server RPC (<c>PlayerVoxelEditor</c>).
        /// </summary>
        public VoxelEditResult RequestModifyVoxel(int3 coord, ushort blockId, PlayerId instigator) =>
            _editor?.RequestModifyVoxel(coord, blockId, instigator) ?? VoxelEditResult.ChunkNotLoaded;

        void CreateDecorations()
        {
            if (decorationSet == null)
                return;

            List<string> errors = decorationSet.Validate(terrainSettings, blockRegistry);
            if (errors.Count > 0)
            {
                Debug.LogError($"Decoration set '{decorationSet.name}' is invalid, so the world has no decorations:\n" +
                               string.Join("\n", errors), decorationSet);
                return;
            }

            _decorations = new DecorationLayer(decorationSet, terrainSettings, blockRegistry, _generator.Seed,
                _generator.Terrain, _generator.Biomes);
            _scheduler.Decorations = _decorations;
            if (decorationCullShader != null && SystemInfo.supportsComputeShaders)
                _decorationRenderer = new DecorationRenderer(_decorations, decorationCullShader);
            else
                Debug.LogWarning("Decorations are placed but not drawn: no cull compute shader, or no compute support.", this);
        }

        void Update()
        {
            _generator.Update();

            if (logMeshingTime && _meshingStartTime < 0f && _world.DirtyCount > 0)
                _meshingStartTime = Time.realtimeSinceStartup;

            _scheduler.Update();
            if (_decorationRenderer != null)
            {
                float radians = windAngle * Mathf.Deg2Rad;
                _decorationRenderer.SetWind(new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)), windStrength, windSpeed);
                _decorationRenderer.Update();
            }

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
            _decorationRenderer?.Dispose();
            _decorations?.Dispose(); // Borrows the generator's biome list, so it goes first.
            _generator?.Dispose();
            _world?.Dispose();
        }
    }
}
