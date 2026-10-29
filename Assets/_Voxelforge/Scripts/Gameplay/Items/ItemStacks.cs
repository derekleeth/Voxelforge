using System;
using System.Collections.Generic;
using Unity.Netcode;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Some number of one item, e.g. 12 Dirt or 1 Stone Pickaxe. Blocks use their block ID as their item ID, other
    /// items start at <see cref="ItemIds.FirstNonBlock"/> (Roadmap 3.5). An empty bag slot is item 0 (Air), count 0.
    /// Sent as raw bytes, so keep it small and unmanaged.
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

        public static ItemStack Empty => default;

        public bool IsEmpty => ItemId == 0 || Count == 0;

        public bool Equals(ItemStack other) => ItemId == other.ItemId && Count == other.Count;
        public override bool Equals(object obj) => obj is ItemStack other && Equals(other);
        public override int GetHashCode() => ItemId | (Count << 16);
        public override string ToString() => IsEmpty ? "(empty)" : $"{Count}x{ItemId}";
    }

    /// <summary>
    /// What a <see cref="PlayerBlockBag"/> holds (Roadmap 3.2; slots since 4.5, D-028): a fixed number of slots, each
    /// empty or a stack of one item up to its <see cref="IItemRules.MaxStackOf"/>. New items go into partial stacks
    /// of the same item, then the first empty slot, so nothing jumps around until <see cref="Sort"/>. Plain C# so tests
    /// can drive it.
    /// <para>Every change reports the slots it touched in a <c>changed</c> list (cleared first), so the bag can sync
    /// just those.</para>
    /// </summary>
    public sealed class ItemStacks
    {
        /// <summary>Slot numbers go over the network as a byte.</summary>
        public const int MaxCapacity = byte.MaxValue;

        readonly List<ItemStack> _slots = new List<ItemStack>();
        readonly IItemRules _rules;
        ItemStack[] _before = Array.Empty<ItemStack>();
        readonly List<ItemStack> _sorting = new List<ItemStack>();
        readonly List<int> _scratch = new List<int>();

        public ItemStacks(int capacity, IItemRules rules = null)
        {
            _rules = rules ?? ItemRules.Default;
            Fill(_slots, ClampCapacity(capacity));
        }

        public IReadOnlyList<ItemStack> Stacks => _slots;

        public int Capacity => _slots.Count;

        public IItemRules Rules => _rules;

        /// <summary>How many slots hold something.</summary>
        public int UsedSlots
        {
            get
            {
                int used = 0;
                foreach (ItemStack slot in _slots)
                {
                    if (!slot.IsEmpty)
                        used++;
                }
                return used;
            }
        }

        public int CountOf(ushort itemId) => CountOf(_slots, itemId);

        /// <summary>How many of the item are in <paramref name="slots"/>, across every stack.</summary>
        public static int CountOf(IReadOnlyList<ItemStack> slots, ushort itemId)
        {
            int total = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].ItemId == itemId)
                    total += slots[i].Count;
            }
            return total;
        }

        /// <summary>How many more of the item would fit: room on its stacks plus whole empty slots.</summary>
        public int RoomFor(ushort itemId)
        {
            if (itemId == 0)
                return 0;
            int max = MaxStackOf(itemId);
            int room = 0;
            foreach (ItemStack slot in _slots)
            {
                if (slot.IsEmpty)
                    room += max;
                else if (slot.ItemId == itemId)
                    room += Math.Max(0, max - slot.Count);
            }
            return room;
        }

        public int Add(ushort itemId, int amount) => Add(itemId, amount, _scratch);

        /// <summary>Adds up to <paramref name="amount"/>, topping up stacks of the same item first. Returns how many fit.</summary>
        public int Add(ushort itemId, int amount, List<int> changed)
        {
            changed.Clear();
            return AddInto(itemId, amount, changed);
        }

        public bool TryTake(ushort itemId, int amount) => TryTake(itemId, amount, _scratch);

        /// <summary>Takes <paramref name="amount"/> if there are that many, from the last stacks first; otherwise none.</summary>
        public bool TryTake(ushort itemId, int amount, List<int> changed)
        {
            changed.Clear();
            if (amount <= 0 || itemId == 0 || CountOf(itemId) < amount)
                return false;
            TakeInto(itemId, amount, changed);
            return true;
        }

        /// <summary>
        /// Crafting (Roadmap 3.5): takes every one of <paramref name="inputs"/> and adds <paramref name="output"/>, or
        /// changes nothing if an input is short or the output wouldn't fit. Inputs naming the same item add up.
        /// </summary>
        public bool TryExchange(IReadOnlyList<ItemStack> inputs, ItemStack output, List<int> changed)
        {
            changed.Clear();
            if (output.IsEmpty)
                return false;
            for (int i = 0; i < inputs.Count; i++)
            {
                if (CountOf(inputs[i].ItemId) < TotalOf(inputs, inputs[i].ItemId))
                    return false;
            }

            Remember();
            for (int i = 0; i < inputs.Count; i++)
            {
                if (inputs[i].Count > 0)
                    TakeInto(inputs[i].ItemId, inputs[i].Count, _scratch);
            }
            if (AddInto(output.ItemId, output.Count, _scratch) < output.Count)
            {
                Forget();
                return false;
            }
            Diff(changed);
            return true;
        }

        /// <summary>
        /// Moves slot <paramref name="from"/> onto slot <paramref name="to"/>: into it if it's empty, onto its stack if
        /// it's the same item (what doesn't fit stays behind), otherwise the two swap. False if nothing happened.
        /// </summary>
        public bool Move(int from, int to, List<int> changed)
        {
            changed.Clear();
            if (from == to || !InRange(from) || !InRange(to) || _slots[from].IsEmpty)
                return false;

            ItemStack source = _slots[from];
            ItemStack target = _slots[to];
            if (target.IsEmpty)
            {
                _slots[to] = source;
                _slots[from] = ItemStack.Empty;
            }
            else if (target.ItemId == source.ItemId)
            {
                int moved = Math.Min(source.Count, MaxStackOf(source.ItemId) - target.Count);
                if (moved <= 0)
                    return false;
                _slots[to] = new ItemStack(target.ItemId, (ushort)(target.Count + moved));
                _slots[from] = source.Count == moved ? ItemStack.Empty : new ItemStack(source.ItemId, (ushort)(source.Count - moved));
            }
            else
            {
                _slots[to] = source;
                _slots[from] = target;
            }
            changed.Add(from);
            changed.Add(to);
            return true;
        }

        /// <summary>Empties slot <paramref name="index"/> and returns what was in it (empty if nothing).</summary>
        public ItemStack TakeSlot(int index, List<int> changed)
        {
            changed.Clear();
            if (!InRange(index) || _slots[index].IsEmpty)
                return ItemStack.Empty;
            ItemStack stack = _slots[index];
            _slots[index] = ItemStack.Empty;
            changed.Add(index);
            return stack;
        }

        /// <summary>
        /// Auto-Sort (Roadmap 4.5): merges partial stacks and packs everything to the front, ordered by tab
        /// (<see cref="ItemCategory"/>) and then item ID. Never needs more slots than before.
        /// </summary>
        public void Sort(List<int> changed)
        {
            changed.Clear();
            Remember();

            _sorting.Clear();
            foreach (ItemStack slot in _slots)
            {
                if (slot.IsEmpty)
                    continue;
                int merged = _sorting.FindIndex(s => s.ItemId == slot.ItemId);
                if (merged < 0)
                    _sorting.Add(slot);
                else // Totals can pass a ushort only in theory (255 slots x 999); clamp rather than wrap.
                    _sorting[merged] = new ItemStack(slot.ItemId, (ushort)Math.Min(ushort.MaxValue, _sorting[merged].Count + slot.Count));
            }
            _sorting.Sort((a, b) =>
            {
                int byCategory = _rules.CategoryOf(a.ItemId).CompareTo(_rules.CategoryOf(b.ItemId));
                return byCategory != 0 ? byCategory : a.ItemId.CompareTo(b.ItemId);
            });

            for (int i = 0; i < _slots.Count; i++)
                _slots[i] = ItemStack.Empty;
            int next = 0;
            foreach (ItemStack total in _sorting)
            {
                int left = total.Count;
                int max = MaxStackOf(total.ItemId);
                while (left > 0 && next < _slots.Count)
                {
                    int put = Math.Min(left, max);
                    _slots[next++] = new ItemStack(total.ItemId, (ushort)put);
                    left -= put;
                }
            }
            Diff(changed);
        }

        /// <summary>
        /// Grows or shrinks to <paramref name="capacity"/> slots (equipping a bag, Roadmap 4.5). Shrinking moves what's in
        /// the slots that go into empty slots in front, and refuses (changing nothing) if there isn't room.
        /// <paramref name="changed"/> lists the kept slots whose contents moved.
        /// </summary>
        public bool TryResize(int capacity, List<int> changed)
        {
            changed.Clear();
            capacity = ClampCapacity(capacity);
            if (capacity >= _slots.Count)
            {
                Fill(_slots, capacity);
                return true;
            }

            int moving = 0;
            for (int i = capacity; i < _slots.Count; i++)
            {
                if (!_slots[i].IsEmpty)
                    moving++;
            }
            int free = 0;
            for (int i = 0; i < capacity; i++)
            {
                if (_slots[i].IsEmpty)
                    free++;
            }
            if (moving > free)
                return false;

            int front = 0;
            for (int i = capacity; i < _slots.Count; i++)
            {
                if (_slots[i].IsEmpty)
                    continue;
                while (!_slots[front].IsEmpty)
                    front++;
                _slots[front] = _slots[i];
                changed.Add(front);
            }
            _slots.RemoveRange(capacity, _slots.Count - capacity);
            return true;
        }

        /// <summary>
        /// Replaces everything with saved slots (Roadmap 3.4) at <paramref name="capacity"/> slots, keeping each saved
        /// slot's place. A stack too big for one slot (a v2 save's 3 Stone Pickaxes) spreads into empty slots.
        /// </summary>
        /// <returns>What didn't fit at all, for the host to drop on the ground. Usually empty.</returns>
        public List<ItemStack> Restore(IEnumerable<ItemStack> saved, int capacity)
        {
            _slots.Clear();
            Fill(_slots, ClampCapacity(capacity));
            var spill = new List<ItemStack>();
            if (saved == null)
                return spill;

            int index = 0;
            foreach (ItemStack stack in saved)
            {
                int i = index++;
                if (stack.IsEmpty)
                    continue;
                int max = MaxStackOf(stack.ItemId);
                if (i < _slots.Count && _slots[i].IsEmpty)
                {
                    int put = Math.Min(stack.Count, max);
                    _slots[i] = new ItemStack(stack.ItemId, (ushort)put);
                    if (stack.Count > put)
                        spill.Add(new ItemStack(stack.ItemId, (ushort)(stack.Count - put)));
                }
                else
                {
                    spill.Add(stack);
                }
            }

            var lost = new List<ItemStack>();
            foreach (ItemStack stack in spill)
            {
                int added = AddInto(stack.ItemId, stack.Count, _scratch);
                if (added < stack.Count)
                    lost.Add(new ItemStack(stack.ItemId, (ushort)(stack.Count - added)));
            }
            return lost;
        }

        /// <summary>Becomes an exact copy of <paramref name="slots"/> (a synced bag), to try things on without changing it.</summary>
        public void CopyFrom(IReadOnlyList<ItemStack> slots)
        {
            _slots.Clear();
            for (int i = 0; i < slots.Count && i < MaxCapacity; i++)
                _slots.Add(slots[i].IsEmpty ? ItemStack.Empty : slots[i]);
        }

        int AddInto(ushort itemId, int amount, List<int> changed)
        {
            if (amount <= 0 || itemId == 0)
                return 0;
            int max = MaxStackOf(itemId);
            int left = amount;
            for (int i = 0; i < _slots.Count && left > 0; i++)
            {
                ItemStack slot = _slots[i];
                if (slot.IsEmpty || slot.ItemId != itemId || slot.Count >= max)
                    continue;
                int put = Math.Min(left, max - slot.Count);
                _slots[i] = new ItemStack(itemId, (ushort)(slot.Count + put));
                left -= put;
                AddChanged(changed, i);
            }
            for (int i = 0; i < _slots.Count && left > 0; i++)
            {
                if (!_slots[i].IsEmpty)
                    continue;
                int put = Math.Min(left, max);
                _slots[i] = new ItemStack(itemId, (ushort)put);
                left -= put;
                AddChanged(changed, i);
            }
            return amount - left;
        }

        void TakeInto(ushort itemId, int amount, List<int> changed)
        {
            int left = amount;
            for (int i = _slots.Count - 1; i >= 0 && left > 0; i--)
            {
                ItemStack slot = _slots[i];
                if (slot.IsEmpty || slot.ItemId != itemId)
                    continue;
                int take = Math.Min(left, slot.Count);
                _slots[i] = take == slot.Count ? ItemStack.Empty : new ItemStack(itemId, (ushort)(slot.Count - take));
                left -= take;
                AddChanged(changed, i);
            }
        }

        // Remember / Forget / Diff: change freely, then undo or report what differs.
        void Remember()
        {
            if (_before.Length < _slots.Count)
                _before = new ItemStack[_slots.Count];
            _slots.CopyTo(_before);
        }

        void Forget()
        {
            for (int i = 0; i < _slots.Count; i++)
                _slots[i] = _before[i];
        }

        void Diff(List<int> changed)
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                if (!_slots[i].Equals(_before[i]))
                    changed.Add(i);
            }
        }

        int MaxStackOf(ushort itemId) => Math.Max(1, _rules.MaxStackOf(itemId));

        bool InRange(int index) => index >= 0 && index < _slots.Count;

        static void AddChanged(List<int> changed, int index)
        {
            if (!changed.Contains(index))
                changed.Add(index);
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

        static int ClampCapacity(int capacity) => Math.Clamp(capacity, 0, MaxCapacity);

        static void Fill(List<ItemStack> slots, int capacity)
        {
            while (slots.Count < capacity)
                slots.Add(ItemStack.Empty);
        }
    }
}
