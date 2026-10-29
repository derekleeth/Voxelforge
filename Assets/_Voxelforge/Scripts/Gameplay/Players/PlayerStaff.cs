using System;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// This player's projectiles (Roadmap 3.6, 4.1): the M.G.R.S.'s Mana Bolts and any other projectile ability.
    /// <see cref="PlayerCaster"/> decides <i>when</i> one goes off; then <b>the host</b> launches it from the hero's eyes
    /// along their aim (<see cref="PlayerController.Aim"/>), moves it (<see cref="ManaBoltFlight"/>), and damages the block
    /// it hits through <see cref="VoxelWorldManager.RequestDamageVoxel"/> in this player's name. No position ever comes
    /// from a PC (CLAUDE.md Laws #1-#3).
    /// <para>Every PC hears when a bolt starts and ends, and draws it (<see cref="ManaBoltView"/>).</para>
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerStaff : NetworkBehaviour
    {
        [Tooltip("Log every bolt that hits, on the host.")]
        [SerializeField] bool logHits;

        readonly ManaBoltFlight _flight = new ManaBoltFlight();
        readonly List<ManaBoltEnd> _ended = new List<ManaBoltEnd>();

        PlayerController _player;
        PlayerCaster _caster;

        /// <summary>Every PC: a bolt left the hero (start, direction, speed and range), and the ability it is.</summary>
        public event Action<ManaBolt, AbilityDefinition> Fired;

        /// <summary>Every PC: a bolt hit a block or fizzled. The block ID is what it hit, as it was before the hit.</summary>
        public event Action<ManaBoltEnd, ushort> Ended;

        /// <summary>Every PC: <see cref="Time.time"/> when this PC last heard of a shot, for the recoil.</summary>
        public float LastShotTime { get; private set; } = float.NegativeInfinity;

        void Awake()
        {
            _player = GetComponent<PlayerController>();
            _caster = GetComponent<PlayerCaster>();
        }

        public override void OnNetworkDespawn()
        {
            _flight.Clear();
            base.OnNetworkDespawn();
        }

        /// <summary>Host only: launches a bolt of <paramref name="ability"/> (a finished cast, <see cref="PlayerCaster"/>).</summary>
        public void ServerLaunch(AbilityDefinition ability, float3 origin, float3 direction)
        {
            if (IsServer && IsSpawned && _flight.TryLaunch(origin, direction, ability, out ManaBolt bolt))
                BoltFiredRpc(bolt.Id, bolt.Position, bolt.Direction, bolt.AbilityId);
        }

        void FixedUpdate()
        {
            if (!IsServer || !IsSpawned || _flight.Count == 0)
                return;
            VoxelWorldManager world = _player.WorldManager;
            _ended.Clear();
            _flight.Step(world != null ? world.World : null, Time.fixedDeltaTime, _ended);
            foreach (ManaBoltEnd end in _ended)
                Strike(world, end);
        }

        void Strike(VoxelWorldManager world, ManaBoltEnd end)
        {
            ushort blockId = BlockIds.Air;
            if (end.HitBlock && world != null && world.World != null)
            {
                blockId = world.World.GetVoxel(end.Hit.Block).BlockId;
                VoxelDamageResult result = world.RequestDamageVoxel(end.Hit.Block, end.Damage, _player.PlayerId);
                if (logHits)
                    Debug.Log($"[Voxelforge] {_player.PlayerId}'s bolt hit {end.Hit.Block}: {result}.", this);
            }
            BoltEndedRpc(end.Id, end.Point, end.Normal, end.HitBlock, blockId);
        }

        [Rpc(SendTo.ClientsAndHost)]
        void BoltFiredRpc(ushort boltId, Vector3 origin, Vector3 direction, ushort abilityId)
        {
            AbilityBook book = _caster != null ? _caster.Book : null;
            if (book == null || !book.TryGet(abilityId, out AbilityDefinition ability))
                return;
            LastShotTime = Time.time;
            Fired?.Invoke(new ManaBolt
            {
                Id = boltId, AbilityId = abilityId, Position = origin, Direction = direction, Speed = ability.ProjectileSpeed,
                Remaining = ability.Range, Damage = ability.BlockDamage,
            }, ability);
        }

        [Rpc(SendTo.ClientsAndHost)]
        void BoltEndedRpc(ushort boltId, Vector3 point, Vector3 normal, bool hitBlock, ushort blockId) =>
            Ended?.Invoke(new ManaBoltEnd { Id = boltId, HitBlock = hitBlock, Point = point, Normal = normal }, blockId);
    }
}
