using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>What a hit did: how much a shield soaked up and how much health was really lost.</summary>
    public readonly struct DamageResult
    {
        public readonly float Absorbed;
        public readonly float Taken;
        /// <summary>This hit took the hero to 0 and knocked them out (Roadmap 5.2).</summary>
        public readonly bool KnockedOut;

        public DamageResult(float absorbed, float taken, bool knockedOut = false)
        {
            Absorbed = absorbed;
            Taken = taken;
            KnockedOut = knockedOut;
        }
    }

    /// <summary>
    /// The rules for one hero's health (Roadmap 4.6): their health, a shield that soaks up damage first (Bulwark Rune),
    /// and slow regeneration once they've been out of combat for a while. Plain C#, so it's tested without a scene;
    /// <see cref="PlayerHealth"/> runs one per player on the host.
    /// <para>There's no fall damage on purpose. Sparring passes a floor that health won't go under, but a creature's hit
    /// can take a hero to 0 (Roadmap 5.2): they're knocked out for <see cref="KnockOutSeconds"/>, nothing hurts or heals
    /// them, and then they wake (<see cref="Wake"/>) with <see cref="WakeShare"/> of their health.</para>
    /// </summary>
    public sealed class HeroHealth
    {
        /// <summary>Health back per second once out of combat.</summary>
        public const float RegenPerSecond = 2f;

        /// <summary>Seconds without being hurt before health starts coming back.</summary>
        public const float RegenDelay = 5f;

        /// <summary>Seconds a knocked-out hero waits before they wake at home (Roadmap 5.2).</summary>
        public const float KnockOutSeconds = 5f;

        /// <summary>The share of most health a hero wakes up with.</summary>
        public const float WakeShare = 0.5f;

        public HeroHealth(float max)
        {
            Max = Mathf.Max(1f, max);
            Current = Max;
            LastHurtAt = float.NegativeInfinity;
        }

        public float Max { get; private set; }
        public float Current { get; private set; }

        /// <summary>Damage the shield will still soak up. 0 = no shield.</summary>
        public float Shield { get; private set; }

        /// <summary>When the shield fades, even if it soaked nothing.</summary>
        public float ShieldEndsAt { get; private set; }

        /// <summary>When the hero last lost health or shield.</summary>
        public float LastHurtAt { get; private set; }

        public bool IsFull => Current >= Max;

        /// <summary>Health ran out (Roadmap 5.2); the hero lies there until they wake.</summary>
        public bool IsKnockedOut { get; private set; }

        /// <summary>When a knocked-out hero may wake.</summary>
        public float WakeAt { get; private set; }

        /// <summary>A knocked-out hero has waited long enough to wake.</summary>
        public bool ReadyToWake(float now) => IsKnockedOut && now >= WakeAt;

        /// <summary>
        /// Hurts the hero by <paramref name="amount"/> at <paramref name="now"/>. The shield soaks up what it can first;
        /// health never goes under <paramref name="floor"/> (or what it already is, if that's lower). A hit that takes it to
        /// 0 knocks the hero out; a knocked-out hero takes nothing more.
        /// </summary>
        public DamageResult Damage(float amount, float now, float floor = 0f)
        {
            if (amount <= 0f || IsKnockedOut)
                return default;
            float absorbed = Mathf.Min(Shield, amount);
            Shield -= absorbed;
            float lowest = Mathf.Min(Current, Mathf.Max(0f, floor));
            float taken = Mathf.Min(amount - absorbed, Current - lowest);
            Current -= taken;
            if (absorbed > 0f || taken > 0f)
                LastHurtAt = now;
            if (taken > 0f && Current <= 0f)
            {
                Current = 0f;
                Shield = 0f;
                IsKnockedOut = true;
                WakeAt = now + KnockOutSeconds;
                return new DamageResult(absorbed, taken, knockedOut: true);
            }
            return new DamageResult(absorbed, taken);
        }

        /// <summary>Wakes a knocked-out hero with <see cref="WakeShare"/> of their health. Does nothing to anyone else.</summary>
        public void Wake()
        {
            if (!IsKnockedOut)
                return;
            IsKnockedOut = false;
            Current = Max * WakeShare;
        }

        /// <summary>
        /// Heals up to <paramref name="amount"/>, never over <see cref="Max"/>. Returns how much it really healed. A
        /// knocked-out hero can't be healed: they wake at home instead.
        /// </summary>
        public float Heal(float amount)
        {
            if (amount <= 0f || IsKnockedOut)
                return 0f;
            float healed = Mathf.Min(amount, Max - Current);
            Current += healed;
            return healed;
        }

        /// <summary>Gives the hero a shield of <paramref name="amount"/> until <paramref name="until"/>, replacing any shield they had.</summary>
        public void SetShield(float amount, float until)
        {
            Shield = Mathf.Max(0f, amount);
            ShieldEndsAt = until;
        }

        /// <summary>
        /// Changes the most health the hero can have (a class, 4.6). They keep the same share of it, so changing class
        /// never looks like being hurt or healed.
        /// </summary>
        public void SetMax(float max)
        {
            float share = Current / Max;
            Max = Mathf.Max(1f, max);
            Current = Max * share;
        }

        public void Refill()
        {
            if (!IsKnockedOut)
                Current = Max;
        }

        /// <summary>Fades an old shield and regenerates health over <paramref name="dt"/> seconds once out of combat.</summary>
        public void Tick(float now, float dt)
        {
            if (Shield > 0f && now >= ShieldEndsAt)
                Shield = 0f;
            if (!IsKnockedOut && now - LastHurtAt >= RegenDelay)
                Current = Mathf.Min(Max, Current + RegenPerSecond * Mathf.Max(0f, dt));
        }
    }
}
