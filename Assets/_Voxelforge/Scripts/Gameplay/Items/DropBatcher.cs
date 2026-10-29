using System.Collections.Generic;
using Unity.Mathematics;

namespace Voxelforge.Gameplay
{
    /// <summary>One stack of drops waiting to pop out (<see cref="DropBatcher"/>).</summary>
    public struct PendingDrop
    {
        public ushort BlockId;
        public int Count;
        /// <summary>The middle of one of the broken blocks: the one nearest the group's middle, so it's always air.</summary>
        public float3 Position;
        /// <summary>How many broken blocks went into this stack.</summary>
        public int Blocks;
    }

    /// <summary>
    /// Gathers the blocks broken in one frame and groups them into stacks by drop and by a coarse grid cell (Roadmap 4.3),
    /// so a crater or a felled tree pops out a few stacks instead of one network object per block. A single mined block
    /// is a group of one and drops just as before. Plain C#, so it's tested without a scene; <see cref="BlockDropSpawner"/>
    /// uses it on the host.
    /// </summary>
    public sealed class DropBatcher
    {
        struct Group
        {
            public int Count;
            public int Blocks;
            public float3 Sum;
        }

        readonly float _cellSize;
        readonly Dictionary<(ushort, int3), Group> _groups = new Dictionary<(ushort, int3), Group>();
        readonly List<((ushort, int3) Key, float3 Position)> _blocks = new List<((ushort, int3), float3)>();
        readonly Dictionary<(ushort, int3), (float3 Position, float Distance)> _nearest =
            new Dictionary<(ushort, int3), (float3, float)>();

        /// <param name="cellSize">Broken blocks closer than about this, in metres, share a stack.</param>
        public DropBatcher(float cellSize)
        {
            _cellSize = math.max(cellSize, 0.5f);
        }

        public bool IsEmpty => _blocks.Count == 0;

        /// <summary>Adds <paramref name="count"/> of <paramref name="blockId"/> from the block whose middle is <paramref name="position"/>.</summary>
        public void Add(float3 position, ushort blockId, int count)
        {
            if (count <= 0)
                return;
            var key = (blockId, (int3)math.floor(position / _cellSize));
            _groups.TryGetValue(key, out Group group);
            group.Count += count;
            group.Blocks++;
            group.Sum += position;
            _groups[key] = group;
            _blocks.Add((key, position));
        }

        /// <summary>Adds one stack per group to <paramref name="into"/> and starts over.</summary>
        public void Flush(List<PendingDrop> into)
        {
            if (IsEmpty)
                return;

            // Each group drops from its block nearest the group's middle.
            foreach (var (key, position) in _blocks)
            {
                float3 middle = _groups[key].Sum / _groups[key].Blocks;
                float distance = math.distancesq(position, middle);
                if (!_nearest.TryGetValue(key, out var best) || distance < best.Distance)
                    _nearest[key] = (position, distance);
            }

            foreach (var pair in _groups)
            {
                into.Add(new PendingDrop
                {
                    BlockId = pair.Key.Item1, Count = pair.Value.Count, Blocks = pair.Value.Blocks,
                    Position = _nearest[pair.Key].Position,
                });
            }
            _groups.Clear();
            _blocks.Clear();
            _nearest.Clear();
        }
    }
}
