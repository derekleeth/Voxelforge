using System.Collections.Generic;
using Unity.Mathematics;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// What this PC has heard about targets' health (Roadmap 4.6), for the bars over Training Dummies and creatures (5.2).
    /// Filled from every hero's <see cref="AbilityCueKind.TargetHit"/> cues, so a partner's hits show too. A bar stays up
    /// while its target is being fought and goes when the host would have forgotten it
    /// (<see cref="SparringDummy.ResetAfter"/>), or when its creature leaves the world. Plain C#, so it's tested without
    /// a scene; it never changes the game.
    /// </summary>
    public sealed class TargetHealthBook
    {
        public struct Entry
        {
            /// <summary>Which target this is (<see cref="TargetKey"/>).</summary>
            public TargetKey Key;
            /// <summary>The target's middle: where the cue said, or where its creature is now (<see cref="Follow"/>).</summary>
            public float3 Middle;
            /// <summary>Just over its head, where its bar floats.</summary>
            public float3 Top;
            public int Health;
            public int MaxHealth;
            public float LastHit;
            /// <summary>When the target was last toppled (or defeated), or -infinity.</summary>
            public float ToppledAt;

            public float Share => MaxHealth > 0 ? math.saturate(Health / (float)MaxHealth) : 0f;
        }

        /// <summary>Seconds a bar stays up after its target's last hit.</summary>
        public const float ShowSeconds = SparringDummy.ResetAfter;

        readonly List<Entry> _entries = new List<Entry>();

        public IReadOnlyList<Entry> Entries => _entries;

        /// <summary>
        /// The target <paramref name="key"/>, whose middle was at <paramref name="middle"/>, was hurt down to
        /// <paramref name="health"/>.
        /// </summary>
        public void Record(TargetKey key, float3 middle, int health, int maxHealth, bool toppled, float now)
        {
            int index = Find(key);
            Entry entry = index >= 0
                ? _entries[index]
                : new Entry
                {
                    Key = key, Middle = middle, Top = middle + new float3(0f, CombatTarget.BodyHeight * 0.5f + 0.35f, 0f),
                    ToppledAt = float.NegativeInfinity,
                };
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

        /// <summary>
        /// Moves each creature's bar to where it stands now (Roadmap 5.2), and drops the bars of creatures that left the
        /// world. Dummies don't move.
        /// </summary>
        public void Follow(CombatTargets targets)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                Entry entry = _entries[i];
                if (!entry.Key.IsCreature)
                    continue;
                if (targets == null || !targets.TryGetCreature(entry.Key.CreatureId, out ICombatCreature creature))
                {
                    _entries.RemoveAt(i);
                    continue;
                }
                CombatTarget now = CombatTarget.Of(creature);
                entry.Middle = now.Position;
                entry.Top = now.Top;
                _entries[i] = entry;
            }
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

        int Find(TargetKey key)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Key == key)
                    return i;
            }
            return -1;
        }
    }
}
