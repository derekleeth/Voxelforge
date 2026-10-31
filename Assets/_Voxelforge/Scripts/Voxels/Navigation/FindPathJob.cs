using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Voxelforge.Voxels
{
    /// <summary>How a path search ended.</summary>
    public enum NavPathStatus : byte
    {
        /// <summary>Not searched yet.</summary>
        Pending,
        /// <summary>The path reaches the goal.</summary>
        Complete,
        /// <summary>The goal couldn't be reached (walled in, too far, out of budget): the path goes as close as it can.</summary>
        Partial,
        /// <summary>No step can be taken: the start isn't on the ground, or it's boxed in.</summary>
        NoPath,
    }

    /// <summary>
    /// Finds a walking path over the <see cref="VoxelNavGrid"/> with A* (Roadmap 5.1), on a worker thread.
    /// <para>
    /// A cell is somewhere a body's feet can be: a solid block below and <see cref="NavAgentProfile.Height"/> open blocks
    /// from there up. From a cell a body can walk to any of its 8 neighbours on the same level, step up onto a ledge as
    /// high as <see cref="NavAgentProfile.MaxStepUp"/> (if there's head room to rise, as the motor needs), or walk off a
    /// ledge as deep as <see cref="NavAgentProfile.MaxDrop"/>. Diagonal moves need both side cells open too, so a path
    /// never cuts a wall's corner.
    /// </para>
    /// <para>
    /// The cell path is then pulled tight: runs of cells on one level a body can walk in a straight line become one
    /// waypoint, so creatures walk straight across open ground instead of zig-zagging along the grid.
    /// </para>
    /// </summary>
    [BurstCompile]
    public struct FindPathJob : IJob
    {
        const float DiagonalCost = 1.41421356f;
        /// <summary>Extra cost per block climbed, so a flat way round wins over a hill of the same length.</summary>
        const float ClimbCost = 0.5f;
        /// <summary>Extra cost per block dropped. Lower than climbing, but not free.</summary>
        const float DropCost = 0.2f;
        /// <summary>How far apart a straight line is checked when pulling the path tight, in blocks.</summary>
        const float SmoothSample = 0.25f;

        /// <summary>The 8 ways out of a cell on the ground plane (x, z): 4 straight, then 4 diagonal.</summary>
        static int2 Direction(int index) => index switch
        {
            0 => new int2(1, 0),
            1 => new int2(-1, 0),
            2 => new int2(0, 1),
            3 => new int2(0, -1),
            4 => new int2(1, 1),
            5 => new int2(1, -1),
            6 => new int2(-1, 1),
            _ => new int2(-1, -1),
        };

        [ReadOnly] public NativeHashMap<int3, int> Slots;
        [ReadOnly] public NativeArray<uint> Bits;

        public NavAgentProfile Profile;

        /// <summary>The block the body's feet are in. It's snapped onto the ground below if it's in the air.</summary>
        public int3 Start;

        /// <summary>The block the body wants its feet in, snapped onto the ground below the same way.</summary>
        public int3 Goal;

        /// <summary>Output: the cells to walk to in turn, not including the start. Their feet blocks.</summary>
        public NativeList<int3> Waypoints;

        /// <summary>Output: [0] the <see cref="NavPathStatus"/>, [1] how many cells were searched.</summary>
        public NativeArray<int> Result;

        struct Node
        {
            public float G;
            public int3 Parent;
            public bool Closed;
        }

        struct OpenEntry
        {
            public int3 Cell;
            public float F;
            public float G;
        }

        public void Execute()
        {
            var grid = new NavGridView(Slots, Bits);
            Waypoints.Clear();
            Result[1] = 0;
            if (!TrySnapToGround(grid, Start, out int3 start))
            {
                Result[0] = (int)NavPathStatus.NoPath;
                return;
            }
            if (!TrySnapToGround(grid, Goal, out int3 goal))
                goal = Goal; // Somewhere nobody can stand: head for as close as we can get.

            var nodes = new NativeHashMap<int3, Node>(math.max(64, Profile.MaxNodes * 2), Allocator.Temp);
            var open = new NativeList<OpenEntry>(256, Allocator.Temp);
            nodes[start] = new Node { G = 0f, Parent = start };
            Push(open, new OpenEntry { Cell = start, F = Heuristic(start, goal), G = 0f });

            int3 best = start;
            float bestH = Heuristic(start, goal);
            float bestG = 0f;
            int expanded = 0;
            bool found = false;
            while (open.Length > 0)
            {
                OpenEntry entry = Pop(open);
                Node node = nodes[entry.Cell];
                if (node.Closed || entry.G > node.G)
                    continue; // An older, longer way here.
                node.Closed = true;
                nodes[entry.Cell] = node;

                if (math.all(entry.Cell == goal))
                {
                    found = true;
                    break;
                }
                float h = Heuristic(entry.Cell, goal);
                if (h < bestH || (h == bestH && entry.G < bestG))
                {
                    best = entry.Cell;
                    bestH = h;
                    bestG = entry.G;
                }
                if (++expanded >= Profile.MaxNodes)
                    break;

                for (int d = 0; d < 8; d++)
                {
                    if (!TryMove(grid, entry.Cell, Direction(d), out int3 to, out float cost))
                        continue;
                    int2 offset = math.abs(to.xz - start.xz);
                    if (math.cmax(offset) > Profile.MaxRange)
                        continue;
                    float g = entry.G + cost;
                    if (nodes.TryGetValue(to, out Node known) && (known.Closed || known.G <= g))
                        continue;
                    nodes[to] = new Node { G = g, Parent = entry.Cell };
                    Push(open, new OpenEntry { Cell = to, F = g + Heuristic(to, goal), G = g });
                }
            }

            Result[1] = expanded;
            int3 end = found ? goal : best;
            if (!found && math.all(end == start))
            {
                Result[0] = (int)NavPathStatus.NoPath;
                return;
            }

            var cells = new NativeList<int3>(64, Allocator.Temp);
            for (int3 cell = end; ; cell = nodes[cell].Parent)
            {
                cells.Add(cell);
                if (math.all(cell == start))
                    break;
            }
            Reverse(cells);
            Smooth(grid, cells);
            Result[0] = (int)(found ? NavPathStatus.Complete : NavPathStatus.Partial);
        }

        /// <summary>
        /// One move from <paramref name="from"/> in <paramref name="direction"/> (x, z), if the body can make it: along the
        /// level, up a step, or down off a ledge. At most one move per direction.
        /// </summary>
        bool TryMove(in NavGridView grid, int3 from, int2 direction, out int3 to, out float cost)
        {
            int height = Profile.Height;
            var up = new int3(0, 1, 0);
            int3 next = from + new int3(direction.x, 0, direction.y);
            to = next;

            if (direction.x != 0 && direction.y != 0)
            {
                // Diagonals stay level and need both side cells open, so they never clip a corner.
                cost = DiagonalCost;
                return grid.IsClear(from + new int3(direction.x, 0, 0), height) &&
                       grid.IsClear(from + new int3(0, 0, direction.y), height) &&
                       grid.IsStandable(next, height);
            }

            if (grid.IsClear(next, height))
            {
                cost = 1f;
                if (grid.IsSolid(next - up))
                    return true;
                // Nothing underfoot: fall down the open column to the first floor, if it isn't too deep.
                for (int drop = 1; drop <= Profile.MaxDrop; drop++)
                {
                    int3 below = next - up * drop;
                    if (grid.IsSolid(below))
                        return false;
                    if (grid.IsSolid(below - up))
                    {
                        to = below;
                        cost = 1f + DropCost * drop;
                        return true;
                    }
                }
                return false;
            }

            // Something's in the way: climb onto it, if there's room to rise where we stand and room to stand up there.
            for (int step = 1; step <= Profile.MaxStepUp; step++)
            {
                if (grid.IsSolid(from + up * (height + step - 1)))
                    break;
                int3 raised = next + up * step;
                if (grid.IsStandable(raised, height))
                {
                    to = raised;
                    cost = 1f + ClimbCost * step;
                    return true;
                }
            }
            cost = 0f;
            return false;
        }

        /// <summary>
        /// The standable cell at or just below <paramref name="cell"/> (a body mid-jump, or a hero on a ledge), or one block up
        /// (feet resting a hair inside the floor).
        /// </summary>
        bool TrySnapToGround(in NavGridView grid, int3 cell, out int3 ground)
        {
            var up = new int3(0, 1, 0);
            for (int drop = 0; drop <= Profile.MaxDrop + 1; drop++)
            {
                ground = cell - up * drop;
                if (grid.IsStandable(ground, Profile.Height))
                    return true;
                if (grid.IsSolid(ground))
                    break;
            }
            ground = cell + up;
            return grid.IsStandable(ground, Profile.Height);
        }

        /// <summary>
        /// Pulls the cell path tight: from each waypoint, skips ahead to the furthest cell on the same level the body can
        /// walk to in a straight line. Writes the waypoints after the start to <see cref="Waypoints"/>.
        /// </summary>
        void Smooth(in NavGridView grid, NativeList<int3> cells)
        {
            int anchor = 0;
            while (anchor < cells.Length - 1)
            {
                int next = anchor + 1;
                while (next + 1 < cells.Length && CanWalkStraight(grid, cells[anchor], cells[next + 1]))
                    next++;
                Waypoints.Add(cells[next]);
                anchor = next;
            }
        }

        /// <summary>
        /// A body can walk straight from the middle of <paramref name="a"/> to the middle of <paramref name="b"/> on one level:
        /// every point along the way, widened by the body's radius, is standable.
        /// </summary>
        bool CanWalkStraight(in NavGridView grid, int3 a, int3 b)
        {
            if (a.y != b.y)
                return false;
            float2 from = (float2)a.xz + 0.5f;
            float2 to = (float2)b.xz + 0.5f;
            float length = math.distance(from, to);
            int samples = math.max(1, (int)math.ceil(length / SmoothSample));
            float radius = Profile.Radius + 0.05f;
            for (int i = 1; i <= samples; i++)
            {
                float2 point = math.lerp(from, to, i / (float)samples);
                for (int corner = 0; corner < 4; corner++)
                {
                    float2 offset = new float2((corner & 1) == 0 ? -radius : radius, (corner & 2) == 0 ? -radius : radius);
                    int2 column = (int2)math.floor(point + offset);
                    if (!grid.IsStandable(new int3(column.x, a.y, column.y), Profile.Height))
                        return false;
                }
            }
            return true;
        }

        /// <summary>Octile distance across the ground plus the cheapest possible cost of the height difference. Never too high.</summary>
        static float Heuristic(int3 cell, int3 goal)
        {
            int3 delta = math.abs(goal - cell);
            float straight = math.max(delta.x, delta.z) - math.min(delta.x, delta.z);
            return straight + DiagonalCost * math.min(delta.x, delta.z) + DropCost * delta.y;
        }

        static void Reverse(NativeList<int3> list)
        {
            for (int i = 0, j = list.Length - 1; i < j; i++, j--)
                (list[i], list[j]) = (list[j], list[i]);
        }

        static void Push(NativeList<OpenEntry> heap, OpenEntry entry)
        {
            heap.Add(entry);
            int i = heap.Length - 1;
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (heap[parent].F <= entry.F)
                    break;
                heap[i] = heap[parent];
                i = parent;
            }
            heap[i] = entry;
        }

        static OpenEntry Pop(NativeList<OpenEntry> heap)
        {
            OpenEntry top = heap[0];
            OpenEntry last = heap[heap.Length - 1];
            heap.RemoveAt(heap.Length - 1);
            int count = heap.Length;
            if (count == 0)
                return top;

            int i = 0;
            while (true)
            {
                int child = 2 * i + 1;
                if (child >= count)
                    break;
                if (child + 1 < count && heap[child + 1].F < heap[child].F)
                    child++;
                if (last.F <= heap[child].F)
                    break;
                heap[i] = heap[child];
                i = child;
            }
            heap[i] = last;
            return top;
        }
    }
}
