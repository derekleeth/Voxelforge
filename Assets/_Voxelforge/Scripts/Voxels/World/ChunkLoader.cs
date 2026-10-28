using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// Streams terrain around every registered <see cref="IChunkViewerSource"/> (Roadmap 2.4, D-010): each frame it
    /// asks the sources where they are and hands the list to a <see cref="ChunkStreamer"/>.
    /// <para>
    /// <b>Not a singleton.</b> Whoever spawns a viewer gets a reference to this component and calls
    /// <see cref="Register(IChunkViewerSource)"/> (CLAUDE.md Law #1). On the host, <c>PlayerRegistry</c> registers
    /// every player, local or remote, because the host's physics needs the blocks around all of them (Law #2).
    /// </para>
    /// </summary>
    public sealed class ChunkLoader : MonoBehaviour
    {
        struct Registration
        {
            public IChunkViewerSource Source;
            public int RadiusChunks;
            public bool IsFallback;
        }

        [SerializeField] VoxelWorldManager worldManager;

        [Tooltip("How far terrain loads around each player, in chunks (32 blocks each).")]
        [SerializeField, Range(2, 32)] int viewRadiusChunks = 8;

        [Tooltip("Extra chunks before terrain unloads again, so walking back and forth across a border doesn't reload it.")]
        [SerializeField, Range(1, 8)] int unloadMarginChunks = 2;

        [Tooltip("World height in chunks. Terrain loads in whole columns, from chunk layer 0 up.")]
        [SerializeField, Range(1, 32)] int heightChunks = 8;

        [Tooltip("Columns unloaded per frame at most, so a big jump can't spike one frame.")]
        [SerializeField, Range(1, 64)] int maxColumnUnloadsPerFrame = 4;

        [Tooltip("Log how long the first terrain took to generate.")]
        [SerializeField] bool logFirstLoadTime = true;

        readonly List<Registration> _sources = new List<Registration>();
        readonly List<ChunkViewer> _viewers = new List<ChunkViewer>();
        ChunkStreamer _streamer;
        float _startTime = -1f;

        /// <summary>Null until <c>Start</c>.</summary>
        public ChunkStreamer Streamer => _streamer;

        /// <summary>The viewers terrain was loaded around this frame.</summary>
        public IReadOnlyList<ChunkViewer> Viewers => _viewers;

        public int ViewRadiusChunks => viewRadiusChunks;

        /// <summary>Loads terrain around <paramref name="source"/> at the default player radius.</summary>
        public void Register(IChunkViewerSource source) => Register(source, viewRadiusChunks);

        /// <param name="isFallback">Only used while no other source has a position, e.g. the spawn area before anyone joins.</param>
        public void Register(IChunkViewerSource source, int radiusChunks, bool isFallback = false)
        {
            if (source == null)
                return;
            Unregister(source);
            _sources.Add(new Registration { Source = source, RadiusChunks = math.max(0, radiusChunks), IsFallback = isFallback });
        }

        public void Unregister(IChunkViewerSource source)
        {
            for (int i = _sources.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_sources[i].Source, source))
                    _sources.RemoveAt(i);
            }
        }

        void Start()
        {
            if (worldManager == null || worldManager.World == null)
            {
                Debug.LogError($"{nameof(ChunkLoader)} needs a working {nameof(VoxelWorldManager)}.", this);
                enabled = false;
                return;
            }

            _streamer = new ChunkStreamer(worldManager.World, worldManager.Generator, worldManager.Scheduler,
                heightChunks, unloadMarginChunks, maxColumnUnloadsPerFrame);
            if (logFirstLoadTime)
                _startTime = Time.realtimeSinceStartup;
        }

        void Update()
        {
            CollectViewers();
            _streamer.Update(_viewers);

            if (_startTime >= 0f && _viewers.Count > 0 && worldManager.Generator.IsIdle)
            {
                Debug.Log($"[Voxelforge] Terrain (seed {worldManager.Seed}): {worldManager.World.ChunkCount} chunks in " +
                          $"{_streamer.ColumnCount} columns generated in {(Time.realtimeSinceStartup - _startTime) * 1000f:F0} ms.", this);
                _startTime = -1f;
            }
        }

        void CollectViewers()
        {
            _viewers.Clear();
            for (int pass = 0; pass < 2 && _viewers.Count == 0; pass++)
            {
                bool fallbacks = pass == 1;
                for (int i = _sources.Count - 1; i >= 0; i--)
                {
                    Registration registration = _sources[i];
                    // A destroyed component still compares equal to null through UnityEngine.Object.
                    if (registration.Source is Object unityObject && unityObject == null)
                    {
                        _sources.RemoveAt(i);
                        continue;
                    }
                    if (registration.IsFallback == fallbacks && registration.Source.TryGetViewerPosition(out float3 position))
                        _viewers.Add(new ChunkViewer(position, registration.RadiusChunks));
                }
            }
        }
    }
}
