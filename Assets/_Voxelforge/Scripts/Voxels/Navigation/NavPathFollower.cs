using System.Collections.Generic;
using Unity.Mathematics;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// Walks one creature along paths from the <see cref="NavPathService"/> (Roadmap 5.1). Each fixed tick, give it where the
    /// body's feet are and it returns the <see cref="MotorInput"/> for <see cref="VoxelBodyMotor.Step"/>. Creatures then move
    /// exactly like heroes: same collision, same auto-step. Plain C#, so tests drive it without a scene.
    /// <para>It asks for a new path, at most once per <see cref="RepathSeconds"/>, when:</para>
    /// <list type="bullet">
    /// <item>a chunk the path crosses changed since the path was planned (someone dug a trench or built a wall),</item>
    /// <item>the destination moved more than <see cref="GoalMoveDistance"/> from where the path ends,</item>
    /// <item>the body stopped getting closer to its next waypoint for <see cref="StuckSeconds"/>,</item>
    /// <item>or it reached the end of a partial path (the way may be open now).</item>
    /// </list>
    /// While a new path is being found it keeps walking the old one.
    /// </summary>
    public sealed class NavPathFollower
    {
        readonly NavPathService _paths;
        readonly List<int3> _waypoints = new List<int3>();
        NavAgentProfile _profile;
        NavPathQuery _query;
        bool _hasPath;
        int _next;
        int3 _pathGoal;
        long _pathStamp;
        int3 _regionMin, _regionMax;
        float _nextRequestAt = float.NegativeInfinity;
        float _closest = float.PositiveInfinity;
        float _closestAt;
        bool _stuck;

        public NavPathFollower(NavPathService paths, in NavAgentProfile profile)
        {
            _paths = paths;
            _profile = profile;
        }

        /// <summary>Shortest gap between path searches, in seconds.</summary>
        public float RepathSeconds = 0.5f;

        /// <summary>A waypoint counts as reached this close to its middle, in blocks.</summary>
        public float ArriveDistance = 0.3f;

        /// <summary>Seconds without getting closer to the next waypoint before trying a new path (and a hop).</summary>
        public float StuckSeconds = 1f;

        /// <summary>How far the destination may move, in blocks, before the path is planned again.</summary>
        public float GoalMoveDistance = 1.5f;

        public NavAgentProfile Profile => _profile;

        public bool HasDestination { get; private set; }

        /// <summary>Where the feet should end up.</summary>
        public float3 Destination { get; private set; }

        /// <summary>The path being walked: feet blocks, not including where it started.</summary>
        public IReadOnlyList<int3> Waypoints => _waypoints;

        /// <summary>Index into <see cref="Waypoints"/> of the one being walked to.</summary>
        public int NextWaypoint => _next;

        /// <summary>How the path being walked was found, or <see cref="NavPathStatus.Pending"/> before the first one.</summary>
        public NavPathStatus Status { get; private set; }

        /// <summary>Walked to the end of a complete path.</summary>
        public bool HasArrived => _hasPath && Status == NavPathStatus.Complete && _next >= _waypoints.Count;

        /// <summary>A path search is running.</summary>
        public bool IsSearching => _query != null;

        /// <summary>How many paths it has asked for.</summary>
        public int RequestCount { get; private set; }

        public void SetDestination(float3 feet)
        {
            Destination = feet;
            HasDestination = true;
        }

        /// <summary>Forgets the destination and the path; the body stands still.</summary>
        public void Stop()
        {
            HasDestination = false;
            _query?.Cancel();
            _query = null;
            _hasPath = false;
            _waypoints.Clear();
            Status = NavPathStatus.Pending;
        }

        /// <summary>One tick: the body's feet are at <paramref name="feet"/>, the time is <paramref name="now"/> seconds.</summary>
        public MotorInput Tick(float3 feet, float now)
        {
            if (!HasDestination)
                return default;

            if (_query != null && _query.IsDone)
                TakePath(_query, feet, now);
            if (_query == null && now >= _nextRequestAt && NeedsNewPath())
            {
                _query = _paths.Request(feet, Destination, _profile);
                _nextRequestAt = now + RepathSeconds;
                RequestCount++;
            }

            if (!_hasPath)
                return default;

            // Skip waypoints already reached. A waypoint on another level needs the feet near its height too.
            float2 toNext = default;
            float distance = 0f;
            while (_next < _waypoints.Count)
            {
                int3 waypoint = _waypoints[_next];
                toNext = (float2)waypoint.xz + 0.5f - feet.xz;
                distance = math.length(toNext);
                if (distance > ArriveDistance || math.abs(feet.y - waypoint.y) > 1.5f)
                    break;
                _next++;
                ResetProgress(now);
            }
            if (_next >= _waypoints.Count)
                return default;

            if (distance < _closest - 0.1f)
            {
                _closest = distance;
                _closestAt = now;
            }
            else if (now - _closestAt > StuckSeconds)
            {
                _stuck = true;
            }

            return new MotorInput { Move = toNext / math.max(distance, 1e-4f), Jump = _stuck };
        }

        bool NeedsNewPath()
        {
            if (!_hasPath || _stuck)
                return true;
            if (_paths.Grid.StampIn(_regionMin, _regionMax) > _pathStamp)
                return true;
            float3 goal = new float3(_pathGoal.x + 0.5f, _pathGoal.y, _pathGoal.z + 0.5f);
            if (math.distance(goal, Destination) > GoalMoveDistance)
                return true;
            return Status == NavPathStatus.Partial && _next >= _waypoints.Count;
        }

        void TakePath(NavPathQuery query, float3 feet, float now)
        {
            _query = null;
            _waypoints.Clear();
            _waypoints.AddRange(query.Waypoints);
            _next = 0;
            Status = query.Status;
            _hasPath = query.Status != NavPathStatus.NoPath;
            _pathGoal = query.Goal;
            _pathStamp = query.GridStamp;
            _stuck = false;
            ResetProgress(now);

            // The chunks the path crosses, and the layer below for the floor under it.
            int3 min = query.Start, max = query.Start;
            foreach (int3 waypoint in _waypoints)
            {
                min = math.min(min, waypoint);
                max = math.max(max, waypoint);
            }
            int3 feetBlock = NavPathService.FeetBlock(feet);
            min = math.min(min, feetBlock) - new int3(0, 1, 0);
            max = math.max(max, feetBlock) + new int3(0, _profile.Height, 0);
            _regionMin = VoxelMath.WorldToChunk(min).Value;
            _regionMax = VoxelMath.WorldToChunk(max).Value;
        }

        void ResetProgress(float now)
        {
            _closest = float.PositiveInfinity;
            _closestAt = now;
        }
    }
}
