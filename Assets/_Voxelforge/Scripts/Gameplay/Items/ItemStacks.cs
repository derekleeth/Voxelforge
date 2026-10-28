using System;
using System.Collections.Generic;
using Unity.Netcode;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Some number of one item, e.g. 12 Dirt or 1 Stone Pickaxe. Blocks use their block ID as their item ID, other
    /// items start at <see cref="ItemIds.FirstNonBlock"/> (Roadmap 3.5). Sent as raw bytes, so keep it small and unmanaged.
    /// </summary>
    public struct ItemStack : INetworkSerializeByMemcpy, IEquatable<ItemStack>
    {
        public ushort ItemId;
        public ushort Count;

        public ItemStack(ushort itemId, ushort count)
        {
            ItemId = itemId;
            Count = count;
        }

        public bool Equals(ItemStack other) => ItemId == other.ItemId && Count == other.Count;
        public override bool Equals(object obj) => obj is ItemStack other && Equals(other);
        public override int GetHashCode() => ItemId | (Count << 16);
    }

    /// <summary>
    /// Counts of items by type: what a <see cref="PlayerBlockBag"/> holds (Roadmap 3.2, tools since 3.5). Plain C# so
    /// tests can drive it. Each item keeps its slot once it has one, even at zero, so a slot's index never changes and
    /// the bag can sync just the slot that changed.
    /// </summary>
    public sealed class ItemStacks
    {
        /// <summary>The most of one item a bag holds.</summary>
        public const int MaxPerItem = 999;

        readonly List<ItemStack> _stacks = new List<ItemStack>();

        public IReadOnlyList<ItemStack> Stacks => _stacks;

        public int CountOf(ushort itemId)
        {
            int index = IndexOf(itemId);
            return index < 0 ? 0 : _stacks[index].Count;
        }

        /// <summary>Adds up to <paramref name="amount"/> items, stopping at <see cref="MaxPerItem"/>. Returns how many fit.</summary>
        /// <param name="index">The slot that changed, or -1 if nothing did.</param>
        public int Add(ushort itemId, int amount, out int index)
        {
            index = -1;
            if (amount <= 0)
                return 0;

            int slot = IndexOf(itemId);
            int current = slot < 0 ? 0 : _stacks[slot].Count;
            int added = Math.Min(amount, MaxPerItem - current);
            if (added <= 0)
                return 0;

            var stack = new ItemStack(itemId, (ushort)(current + added));
            if (slot < 0)
            {
                slot = _stacks.Count;
                _stacks.Add(stack);
            }
            else
            {
                _stacks[slot] = stack;
            }
            index = slot;
            return added;
        }

        /// <summary>Takes <paramref name="amount"/> items if there are that many; otherwise takes none.</summary>
        /// <param name="index">The slot that changed, or -1 if nothing did.</param>
        public bool TryTake(ushort itemId, int amount, out int index)
        {
            index = IndexOf(itemId);
            if (amount <= 0 || index < 0 || _stacks[index].Count < amount)
            {
                index = -1;
                return false;
            }
            _stacks[index] = new ItemStack(itemId, (ushort)(_stacks[index].Count - amount));
            return true;
        }

        /// <summary>
        /// Crafting (Roadmap 3.5): takes every one of <paramref name="inputs"/> and adds <paramref name="output"/>, or
        /// changes nothing if an input is short or the output wouldn't fit. Inputs naming the same item add up.
        /// </summary>
        /// <param name="changed">Cleared, then filled with every slot that changed.</param>
        public bool TryExchange(IReadOnlyList<ItemStack> inputs, ItemStack output, List<int> changed)
        {
            changed.Clear();
            for (int i = 0; i < inputs.Count; i++)
            {
                if (CountOf(inputs[i].ItemId) < TotalOf(inputs, inputs[i].ItemId))
                    return false;
            }
            if (output.Count == 0 || CountOf(output.ItemId) - TotalOf(inputs, output.ItemId) + output.Count > MaxPerItem)
                return false;

            for (int i = 0; i < inputs.Count; i++)
            {
                if (inputs[i].Count > 0 && TryTake(inputs[i].ItemId, inputs[i].Count, out int index) && !changed.Contains(index))
                    changed.Add(index);
            }
            Add(output.ItemId, output.Count, out int outputIndex);
            if (outputIndex >= 0 && !changed.Contains(outputIndex))
                changed.Add(outputIndex);
            return true;
        }

        static int TotalOf(IReadOnlyList<ItemStack> stacks, ushort itemId)
        {
            int total = 0;
            for (int i = 0; i < stacks.Count; i++)
            {
                if (stacks[i].ItemId == itemId)
                    total += stacks[i].Count;
            }
            return total;
        }

        /// <summary>
        /// Replaces everything with saved slots (Roadmap 3.4), in the same order. An item listed twice is merged into
        /// its first slot, and counts stop at <see cref="MaxPerItem"/>.
        /// </summary>
        public void Restore(IEnumerable<ItemStack> stacks)
        {
            _stacks.Clear();
            if (stacks == null)
                return;
            foreach (ItemStack stack in stacks)
            {
                int slot = IndexOf(stack.ItemId);
                if (slot < 0)
                    _stacks.Add(new ItemStack(stack.ItemId, (ushort)Math.Min((int)stack.Count, MaxPerItem)));
                else
                    _stacks[slot] = new ItemStack(stack.ItemId, (ushort)Math.Min(_stacks[slot].Count + stack.Count, MaxPerItem));
            }
        }

        int IndexOf(ushort itemId)
        {
            for (int i = 0; i < _stacks.Count; i++)
            {
                if (_stacks[i].ItemId == itemId)
                    return i;
            }
            return -1;
        }
    }
}
