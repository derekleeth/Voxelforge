using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using Voxelforge.Core;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>Something spells can hurt (Roadmap 4.6). In 4.6 that's a Training Dummy block; Phase 5 adds creatures.</summary>
    public readonly struct CombatTarget
    {
        /// <summary>
        /// How tall a dummy's body is, in metres. The model stands taller than its block, so spells hit this whole box,
        /// not just the block (Roadmap 4.6 play-test: aiming at its chest flew over the block).
        /// </summary>
        public const float BodyHeight = 1.8f;

        /// <summary>The Training Dummy's block.</summary>
        public readonly int3 Block;

        public CombatTarget(int3 block) => Block = block;

        /// <summary>Its middle, where spells aim and numbers float from.</summary>
        public float3 Position => (float3)Block + new float3(0.5f, BodyHeight * 0.5f, 0.5f);

        /// <summary>Just over its head, where its health bar floats.</summary>
        public float3 Top => (float3)Block + new float3(0.5f, BodyHeight + 0.35f, 0.5f);

        /// <summary>
        /// Where a ray from <paramref name="origin"/> along the unit <paramref name="direction"/> first enters the body of
        /// a target standing in <paramref name="block"/>, within <paramref name="maxDistance"/>. A ray starting inside the
        /// body hits at once with a zero <paramref name="normal"/>.
        /// </summary>
        public static bool RayHitsBody(int3 block, float3 origin, float3 direction, float maxDistance, out float distance,
            out int3 normal)
        {
            float3 lo = block, hi = (float3)block + new float3(1f, BodyHeight, 1f);
            distance = 0f;
            normal = int3.zero;
            float enter = 0f, exit = maxDistance;
            for (int axis = 0; axis < 3; axis++)
            {
                if (math.abs(direction[axis]) < 1e-8f)
                {
                    if (origin[axis] < lo[axis] || origin[axis] > hi[axis])
                        return false;
                    continue;
                }
                float inv = 1f / direction[axis];
                float near = (lo[axis] - origin[axis]) * inv, far = (hi[axis] - origin[axis]) * inv;
                int side = -1;
                if (near > far)
                {
                    (near, far) = (far, near);
                    side = 1;
                }
                if (near > enter)
                {
                    enter = near;
                    normal = int3.zero;
                    normal[axis] = side;
                }
                exit = math.min(exit, far);
                if (enter > exit)
                    return false;
            }
            distance = enter;
            return true;
        }
    }

    /// <summary>What a hit did to a target: the damage it took and the health it has left.</summary>
    public readonly struct TargetDamage
    {
        public readonly float Dealt;
        public readonly float Health;
        public readonly float MaxHealth;
        /// <summary>The hit emptied it: a dummy topples and springs back to full.</summary>
        public readonly bool Toppled;

        public TargetDamage(float dealt, float health, float maxHealth, bool toppled)
        {
            Dealt = dealt;
            Health = health;
            MaxHealth = maxHealth;
            Toppled = toppled;
        }
    }

    /// <summary>
    /// Everything spells can hurt (Roadmap 4.6), on <b>the host</b>. Spells and totems ask here for targets near a point
    /// and hurt them here, so Phase 5's creatures only have to join this list. For now the only targets are Training
    /// Dummies: blocks that spells hurt instead of breaking (<see cref="SparringDummy"/>) and that throw hay bales back
    /// at whoever hit them, so heals and shields have something to undo. Hay bales never take a hero under
    /// <see cref="PlayerHealth.SparringFloor"/>.
    /// <b>Not a singleton:</b> systems get it through <see cref="PlayerRegistry.Targets"/> (CLAUDE.md Law #1).
    /// </summary>
    public sealed class CombatTargets : MonoBehaviour
    {
        [SerializeField] NetworkManager networkManager;

        [SerializeField] VoxelWorldManager world;

        [Tooltip("Who the dummies throw hay bales at.")]
        [SerializeField] PlayerRegistry players;

        [Header("Sparring")]
        [Tooltip("Damage of one hay bale.")]
        [SerializeField, Min(0f)] float baleDamage = 8f;

        [Tooltip("Seconds a hay bale flies before it lands.")]
        [SerializeField, Range(0.1f, 2f)] float baleFlight = 0.6f;

        [Tooltip("A dummy only throws at someone this close, in metres.")]
        [SerializeField, Min(1f)] float sparRange = 20f;

        // A bale lands on whoever it was thrown at, wherever they've gone.
        struct Bale
        {
            public PlayerId Target;
            public float LandsAt;
            public float3 From;
        }

        readonly Dictionary<int3, SparringDummy> _dummies = new Dictionary<int3, SparringDummy>();
        readonly List<int3> _forget = new List<int3>();
        readonly List<Bale> _bales = new List<Bale>();

        bool IsServer => networkManager == null || networkManager.IsServer || !networkManager.IsListening;

        VoxelWorld Blocks => world != null ? world.World : null;

        /// <summary>Host only: is there a target standing in <paramref name="block"/>?</summary>
        public bool IsTarget(int3 block)
        {
            VoxelWorld blocks = Blocks;
            return blocks != null && blocks.GetVoxel(block).BlockId == BlockIds.TrainingDummy;
        }

        /// <summary>
        /// Host only: the first target body a ray hits within <paramref name="maxDistance"/>, reported like a block hit
        /// on the target's own block, so flights and slams treat it like any other hit (<see cref="IsTarget"/>).
        /// </summary>
        public bool Raycast(float3 origin, float3 direction, float maxDistance, out VoxelHit hit)
        {
            hit = default;
            VoxelWorld blocks = Blocks;
            if (blocks == null || maxDistance <= 0f)
                return false;
            direction = math.normalizesafe(direction);
            float3 end = origin + direction * maxDistance;
            // A body reaches above its block, so look a little lower than the ray for blocks whose body it might cross.
            int3 lo = (int3)math.floor(math.min(origin, end) - new float3(0f, CombatTarget.BodyHeight - 1f, 0f));
            int3 hi = (int3)math.floor(math.max(origin, end));
            float best = float.MaxValue;
            for (int y = lo.y; y <= hi.y; y++)
            for (int z = lo.z; z <= hi.z; z++)
            for (int x = lo.x; x <= hi.x; x++)
            {
                var block = new int3(x, y, z);
                if (blocks.GetVoxel(block).BlockId != BlockIds.TrainingDummy ||
                    !CombatTarget.RayHitsBody(block, origin, direction, maxDistance, out float distance, out int3 normal) ||
                    distance >= best)
                    continue;
                best = distance;
                hit = new VoxelHit { Block = block, Normal = normal, Distance = distance, Point = origin + direction * distance };
            }
            return best < float.MaxValue;
        }

        /// <summary>Host only: the target nearest <paramref name="point"/> within <paramref name="radius"/> metres.</summary>
        public bool TryFindNearest(float3 point, float radius, out CombatTarget nearest)
        {
            nearest = default;
            float best = float.MaxValue;
            VoxelWorld blocks = Blocks;
            if (blocks == null || radius <= 0f)
                return false;
            int3 lo = (int3)math.floor(point - radius), hi = (int3)math.floor(point + radius);
            for (int y = lo.y; y <= hi.y; y++)
            for (int z = lo.z; z <= hi.z; z++)
            for (int x = lo.x; x <= hi.x; x++)
            {
                var block = new int3(x, y, z);
                if (blocks.GetVoxel(block).BlockId != BlockIds.TrainingDummy)
                    continue;
                float distance = math.distancesq(new CombatTarget(block).Position, point);
                if (distance <= radius * radius && distance < best)
                {
                    best = distance;
                    nearest = new CombatTarget(block);
                }
            }
            return best < float.MaxValue;
        }

        /// <summary>Host only: every target within <paramref name="radius"/> metres of <paramref name="point"/>, added to <paramref name="found"/>.</summary>
        public void FindAll(float3 point, float radius, List<CombatTarget> found)
        {
            VoxelWorld blocks = Blocks;
            if (blocks == null || radius <= 0f)
                return;
            int3 lo = (int3)math.floor(point - radius), hi = (int3)math.floor(point + radius);
            for (int y = lo.y; y <= hi.y; y++)
            for (int z = lo.z; z <= hi.z; z++)
            for (int x = lo.x; x <= hi.x; x++)
            {
                var block = new int3(x, y, z);
                if (blocks.GetVoxel(block).BlockId == BlockIds.TrainingDummy &&
                    math.distancesq(new CombatTarget(block).Position, point) <= radius * radius)
                    found.Add(new CombatTarget(block));
            }
        }

        /// <summary>Host only: hurts <paramref name="target"/> in <paramref name="instigator"/>'s name.</summary>
        public TargetDamage ServerDamage(in CombatTarget target, float amount, PlayerId instigator)
        {
            if (!IsServer || amount <= 0f || !IsTarget(target.Block))
                return default;
            float now = Time.time;
            if (!_dummies.TryGetValue(target.Block, out SparringDummy dummy))
                _dummies[target.Block] = dummy = new SparringDummy(now);
            float dealt = dummy.Hit(amount, instigator, now, out bool toppled);
            return new TargetDamage(dealt, dummy.Health, SparringDummy.MaxHealth, toppled);
        }

        void FixedUpdate()
        {
            if (!IsServer || (_dummies.Count == 0 && _bales.Count == 0))
                return;
            float now = Time.time;

            _forget.Clear();
            foreach (KeyValuePair<int3, SparringDummy> pair in _dummies)
            {
                if (pair.Value.IsIdle(now) || !IsTarget(pair.Key))
                {
                    _forget.Add(pair.Key);
                    continue;
                }
                if (pair.Value.TrySpar(now, out PlayerId target))
                    Throw(new CombatTarget(pair.Key).Position, target, now);
            }
            foreach (int3 block in _forget)
                _dummies.Remove(block);

            for (int i = _bales.Count - 1; i >= 0; i--)
            {
                Bale bale = _bales[i];
                if (now < bale.LandsAt)
                    continue;
                _bales.RemoveAt(i);
                if (TryGetHealth(bale.Target, out PlayerHealth health))
                    health.ServerDamage(baleDamage, PlayerId.None, (Vector3)bale.From);
            }
        }

        void Throw(float3 from, PlayerId target, float now)
        {
            if (!TryGetHealth(target, out PlayerHealth health) ||
                math.distance((float3)health.transform.position, from) > sparRange)
                return;
            health.ServerIncoming((Vector3)from, baleFlight);
            _bales.Add(new Bale { Target = target, LandsAt = now + baleFlight, From = from });
        }

        bool TryGetHealth(PlayerId player, out PlayerHealth health)
        {
            health = null;
            return players != null && players.TryGet(player, out PlayerRecord record) && record.Controller != null &&
                   record.Controller.TryGetComponent(out health);
        }
    }
}
