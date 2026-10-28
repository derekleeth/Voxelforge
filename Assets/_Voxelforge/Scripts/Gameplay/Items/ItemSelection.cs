using System.Collections.Generic;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Which item from the bag a player holds (Roadmap 3.3, D-016; tools since 3.5): a block to build with or a tool to
    /// mine with. It steps through the bag's non-empty
    /// stacks in slot order. Slots never move (<see cref="ItemStacks"/>), so the order stays put as counts change.
    /// <see cref="BlockIds.Air"/> means nothing is selected. Plain C# so tests can drive it.
    /// </summary>
    public static class ItemSelection
    {
        /// <summary>
        /// <paramref name="current"/> if the bag still has some, otherwise the next item it does have
        /// (or <see cref="BlockIds.Air"/> when it's empty).
        /// </summary>
        public static ushort Fix(IReadOnlyList<ItemStack> stacks, ushort current)
        {
            int index = IndexOf(stacks, current);
            if (index >= 0 && stacks[index].Count > 0)
                return current;
            return Step(stacks, index, +1);
        }

        /// <summary>The next item in the bag after <paramref name="current"/>, wrapping round.</summary>
        public static ushort Next(IReadOnlyList<ItemStack> stacks, ushort current) => Step(stacks, IndexOf(stacks, current), +1);

        /// <summary>The item in the bag before <paramref name="current"/>, wrapping round.</summary>
        public static ushort Previous(IReadOnlyList<ItemStack> stacks, ushort current)
        {
            int index = IndexOf(stacks, current);
            return Step(stacks, index < 0 ? stacks.Count : index, -1);
        }

        /// <summary>The first non-empty stack after (or before) slot <paramref name="from"/>, wrapping round to it.</summary>
        static ushort Step(IReadOnlyList<ItemStack> stacks, int from, int direction)
        {
            int count = stacks.Count;
            for (int i = 1; i <= count; i++)
            {
                int index = ((from + direction * i) % count + count) % count;
                if (stacks[index].Count > 0)
                    return stacks[index].ItemId;
            }
            return BlockIds.Air;
        }

        static int IndexOf(IReadOnlyList<ItemStack> stacks, ushort itemId)
        {
            for (int i = 0; i < stacks.Count; i++)
            {
                if (stacks[i].ItemId == itemId)
                    return i;
            }
            return -1;
        }
    }
}
