using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>What a hit did: how much a shield soaked up and how much health was really lost.</summary>
    public readonly struct DamageResult
    {
        public readonly float Absorbed;
        public readonly float Taken;

        public DamageResult(float absorbed, float taken)
        {
            Absorbed = absorbed;
            Taken = taken;
        }
    }

    /// <summary>
    /// The rules for one hero's health (Roadmap 4.6): their health, a shield that soaks up damage first (Bulwark Rune),
    /// and slow regeneration once they've been out of combat for a while. Plain C#, so it's tested without a scene;
    /// <see cref="PlayerHealth"/> runs one per player on the host.
    /// <para>There's no fall damage on purpose, and nothing in 4.6 can knock a hero out: every hit passes a floor
    /// that health won't go under. Knock-outs come with Phase 5 monsters.</para>
    /// </summary>
    public sealed class HeroHealth
    {
        /// <summary>Health back per second once out of combat.</summary>
        public const float RegenPerSecond = 2f;

        /// <summary>Seconds without being hurt before health starts coming back.</summary>
        public const float RegenDelay = 5f;

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

        /// <summary>
        /// Hurts the hero by <paramref name="amount"/> at <paramref name="now"/>. The shield soaks up what it can first;
        /// health never goes under <paramref name="floor"/> (or what it already is, if that's lower).
        /// </summary>
        public DamageResult Damage(float amount, float now, float floor = 0f)
        {
            if (amount <= 0f)
                return default;
            float absorbed = Mathf.Min(Shield, amount);
            Shield -= absorbed;
            float lowest = Mathf.Min(Current, Mathf.Max(0f, floor));
            float taken = Mathf.Min(amount - absorbed, Current - lowest);
            Current -= taken;
            if (absorbed > 0f || taken > 0f)
                LastHurtAt = now;
            return new DamageResult(absorbed, taken);
        }

        /// <summary>Heals up to <paramref name="amount"/>, never over <see cref="Max"/>. Returns how much it really healed.</summary>
        public float Heal(float amount)
        {
            if (amount <= 0f)
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

        public void Refill() => Current = Max;

        /// <summary>Fades an old shield and regenerates health over <paramref name="dt"/> seconds once out of combat.</summary>
        public void Tick(float now, float dt)
        {
            if (Shield > 0f && now >= ShieldEndsAt)
                Shield = 0f;
            if (now - LastHurtAt >= RegenDelay)
                Current = Mathf.Min(Max, Current + RegenPerSecond * Mathf.Max(0f, dt));
        }
    }
}
