using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// One path asked of the <see cref="NavPathService"/>. Check <see cref="IsDone"/> on later frames; the search runs on
    /// a worker thread in between.
    /// </summary>
    public sealed class NavPathQuery
    {
        readonly List<int3> _waypoints = new List<int3>();

        internal NavPathQuery(int3 start, int3 goal, in NavAgentProfile profile)
        {
            Start = start;
            Goal = goal;
            Profile = profile;
        }

        /// <summary>The feet block the search started from.</summary>
        public int3 Start { get; }

        /// <summary>The feet block it was asked to reach.</summary>
        public int3 Goal { get; }

        public NavAgentProfile Profile { get; }

        public NavPathStatus Status { get; internal set; } = NavPathStatus.Pending;

        public bool IsDone => Status != NavPathStatus.Pending;

        /// <summary>Nobody wants it any more. A queued search is skipped; a running one finishes and is thrown away.</summary>
        public bool IsCancelled { get; private set; }

        /// <summary>The feet blocks to walk to in turn, not including the start. Empty until done.</summary>
        public IReadOnlyList<int3> Waypoints => _waypoints;

        /// <summary>How many cells the search looked at.</summary>
        public int NodesExpanded { get; internal set; }

        /// <summary>
        /// <see cref="VoxelNavGrid.Stamp"/> when the search started. A chunk stamped later changed after the search read it,
        /// so a path across it may be out of date.
        /// </summary>
        public long GridStamp { get; internal set; }

        public void Cancel() => IsCancelled = true;

        internal List<int3> WaypointList => _waypoints;
    }

    /// <summary>
    /// Runs path searches (Roadmap 5.1) as Burst jobs (<see cref="FindPathJob"/>), a few per frame, so creatures never stall
    /// the main thread. <see cref="VoxelWorldManager"/> starts the queued searches late in a frame and collects them early
    /// in the next, so they run while the frame renders.
    /// <b>Host only</b> in practice: creatures are moved by the host (CLAUDE.md Law #2).
    /// </summary>
    public sealed class NavPathService : IDisposable
    {
        sealed class Running
        {
            public NavPathQuery Query;
            public NativeList<int3> Waypoints;
            public NativeArray<int> Result;
            public JobHandle Handle;
        }

        readonly VoxelNavGrid _grid;
        readonly int _maxJobsPerFrame;
        readonly Queue<NavPathQuery> _queued = new Queue<NavPathQuery>();
        readonly List<Running> _running = new List<Running>();
        readonly Stack<Running> _pool = new Stack<Running>();
        bool _isDisposed;

        public NavPathService(VoxelNavGrid grid, int maxJobsPerFrame = 8)
        {
            _grid = grid;
            _maxJobsPerFrame = math.max(1, maxJobsPerFrame);
        }

        public VoxelNavGrid Grid => _grid;

        /// <summary>Searches waiting for a free job.</summary>
        public int QueuedCount => _queued.Count;

        /// <summary>Searches running on worker threads.</summary>
        public int RunningCount => _running.Count;

        /// <summary>Paths found since the start, for the debug overlay.</summary>
        public int CompletedCount { get; private set; }

        /// <summary>
        /// Asks for a path for a body whose feet are at <paramref name="fromFeet"/> to <paramref name="toFeet"/>. The answer
        /// comes on a later frame.
        /// </summary>
        public NavPathQuery Request(float3 fromFeet, float3 toFeet, in NavAgentProfile profile)
        {
            var query = new NavPathQuery(FeetBlock(fromFeet), FeetBlock(toFeet), profile);
            _queued.Enqueue(query);
            return query;
        }

        /// <summary>The block feet at <paramref name="feet"/> stand in. A hair above a block top still counts as that cell.</summary>
        public static int3 FeetBlock(float3 feet) => (int3)math.floor(feet + new float3(0f, 0.01f, 0f));

        /// <summary>Starts up to the per-frame limit of queued searches.</summary>
        public void ScheduleQueued()
        {
            int started = 0;
            while (started < _maxJobsPerFrame && _queued.Count > 0)
            {
                NavPathQuery query = _queued.Dequeue();
                if (query.IsCancelled)
                    continue;

                Running running = _pool.Count > 0 ? _pool.Pop() : new Running
                {
                    Waypoints = new NativeList<int3>(32, Allocator.Persistent),
                    Result = new NativeArray<int>(2, Allocator.Persistent),
                };
                running.Query = query;
                query.GridStamp = _grid.Stamp;
                running.Handle = new FindPathJob
                {
                    Slots = _grid.Slots,
                    Bits = _grid.Bits,
                    Profile = query.Profile,
                    Start = query.Start,
                    Goal = query.Goal,
                    Waypoints = running.Waypoints,
                    Result = running.Result,
                }.Schedule();
                _grid.AddReader(running.Handle);
                _running.Add(running);
                started++;
            }
            JobHandle.ScheduleBatchedJobs();
        }

        /// <summary>Finishes every running search and hands out its path.</summary>
        public void CollectFinished()
        {
            foreach (Running running in _running)
            {
                running.Handle.Complete();
                NavPathQuery query = running.Query;
                query.WaypointList.Clear();
                for (int i = 0; i < running.Waypoints.Length; i++)
                    query.WaypointList.Add(running.Waypoints[i]);
                query.NodesExpanded = running.Result[1];
                query.Status = (NavPathStatus)running.Result[0];
                running.Query = null;
                _pool.Push(running);
                CompletedCount++;
            }
            _running.Clear();
        }

        /// <summary>Runs every queued search to the end, right now. For tests.</summary>
        public void CompleteAll()
        {
            while (_queued.Count > 0 || _running.Count > 0)
            {
                ScheduleQueued();
                CollectFinished();
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;
            _isDisposed = true;

            foreach (Running running in _running)
            {
                running.Handle.Complete();
                _pool.Push(running);
            }
            _running.Clear();
            _queued.Clear();
            while (_pool.Count > 0)
            {
                Running running = _pool.Pop();
                running.Waypoints.Dispose();
                running.Result.Dispose();
            }
        }
    }
}
