using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// The parts of the world one player has seen (Roadmap 4.8, D-031): the map's fog of war. The world is split into
    /// <see cref="CellSize"/>-block square cells (one per chunk column), and a cell is either explored or not. Cells only
    /// ever get explored, never forgotten. The host keeps each hero's map (<see cref="PlayerExplorer"/>) and saves it in
    /// their <see cref="PlayerProfile"/>; the owner's PC gets a copy to draw the fog from.
    /// </summary>
    public sealed class ExploredMap
    {
        /// <summary>Blocks along each side of a cell. The same as a chunk, so a cell is a chunk column.</summary>
        public const int CellSize = 16;

        readonly HashSet<int2> _cells;

        public ExploredMap() => _cells = new HashSet<int2>();

        public ExploredMap(IEnumerable<int2> cells) => _cells = new HashSet<int2>(cells ?? Array.Empty<int2>());

        public int Count => _cells.Count;

        /// <summary>Counts up whenever a cell is explored, so a map can tell when to redraw its fog.</summary>
        public int Version { get; private set; }

        /// <summary>Raised with each batch of newly explored cells.</summary>
        public event Action<IReadOnlyList<int2>> Explored;

        public IReadOnlyCollection<int2> Cells => _cells;

        /// <summary>The cell holding world column (<paramref name="x"/>, <paramref name="z"/>). Works for negative columns too.</summary>
        public static int2 CellOf(float x, float z) => (int2)math.floor(new float2(x, z) / CellSize);

        public bool IsExplored(int2 cell) => _cells.Contains(cell);

        /// <summary>Explored if the cell holding world column (<paramref name="x"/>, <paramref name="z"/>) is.</summary>
        public bool IsExplored(float x, float z) => _cells.Contains(CellOf(x, z));

        /// <summary>
        /// Explores every cell within <paramref name="radius"/> cells of <paramref name="center"/> (a circle, measured
        /// between cell centres). Adds the cells that are new to <paramref name="added"/> (if given) and returns how many.
        /// </summary>
        public int Reveal(int2 center, int radius, List<int2> added = null)
        {
            int before = _cells.Count;
            int start = added?.Count ?? 0;
            for (int z = -radius; z <= radius; z++)
            for (int x = -radius; x <= radius; x++)
            {
                if (x * x + z * z > radius * radius + radius) // + radius rounds the circle out a little.
                    continue;
                int2 cell = center + new int2(x, z);
                if (_cells.Add(cell))
                    added?.Add(cell);
            }
            int count = _cells.Count - before;
            if (count > 0)
            {
                Version++;
                if (added != null && Explored != null)
                    Explored.Invoke(added.GetRange(start, added.Count - start));
            }
            return count;
        }

        /// <summary>Explores <paramref name="cells"/>, e.g. a batch the host sent. Returns how many were new.</summary>
        public int Add(IReadOnlyList<int2> cells)
        {
            List<int2> added = null;
            foreach (int2 cell in cells)
            {
                if (_cells.Add(cell))
                    (added ??= new List<int2>()).Add(cell);
            }
            if (added == null)
                return 0;
            Version++;
            Explored?.Invoke(added);
            return added.Count;
        }

        /// <summary>A copy of every explored cell, e.g. for the save.</summary>
        public HashSet<int2> Snapshot() => new HashSet<int2>(_cells);
    }
}
