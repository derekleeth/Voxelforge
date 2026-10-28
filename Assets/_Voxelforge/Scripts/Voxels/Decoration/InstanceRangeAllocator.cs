using System;
using System.Collections.Generic;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// Hands out ranges of slots in one big instance buffer (Roadmap 2.6), so each chunk's decorations of one type sit
    /// together and only that range is uploaded when the chunk changes. First fit; freed ranges merge with free
    /// neighbours. When nothing fits, the capacity grows (at least doubling) and the caller must make a bigger buffer.
    /// Plain C# so tests can drive it.
    /// </summary>
    public sealed class InstanceRangeAllocator
    {
        // Free ranges as (start, count), sorted by start, never touching each other.
        readonly List<(int Start, int Count)> _free = new List<(int, int)>();

        public InstanceRangeAllocator(int capacity)
        {
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be at least 1.");
            Capacity = capacity;
            _free.Add((0, capacity));
        }

        public int Capacity { get; private set; }

        /// <summary>Slots in use.</summary>
        public int UsedCount { get; private set; }

        /// <summary>One past the last slot in use. The GPU only has to look at slots below this.</summary>
        public int UsedEnd
        {
            get
            {
                if (_free.Count == 0)
                    return Capacity;
                (int start, int count) = _free[_free.Count - 1];
                return start + count == Capacity ? start : Capacity;
            }
        }

        /// <summary>
        /// Reserves <paramref name="count"/> slots in a row and returns the first one. <paramref name="grew"/> is true
        /// when <see cref="Capacity"/> had to grow to fit them.
        /// </summary>
        public int Allocate(int count, out bool grew)
        {
            if (count < 1)
                throw new ArgumentOutOfRangeException(nameof(count), count, "Allocate at least 1 slot.");

            grew = false;
            int index = FindFit(count);
            if (index < 0)
            {
                Grow(Math.Max(Capacity * 2, Capacity + count));
                grew = true;
                index = FindFit(count);
            }

            (int start, int free) = _free[index];
            if (free == count)
                _free.RemoveAt(index);
            else
                _free[index] = (start + count, free - count);
            UsedCount += count;
            return start;
        }

        /// <summary>Gives back a range from <see cref="Allocate"/>.</summary>
        public void Free(int start, int count)
        {
            if (count < 1 || start < 0 || start + count > Capacity)
                throw new ArgumentOutOfRangeException(nameof(start), $"Range {start}+{count} is outside the buffer.");

            int index = 0;
            while (index < _free.Count && _free[index].Start < start)
                index++;
            if ((index > 0 && _free[index - 1].Start + _free[index - 1].Count > start) ||
                (index < _free.Count && start + count > _free[index].Start))
                throw new InvalidOperationException($"Range {start}+{count} is already free.");

            _free.Insert(index, (start, count));
            UsedCount -= count;

            // Merge with the next range, then the previous one.
            if (index + 1 < _free.Count && start + count == _free[index + 1].Start)
            {
                _free[index] = (start, count + _free[index + 1].Count);
                _free.RemoveAt(index + 1);
            }
            if (index > 0 && _free[index - 1].Start + _free[index - 1].Count == start)
            {
                _free[index - 1] = (_free[index - 1].Start, _free[index - 1].Count + _free[index].Count);
                _free.RemoveAt(index);
            }
        }

        int FindFit(int count)
        {
            for (int i = 0; i < _free.Count; i++)
            {
                if (_free[i].Count >= count)
                    return i;
            }
            return -1;
        }

        void Grow(int capacity)
        {
            int added = capacity - Capacity;
            if (_free.Count > 0 && _free[_free.Count - 1].Start + _free[_free.Count - 1].Count == Capacity)
                _free[_free.Count - 1] = (_free[_free.Count - 1].Start, _free[_free.Count - 1].Count + added);
            else
                _free.Add((Capacity, added));
            Capacity = capacity;
        }
    }
}
