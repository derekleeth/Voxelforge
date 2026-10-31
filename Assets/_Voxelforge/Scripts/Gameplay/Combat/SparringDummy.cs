using UnityEngine;
using Voxelforge.Core;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// The rules for one Training Dummy (Roadmap 4.6): its health, who hit it last, and when it throws a hay bale back.
    /// Plain C#, so it's tested without a scene; <see cref="TrainingDummies"/> keeps one per dummy block on the host.
    /// <para>A dummy can't be destroyed by spells: emptying its health topples it and it springs back to full. Left
    /// alone for <see cref="ResetAfter"/> seconds it stops sparring and is forgotten (full health next time). A stun
    /// (Roadmap 4.7, Hoofquake) stops it sparring until it wears off.</para>
    /// </summary>
    public sealed class SparringDummy
    {
        public const float MaxHealth = 500f;

        /// <summary>Seconds without a hit before the dummy heals up and calms down.</summary>
        public const float ResetAfter = 10f;

        /// <summary>Seconds between hay bales while it's sparring.</summary>
        public const float SparEvery = 3f;

        public SparringDummy(float now)
        {
            Health = MaxHealth;
            LastHitAt = now;
            NextSparAt = now + SparEvery;
        }

        public float Health { get; private set; }

        /// <summary>Who hit it last: who the next hay bale is for.</summary>
        public PlayerId LastAttacker { get; private set; }

        public float LastHitAt { get; private set; }

        public float NextSparAt { get; private set; }

        /// <summary>It throws nothing until then (Roadmap 4.7).</summary>
        public float StunnedUntil { get; private set; } = float.NegativeInfinity;

        public bool IsStunned(float now) => now < StunnedUntil;

        /// <summary>
        /// Stuns it for <paramref name="seconds"/> from <paramref name="now"/>, or longer if it's already stunned for longer.
        /// Its next hay bale waits until a moment after the stun ends. A stun counts as being hit, so it keeps it sparring.
        /// </summary>
        public void Stun(float now, float seconds, PlayerId attacker)
        {
            if (seconds <= 0f)
                return;
            StunnedUntil = Mathf.Max(StunnedUntil, now + seconds);
            NextSparAt = Mathf.Max(NextSparAt, StunnedUntil + 0.5f);
            LastHitAt = now;
            if (attacker.IsValid)
                LastAttacker = attacker;
        }

        /// <summary>No one has hit it for <see cref="ResetAfter"/> seconds; it can be forgotten.</summary>
        public bool IsIdle(float now) => now - LastHitAt >= ResetAfter;

        /// <summary>
        /// Takes up to <paramref name="amount"/> damage from <paramref name="attacker"/>. Returns what it really took.
        /// A hit that empties it <paramref name="toppled"/> it, and it springs straight back to full health.
        /// </summary>
        public float Hit(float amount, PlayerId attacker, float now, out bool toppled)
        {
            toppled = false;
            if (amount <= 0f)
                return 0f;
            // The first hit after a rest gives the attacker a moment before the first bale comes back.
            if (IsIdle(now))
                NextSparAt = now + SparEvery;
            LastHitAt = now;
            LastAttacker = attacker;
            float taken = Mathf.Min(amount, Health);
            Health -= taken;
            if (Health <= 0f)
            {
                toppled = true;
                Health = MaxHealth;
            }
            return taken;
        }

        /// <summary>
        /// True if it's time to throw a hay bale at <paramref name="target"/>: someone hit it, it isn't idle yet and it
        /// isn't stunned.
        /// Each call that returns true waits <see cref="SparEvery"/> for the next one.
        /// </summary>
        public bool TrySpar(float now, out PlayerId target)
        {
            target = LastAttacker;
            if (!LastAttacker.IsValid || IsIdle(now) || IsStunned(now) || now < NextSparAt)
                return false;
            NextSparAt = now + SparEvery;
            return true;
        }
    }
}
