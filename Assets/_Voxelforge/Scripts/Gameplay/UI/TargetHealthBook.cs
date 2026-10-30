using System.Collections.Generic;
using Unity.Mathematics;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// What this PC has heard about targets' health (Roadmap 4.6), for the bars over Training Dummies. Filled from every
    /// hero's <see cref="AbilityCueKind.TargetHit"/> cues, so a partner's hits show too. A bar stays up while its target is
    /// being fought and goes when the host would have forgotten it (<see cref="SparringDummy.ResetAfter"/>). Plain C#, so
    /// it's tested without a scene; it never changes the game.
    /// </summary>
    public sealed class TargetHealthBook
    {
        public struct Entry
        {
            /// <summary>The target's middle, as the cue said.</summary>
            public float3 Middle;
            public int Health;
            public int MaxHealth;
            public float LastHit;
            /// <summary>When the target was last toppled, or -infinity.</summary>
            public float ToppledAt;

            public float Share => MaxHealth > 0 ? math.saturate(Health / (float)MaxHealth) : 0f;
        }

        /// <summary>Seconds a bar stays up after its target's last hit.</summary>
        public const float ShowSeconds = SparringDummy.ResetAfter;

        readonly List<Entry> _entries = new List<Entry>();

        public IReadOnlyList<Entry> Entries => _entries;

        /// <summary>A target whose middle is at <paramref name="middle"/> was hurt down to <paramref name="health"/>.</summary>
        public void Record(float3 middle, int health, int maxHealth, bool toppled, float now)
        {
            int index = Find(middle);
            Entry entry = index >= 0 ? _entries[index] : new Entry { Middle = middle, ToppledAt = float.NegativeInfinity };
            entry.Health = health;
            entry.MaxHealth = maxHealth;
            entry.LastHit = now;
            if (toppled)
                entry.ToppledAt = now;
            if (index >= 0)
                _entries[index] = entry;
            else
                _entries.Add(entry);
        }

        public void Expire(float now)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (now - _entries[i].LastHit >= ShowSeconds)
                    _entries.RemoveAt(i);
            }
        }

        public void Clear() => _entries.Clear();

        // A target is the same one if its middle is in the same place (dummies don't move).
        int Find(float3 middle)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (math.distancesq(_entries[i].Middle, middle) < 0.01f)
                    return i;
            }
            return -1;
        }
    }
}
