using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// Loads whole columns of chunks around a list of <see cref="ChunkViewer"/>s and unloads the ones nobody is
    /// near any more (Roadmap 2.4, D-010). Plain C# so tests can drive it without a scene.
    /// <para>
    /// <b>Load:</b> a column is wanted within a viewer's radius. Every layer from 0 to the world height is requested,
    /// nearest column first and top layer down, so the ground under the players appears first.
    /// <b>Unload:</b> a column goes once it's more than radius + <c>unloadMargin</c> from every viewer. The margin
    /// stops terrain flickering in and out when a player walks back and forth across a chunk border.
    /// </para>
    /// <para>
    /// Plans only change when a viewer crosses into another column or the viewers change, so standing still is free.
    /// </para>
    /// </summary>
    public sealed class ChunkStreamer
    {
        readonly VoxelWorld _world;
        readonly ChunkGenerationScheduler _generator;
        readonly ChunkMeshScheduler _meshes;
        readonly int _heightChunks;
        readonly int _unloadMargin;
        readonly int _maxUnloadsPerFrame;

        readonly HashSet<int2> _columns = new HashSet<int2>(); // Columns that have, or will soon have, chunks.
        readonly List<int2> _unloadQueue = new List<int2>();
        readonly List<int3> _plannedFor = new List<int3>(); // (column x, column z, radius) of each viewer at the last plan.

        /// <param name="meshes">Optional: releases the renderers of unloaded chunks.</param>
        /// <param name="heightChunks">Layers per column, starting at chunk layer 0.</param>
        /// <param name="unloadMargin">Extra chunks beyond a viewer's radius before a column unloads.</param>
        /// <param name="maxUnloadsPerFrame">Columns unloaded per <see cref="Update"/>, so a teleport can't spike one frame.</param>
        public ChunkStreamer(VoxelWorld world, ChunkGenerationScheduler generator, ChunkMeshScheduler meshes,
            int heightChunks, int unloadMargin = 2, int maxUnloadsPerFrame = 4)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _generator = generator ?? throw new ArgumentNullException(nameof(generator));
            _meshes = meshes;
            _heightChunks = math.max(1, heightChunks);
            _unloadMargin = math.max(0, unloadMargin);
            _maxUnloadsPerFrame = math.max(1, maxUnloadsPerFrame);
        }

        /// <summary>Columns loaded or loading, including ones waiting to unload.</summary>
        public int ColumnCount => _columns.Count;

        /// <summary>Columns out of range, waiting their turn to unload.</summary>
        public int UnloadingCount => _unloadQueue.Count;

        public bool HasColumn(int2 column) => _columns.Contains(column);

        /// <summary>Call once per frame with every current viewer.</summary>
        public void Update(IReadOnlyList<ChunkViewer> viewers)
        {
            if (ViewersMoved(viewers))
                Replan(viewers);
            UnloadSome();
        }

        /// <summary>
        /// Every column within <c>RadiusChunks + extraRadius</c> of any viewer, nearest to its closest viewer first
        /// (ties broken by x, then z, so the order is always the same).
        /// </summary>
        public static List<int2> ComputeWantedColumns(IReadOnlyList<ChunkViewer> viewers, int extraRadius = 0)
        {
            var nearest = new Dictionary<int2, int>();
            foreach (ChunkViewer viewer in viewers)
            {
                int radius = math.max(0, viewer.RadiusChunks + extraRadius);
                int2 center = viewer.Column;
                for (int dz = -radius; dz <= radius; dz++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int distanceSq = dx * dx + dz * dz;
                    if (distanceSq > radius * radius)
                        continue;
                    int2 column = center + new int2(dx, dz);
                    if (!nearest.TryGetValue(column, out int best) || distanceSq < best)
                        nearest[column] = distanceSq;
                }
            }

            var columns = new List<int2>(nearest.Keys);
            columns.Sort((a, b) =>
            {
                int byDistance = nearest[a].CompareTo(nearest[b]);
                if (byDistance != 0)
                    return byDistance;
                return a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y);
            });
            return columns;
        }

        /// <summary>True if <paramref name="column"/> is within <c>RadiusChunks + extraRadius</c> of any viewer.</summary>
        public static bool IsInRange(int2 column, IReadOnlyList<ChunkViewer> viewers, int extraRadius = 0)
        {
            foreach (ChunkViewer viewer in viewers)
            {
                int radius = math.max(0, viewer.RadiusChunks + extraRadius);
                int2 d = column - viewer.Column;
                if (d.x * d.x + d.y * d.y <= radius * radius)
                    return true;
            }
            return false;
        }

        bool ViewersMoved(IReadOnlyList<ChunkViewer> viewers)
        {
            bool moved = viewers.Count != _plannedFor.Count;
            for (int i = 0; !moved && i < viewers.Count; i++)
                moved = !_plannedFor[i].Equals(new int3(viewers[i].Column, viewers[i].RadiusChunks));
            if (!moved)
                return false;

            _plannedFor.Clear();
            foreach (ChunkViewer viewer in viewers)
                _plannedFor.Add(new int3(viewer.Column, viewer.RadiusChunks));
            return true;
        }

        void Replan(IReadOnlyList<ChunkViewer> viewers)
        {
            // Start the queue over, so the closest columns to where the viewers are *now* go first.
            _generator.CancelQueued();

            List<int2> wanted = ComputeWantedColumns(viewers);
            var wantedSet = new HashSet<int2>(wanted);
            var kept = new List<int2>();
            _unloadQueue.Clear();
            foreach (int2 column in _columns)
            {
                if (wantedSet.Contains(column))
                    continue;
                if (IsInRange(column, viewers, _unloadMargin))
                    kept.Add(column); // In the margin: stays, and finishes loading if it was cut short.
                else
                    _unloadQueue.Add(column);
            }

            foreach (int2 column in wanted)
            {
                _columns.Add(column);
                RequestColumn(column);
            }
            foreach (int2 column in kept)
                RequestColumn(column);
        }

        void RequestColumn(int2 column)
        {
            // Chunks that generate again get their edits back from the delta log (ChunkGenerationScheduler.DeltaLog).
            for (int y = _heightChunks - 1; y >= 0; y--)
                _generator.Request(new ChunkCoord(column.x, y, column.y)); // Does nothing for loaded chunks.
        }

        void UnloadSome()
        {
            int budget = _maxUnloadsPerFrame;
            bool gathersDone = false;
            for (int i = _unloadQueue.Count - 1; i >= 0 && budget > 0; i--)
            {
                int2 column = _unloadQueue[i];
                if (IsGenerating(column))
                    continue; // A job is still building one of its chunks. It arrives soon; unload it then.

                if (!gathersDone)
                {
                    _meshes?.CompleteGatherJobs(); // A gather job may be reading a chunk we're about to free.
                    gathersDone = true;
                }

                for (int y = 0; y < _heightChunks; y++)
                {
                    var coord = new ChunkCoord(column.x, y, column.y);
                    _world.RemoveChunk(coord);
                    _meshes?.ReleaseRenderer(coord);
                }
                _columns.Remove(column);
                _unloadQueue.RemoveAt(i);
                budget--;
            }
        }

        bool IsGenerating(int2 column)
        {
            for (int y = 0; y < _heightChunks; y++)
            {
                if (_generator.IsRequested(new ChunkCoord(column.x, y, column.y)))
                    return true;
            }
            return false;
        }
    }
}
