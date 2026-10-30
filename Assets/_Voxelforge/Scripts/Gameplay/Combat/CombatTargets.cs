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
        /// <summary>The Training Dummy's block.</summary>
        public readonly int3 Block;

        public CombatTarget(int3 block) => Block = block;

        /// <summary>Its middle, where spells aim and numbers float from.</summary>
        public float3 Position => (float3)Block + new float3(0.5f, 0.9f, 0.5f);
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

        /// <summary>Host only: hurts <paramref name="target"/> in <paramref name="instigator"/>'s name. Returns the damage it took.</summary>
        public float ServerDamage(in CombatTarget target, float amount, PlayerId instigator)
        {
            if (!IsServer || amount <= 0f || !IsTarget(target.Block))
                return 0f;
            float now = Time.time;
            if (!_dummies.TryGetValue(target.Block, out SparringDummy dummy))
                _dummies[target.Block] = dummy = new SparringDummy(now);
            return dummy.Hit(amount, instigator, now, out _);
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
