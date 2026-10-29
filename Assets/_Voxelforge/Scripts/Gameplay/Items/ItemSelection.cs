using System.Collections.Generic;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Which item from the bag a player holds (Roadmap 3.3, D-016; tools since 3.5): a block to build with or a tool to
    /// mine with. It steps through the different items in the bag in slot order, once per item even when it fills
    /// several slots (Roadmap 4.5).
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
            int slot = -1;
            return Fix(stacks, current, ref slot);
        }

        /// <summary>
        /// <see cref="Fix(IReadOnlyList{ItemStack}, ushort)"/> that remembers where the held item was in
        /// <paramref name="slot"/>: an emptied slot forgets its item (Roadmap 4.5), so this is how running out moves on to
        /// the next item along rather than back to the first.
        /// </summary>
        public static ushort Fix(IReadOnlyList<ItemStack> stacks, ushort current, ref int slot)
        {
            int index = IndexOf(stacks, current);
            if (index >= 0)
            {
                slot = index;
                return current;
            }
            int from = slot >= 0 && slot < stacks.Count ? slot : IndexOfAny(stacks, current);
            ushort next = Step(stacks, from, +1);
            slot = IndexOf(stacks, next);
            return next;
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
                if (!stacks[index].IsEmpty && IndexOf(stacks, stacks[index].ItemId) == index)
                    return stacks[index].ItemId;
            }
            return BlockIds.Air;
        }

        // Any slot naming it, even an empty one.
        static int IndexOfAny(IReadOnlyList<ItemStack> stacks, ushort itemId)
        {
            for (int i = 0; i < stacks.Count; i++)
            {
                if (stacks[i].ItemId == itemId && itemId != 0)
                    return i;
            }
            return -1;
        }

        static int IndexOf(IReadOnlyList<ItemStack> stacks, ushort itemId)
        {
            // The first non-empty slot holding it.
            for (int i = 0; i < stacks.Count; i++)
            {
                if (stacks[i].ItemId == itemId && !stacks[i].IsEmpty)
                    return i;
            }
            return -1;
        }
    }
}
