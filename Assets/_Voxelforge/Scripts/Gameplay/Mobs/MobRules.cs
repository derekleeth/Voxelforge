using UnityEngine;
using Voxelforge.Core;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// The rules for one mob's health (Roadmap 5.2): its health, stuns, and who it's angry at (its
    /// <see cref="Threat"/> table, Roadmap 5.3), so it knows who to go after. Plain C#, so it's tested without a scene;
    /// <see cref="Mob"/> runs one per mob on the host, and <see cref="MobBrain"/> reads it.
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

        /// <summary>Who it's angry at, and how angry (Roadmap 5.3). Host only, like the rest.</summary>
        public ThreatTable Threat { get; } = new ThreatTable();

        /// <summary>How many hits it has taken, so the brain can tell when a new one landed.</summary>
        public int HitCount { get; private set; }

        public bool IsStunned(float now) => !IsDefeated && now < StunnedUntil;

        /// <summary>
        /// Takes up to <paramref name="amount"/> damage from <paramref name="attacker"/>, who gains threat for what it
        /// really took, times <paramref name="threatScale"/>. The hit that empties it defeats it
        /// (<see cref="TargetDamage.Toppled"/>).
        /// </summary>
        public TargetDamage Hit(float amount, PlayerId attacker, float threatScale = 1f)
        {
            if (amount <= 0f || IsDefeated)
                return default;
            float dealt = Mathf.Min(amount, Health);
            Health -= dealt;
            HitCount++;
            if (attacker.IsValid)
                LastAttacker = attacker;
            Threat.Add(attacker, ThreatRules.Damage(dealt, threatScale));
            return new TargetDamage(dealt, Health, Max, IsDefeated);
        }

        /// <summary>
        /// Stuns it until <paramref name="now"/> + <paramref name="seconds"/>, unless it's already stunned for longer. The
        /// attacker gains <see cref="ThreatRules.StunThreat"/> times <paramref name="threatScale"/>.
        /// </summary>
        public bool Stun(float now, float seconds, PlayerId attacker, float threatScale = 1f)
        {
            if (seconds <= 0f || IsDefeated)
                return false;
            StunnedUntil = Mathf.Max(StunnedUntil, now + seconds);
            if (attacker.IsValid)
                LastAttacker = attacker;
            Threat.Add(attacker, ThreatRules.StunThreat * threatScale);
            HitCount++;
            return true;
        }

        /// <summary>Back to full health, having got home (a leash), forgetting who hit it and all its threat.</summary>
        public void Reset()
        {
            if (IsDefeated)
                return;
            Health = Max;
            LastAttacker = PlayerId.None;
            Threat.Clear();
        }
    }
}
