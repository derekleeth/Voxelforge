using System.Collections.Generic;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Which block from the bag a player builds with (Roadmap 3.3, D-016). It steps through the bag's non-empty
    /// stacks in slot order. Slots never move (<see cref="BlockStacks"/>), so the order stays put as counts change.
    /// <see cref="BlockIds.Air"/> means nothing is selected. Plain C# so tests can drive it.
    /// </summary>
    public static class BlockSelection
    {
        /// <summary>
        /// <paramref name="current"/> if the bag still has some, otherwise the next block it does have
        /// (or <see cref="BlockIds.Air"/> when it's empty).
        /// </summary>
        public static ushort Fix(IReadOnlyList<BlockStack> stacks, ushort current)
        {
            int index = IndexOf(stacks, current);
            if (index >= 0 && stacks[index].Count > 0)
                return current;
            return Step(stacks, index, +1);
        }

        /// <summary>The next block in the bag after <paramref name="current"/>, wrapping round.</summary>
        public static ushort Next(IReadOnlyList<BlockStack> stacks, ushort current) => Step(stacks, IndexOf(stacks, current), +1);

        /// <summary>The block in the bag before <paramref name="current"/>, wrapping round.</summary>
        public static ushort Previous(IReadOnlyList<BlockStack> stacks, ushort current)
        {
            int index = IndexOf(stacks, current);
            return Step(stacks, index < 0 ? stacks.Count : index, -1);
        }

        /// <summary>The first non-empty stack after (or before) slot <paramref name="from"/>, wrapping round to it.</summary>
        static ushort Step(IReadOnlyList<BlockStack> stacks, int from, int direction)
        {
            int count = stacks.Count;
            for (int i = 1; i <= count; i++)
            {
                int index = ((from + direction * i) % count + count) % count;
                if (stacks[index].Count > 0)
                    return stacks[index].BlockId;
            }
            return BlockIds.Air;
        }

        static int IndexOf(IReadOnlyList<BlockStack> stacks, ushort blockId)
        {
            for (int i = 0; i < stacks.Count; i++)
            {
                if (stacks[i].BlockId == blockId)
                    return i;
            }
            return -1;
        }
    }
}
