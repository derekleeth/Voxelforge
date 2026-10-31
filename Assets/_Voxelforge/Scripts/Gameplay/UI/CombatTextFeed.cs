using System.Collections.Generic;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// The lines of floating combat text up in one player's view (Roadmap 4.4), without drawing them: how long each
    /// lives, a fixed pool of label slots (the oldest line gives way when it's full), and pickups of the same item close
    /// together merging into one growing "+N". Plain C#, so it's tested without a scene; <see cref="CombatTextView"/>
    /// draws it.
    /// </summary>
    public sealed class CombatTextFeed
    {
        /// <summary>Pickups of the same item this close together (seconds) add up on one line.</summary>
        public const float MergeSeconds = 0.3f;

        public struct Entry
        {
            public CombatTextEvent Event;
            /// <summary>When it appeared (or last grew).</summary>
            public float Born;
            /// <summary>Which pooled label shows it, 0 to capacity - 1. Stays the same for the line's whole life.</summary>
            public int Label;
            /// <summary>Changes whenever the words change, so the view only rebuilds text then.</summary>
            public int Version;
        }

        readonly List<Entry> _entries = new List<Entry>();
        readonly Stack<int> _freeLabels = new Stack<int>();
        int _version;

        public CombatTextFeed(int capacity)
        {
            Capacity = capacity;
            for (int i = capacity - 1; i >= 0; i--)
                _freeLabels.Push(i);
        }

        public int Capacity { get; }

        /// <summary>The lines up now, oldest first.</summary>
        public IReadOnlyList<Entry> Entries => _entries;

        /// <summary>Seconds a line of this kind stays up.</summary>
        public static float Lifetime(CombatTextKind kind)
        {
            switch (kind)
            {
                case CombatTextKind.CraterHit:
                case CombatTextKind.Toppled:
                case CombatTextKind.Stunned:
                    return 1.6f;
                case CombatTextKind.Refused:
                case CombatTextKind.BagRefused:
                    return 1.2f;
                case CombatTextKind.Pickup:
                case CombatTextKind.Sent:
                case CombatTextKind.Shielded:
                    return 1.4f;
                default:
                    return 1f;
            }
        }

        public void Push(in CombatTextEvent e, float now)
        {
            if (Capacity <= 0)
                return;
            if (e.Kind == CombatTextKind.Pickup)
            {
                for (int i = _entries.Count - 1; i >= 0; i--)
                {
                    Entry entry = _entries[i];
                    if (entry.Event.Kind != CombatTextKind.Pickup || entry.Event.ItemId != e.ItemId || now - entry.Born > MergeSeconds)
                        continue;
                    entry.Event.Amount += e.Amount;
                    entry.Born = now;
                    entry.Version = ++_version;
                    // Move it to the end, so entries stay oldest first.
                    _entries.RemoveAt(i);
                    _entries.Add(entry);
                    return;
                }
            }
            if (_freeLabels.Count == 0)
                RemoveAt(0);
            _entries.Add(new Entry { Event = e, Born = now, Label = _freeLabels.Pop(), Version = ++_version });
        }

        /// <summary>Drops the lines whose time is up.</summary>
        public void Expire(float now)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (now - _entries[i].Born >= Lifetime(_entries[i].Event.Kind))
                    RemoveAt(i);
            }
        }

        public void Clear()
        {
            while (_entries.Count > 0)
                RemoveAt(_entries.Count - 1);
        }

        void RemoveAt(int index)
        {
            _freeLabels.Push(_entries[index].Label);
            _entries.RemoveAt(index);
        }
    }
}
