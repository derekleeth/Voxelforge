using System.Collections.Generic;
using Unity.Mathematics;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Who a support spell lands on (Roadmap 4.6): the partner the caster is aiming at for a Shield, and who's standing in
    /// a Zone. Plain C#, so it's tested without a scene; <see cref="PlayerSupport"/> and <see cref="AbilityZone"/> use it
    /// on the host.
    /// </summary>
    public static class SupportRules
    {
        /// <summary>How far a hero's middle is above their feet, where a shield aims.</summary>
        public const float HeroMiddle = 0.9f;

        /// <summary>
        /// The index in <paramref name="partnerFeet"/> of the partner nearest the caster's aim: their middle within
        /// <paramref name="range"/> metres of <paramref name="eyes"/> and within <paramref name="coneDegrees"/> of
        /// <paramref name="aim"/>. -1 if none is, and the spell goes on the caster.
        /// </summary>
        public static int PickPartner(float3 eyes, float3 aim, IReadOnlyList<float3> partnerFeet, float range, float coneDegrees)
        {
            aim = math.normalizesafe(aim);
            float minDot = math.cos(math.radians(coneDegrees));
            int best = -1;
            float bestDot = minDot;
            for (int i = 0; i < partnerFeet.Count; i++)
            {
                float3 to = partnerFeet[i] + new float3(0f, HeroMiddle, 0f) - eyes;
                float distance = math.length(to);
                if (distance > range || distance < 1e-4f)
                    continue;
                float dot = math.dot(to / distance, aim);
                if (dot >= bestDot)
                {
                    bestDot = dot;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>
        /// Whether a hero with their feet at <paramref name="feet"/> stands in a zone of <paramref name="radius"/> centred
        /// on <paramref name="centre"/>: within the radius across the ground, and no more than a jump below or a little
        /// above it, so heroes on a ledge over the circle don't count.
        /// </summary>
        public static bool InZone(float3 centre, float radius, float3 feet)
        {
            float dy = feet.y - centre.y;
            return dy >= -1.5f && dy <= 3f && math.distancesq(centre.xz, feet.xz) <= radius * radius;
        }
    }

    /// <summary>
    /// When a zone pulses (Roadmap 4.6): once as it's placed, then every <see cref="Interval"/> seconds until it ends, so a
    /// 6 s zone pulsing every 1 s pulses 6 times. Plain C#, tested; <see cref="AbilityZone"/> runs one on the host.
    /// </summary>
    public struct ZoneRules
    {
        public readonly float Start;
        public readonly float Duration;
        public readonly float Interval;
        float _nextPulse;

        public ZoneRules(float start, float duration, float interval)
        {
            Start = start;
            Duration = duration;
            Interval = math.max(interval, 0.05f);
            _nextPulse = start;
        }

        public float EndsAt => Start + Duration;

        public bool IsOver(float now) => now >= EndsAt;

        /// <summary>True when a pulse is due at <paramref name="now"/>; each due pulse is only given once.</summary>
        public bool TryPulse(float now)
        {
            if (now < _nextPulse || now >= EndsAt)
                return false;
            _nextPulse += Interval;
            return true;
        }
    }
}
