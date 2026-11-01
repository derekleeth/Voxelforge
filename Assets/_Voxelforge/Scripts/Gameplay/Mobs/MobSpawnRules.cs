using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// The rules the biome spawner follows (Roadmap 5.2 step D): which mob lives at a spot, how many may be about, where
    /// round a hero to look, and which mobs are too far from everyone to keep. Plain C#, so it's tested without a scene;
    /// <see cref="MobSpawner"/> uses it on the host.
    /// </summary>
    public static class MobSpawnRules
    {
        /// <summary>Whether <paramref name="mob"/> lives at a spot in <paramref name="biome"/>.</summary>
        /// <param name="cave">The spot is on a cave floor, not under the sky.</param>
        /// <param name="aboveSnowLine">The ground there is above the snow line.</param>
        public static bool LivesAt(MobDefinition mob, string biome, bool cave, bool aboveSnowLine)
        {
            if (mob == null || mob.Prefab == null)
                return false;
            MobSpawnRule rule = mob.Spawn;
            if (rule.Place == MobSpawnPlace.Nowhere || rule.Weight <= 0f)
                return false;
            if (rule.Place != (cave ? MobSpawnPlace.Cave : MobSpawnPlace.Surface))
                return false;
            if (!cave && rule.AboveSnowLine && aboveSnowLine)
                return true;
            if (rule.Biomes == null || rule.Biomes.Length == 0)
                return true;
            foreach (string name in rule.Biomes)
            {
                if (!string.IsNullOrEmpty(name) && biome != null && biome.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Picks a mob that lives at the spot, by weight, or null if none does. <paramref name="roll"/> is a random number
        /// from 0 up to 1.
        /// </summary>
        public static MobDefinition Pick(IReadOnlyList<MobDefinition> mobs, string biome, bool cave, bool aboveSnowLine, float roll)
        {
            float total = 0f;
            foreach (MobDefinition mob in mobs)
            {
                if (LivesAt(mob, biome, cave, aboveSnowLine))
                    total += mob.Spawn.Weight;
            }
            if (total <= 0f)
                return null;
            float target = math.clamp(roll, 0f, 0.99999f) * total;
            MobDefinition last = null;
            foreach (MobDefinition mob in mobs)
            {
                if (!LivesAt(mob, biome, cave, aboveSnowLine))
                    continue;
                last = mob;
                target -= mob.Spawn.Weight;
                if (target < 0f)
                    return mob;
            }
            return last;
        }

        /// <summary>
        /// How many to spawn together, from 0 up to 1 <paramref name="roll"/>, without going over <paramref name="room"/>.
        /// 0 if there isn't room for its smallest group: two wolves aren't a pack.
        /// </summary>
        public static int GroupSize(in MobSpawnRule rule, float roll, int room)
        {
            int min = math.max(1, rule.GroupMin);
            int max = math.max(min, rule.GroupMax);
            if (room < min)
                return 0;
            int size = min + (int)math.floor(math.clamp(roll, 0f, 0.99999f) * (max - min + 1));
            return math.min(size, room);
        }

        /// <summary>
        /// How many more mobs may spawn near one hero: the room under their own cap and under the cap for the whole world,
        /// whichever is less.
        /// </summary>
        public static int Room(int nearHero, int perHeroCap, int alive, int totalCap) =>
            math.max(0, math.min(perHeroCap - nearHero, totalCap - alive));

        /// <summary>
        /// A spot in the ring between <paramref name="inner"/> and <paramref name="outer"/> metres round
        /// <paramref name="centre"/>. <paramref name="behind"/> keeps it in the half circle behind
        /// <paramref name="facing"/>, out of the hero's view; otherwise it's anywhere round them.
        /// </summary>
        /// <param name="angleRoll">From 0 up to 1: which way round.</param>
        /// <param name="distanceRoll">From 0 up to 1: how far out.</param>
        public static float2 RingSpot(float2 centre, float2 facing, bool behind, float inner, float outer, float angleRoll,
            float distanceRoll)
        {
            float facingAngle = math.lengthsq(facing) > 0.0001f ? math.atan2(facing.x, facing.y) : 0f;
            float angle = behind
                ? facingAngle + math.PI * 0.5f + angleRoll * math.PI
                : angleRoll * 2f * math.PI;
            // Spread evenly over the ring's area, not bunched at its inside edge.
            float distance = math.sqrt(math.lerp(inner * inner, outer * outer, distanceRoll));
            return centre + new float2(math.sin(angle), math.cos(angle)) * distance;
        }

        /// <summary>Whether <paramref name="spot"/> is closer than <paramref name="radius"/> to any of <paramref name="points"/> (across the ground).</summary>
        public static bool IsNearAny(float3 spot, IReadOnlyList<float3> points, float radius)
        {
            float squared = radius * radius;
            foreach (float3 point in points)
            {
                if (math.distancesq(spot.xz, point.xz) < squared)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Whether a mob at <paramref name="feet"/> should leave the world: more than <paramref name="distance"/> from
        /// every hero, and not in a fight.
        /// </summary>
        public static bool ShouldDespawn(float3 feet, bool fighting, IReadOnlyList<float3> heroes, float distance) =>
            !fighting && !IsNearAny(feet, heroes, distance);
    }
}
