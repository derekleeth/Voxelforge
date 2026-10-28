using System;
using System.Collections.Generic;
using Unity.Netcode;

namespace Voxelforge.Gameplay
{
    /// <summary>Some number of one block type, e.g. 12 Dirt. Sent as raw bytes, so keep it small and unmanaged.</summary>
    public struct BlockStack : INetworkSerializeByMemcpy, IEquatable<BlockStack>
    {
        public ushort BlockId;
        public ushort Count;

        public BlockStack(ushort blockId, ushort count)
        {
            BlockId = blockId;
            Count = count;
        }

        public bool Equals(BlockStack other) => BlockId == other.BlockId && Count == other.Count;
        public override bool Equals(object obj) => obj is BlockStack other && Equals(other);
        public override int GetHashCode() => BlockId | (Count << 16);
    }

    /// <summary>
    /// Counts of blocks by type: what a <see cref="PlayerBlockBag"/> holds (Roadmap 3.2). Plain C# so tests can drive
    /// it. Each block type keeps its slot once it has one, even at zero, so a slot's index never changes and the
    /// bag can sync just the slot that changed.
    /// </summary>
    public sealed class BlockStacks
    {
        /// <summary>The most of one block type a bag holds.</summary>
        public const int MaxPerBlock = 999;

        readonly List<BlockStack> _stacks = new List<BlockStack>();

        public IReadOnlyList<BlockStack> Stacks => _stacks;

        public int CountOf(ushort blockId)
        {
            int index = IndexOf(blockId);
            return index < 0 ? 0 : _stacks[index].Count;
        }

        /// <summary>Adds up to <paramref name="amount"/> blocks, stopping at <see cref="MaxPerBlock"/>. Returns how many fit.</summary>
        /// <param name="index">The slot that changed, or -1 if nothing did.</param>
        public int Add(ushort blockId, int amount, out int index)
        {
            index = -1;
            if (amount <= 0)
                return 0;

            int slot = IndexOf(blockId);
            int current = slot < 0 ? 0 : _stacks[slot].Count;
            int added = Math.Min(amount, MaxPerBlock - current);
            if (added <= 0)
                return 0;

            var stack = new BlockStack(blockId, (ushort)(current + added));
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

        /// <summary>Takes <paramref name="amount"/> blocks if there are that many; otherwise takes none.</summary>
        /// <param name="index">The slot that changed, or -1 if nothing did.</param>
        public bool TryTake(ushort blockId, int amount, out int index)
        {
            index = IndexOf(blockId);
            if (amount <= 0 || index < 0 || _stacks[index].Count < amount)
            {
                index = -1;
                return false;
            }
            _stacks[index] = new BlockStack(blockId, (ushort)(_stacks[index].Count - amount));
            return true;
        }

        int IndexOf(ushort blockId)
        {
            for (int i = 0; i < _stacks.Count; i++)
            {
                if (_stacks[i].BlockId == blockId)
                    return i;
            }
            return -1;
        }
    }
}
