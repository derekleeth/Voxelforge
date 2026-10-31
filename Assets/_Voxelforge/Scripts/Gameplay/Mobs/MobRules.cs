using UnityEngine;
using Voxelforge.Core;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// The rules for one mob's health (Roadmap 5.2): its health, stuns, and who hit it last, so it knows who to go after.
    /// Plain C#, so it's tested without a scene; <see cref="Mob"/> runs one per mob on the host, and
    /// <see cref="MobBrain"/> reads it. Roadmap 5.3 adds a real threat table; until then the last attacker is enough.
    /// </summary>
    public sealed class MobRules
    {
        public MobRules(float max)
        {
            Max = Mathf.Max(1f, max);
            Health = Max;
        }

        public float Max { get; }
        public float Health { get; private set; }
        public float Share => Health / Max;

        /// <summary>Its health ran out. It stays defeated.</summary>
        public bool IsDefeated => Health <= 0f;

        /// <summary>It neither moves nor fights until then (Roadmap 4.7, Hoofquake).</summary>
        public float StunnedUntil { get; private set; } = float.NegativeInfinity;

        /// <summary>Who hit it last.</summary>
        public PlayerId LastAttacker { get; private set; }

        /// <summary>How many hits it has taken, so the brain can tell when a new one landed.</summary>
        public int HitCount { get; private set; }

        public bool IsStunned(float now) => !IsDefeated && now < StunnedUntil;

        /// <summary>
        /// Takes up to <paramref name="amount"/> damage from <paramref name="attacker"/>. The hit that empties it defeats it
        /// (<see cref="TargetDamage.Toppled"/>).
        /// </summary>
        public TargetDamage Hit(float amount, PlayerId attacker)
        {
            if (amount <= 0f || IsDefeated)
                return default;
            float dealt = Mathf.Min(amount, Health);
            Health -= dealt;
            HitCount++;
            if (attacker.IsValid)
                LastAttacker = attacker;
            return new TargetDamage(dealt, Health, Max, IsDefeated);
        }

        /// <summary>Stuns it until <paramref name="now"/> + <paramref name="seconds"/>, unless it's already stunned for longer.</summary>
        public bool Stun(float now, float seconds, PlayerId attacker)
        {
            if (seconds <= 0f || IsDefeated)
                return false;
            StunnedUntil = Mathf.Max(StunnedUntil, now + seconds);
            if (attacker.IsValid)
                LastAttacker = attacker;
            HitCount++;
            return true;
        }

        /// <summary>Back to full health, having got home (a leash), forgetting who hit it.</summary>
        public void Reset()
        {
            if (IsDefeated)
                return;
            Health = Max;
            LastAttacker = PlayerId.None;
        }
    }
}
