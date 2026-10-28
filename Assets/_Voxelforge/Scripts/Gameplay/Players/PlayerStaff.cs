using System;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Kian's M.G.R.S. (Magic Gun Rifle Staff, Roadmap 3.6). While this player holds a ranged item and Mine,
    /// <b>the host</b> launches a Mana Bolt from the hero's eyes along their aim (<see cref="PlayerController.Aim"/>),
    /// moves it (<see cref="ManaBoltFlight"/>), and damages the block it hits through
    /// <see cref="VoxelWorldManager.RequestDamageVoxel"/> in this player's name. The held item is the one the host agreed
    /// is in the bag (<see cref="PlayerHands"/>), and no position ever comes from a PC (CLAUDE.md Laws #1-#3).
    /// <para>Every PC hears when a bolt starts and ends, and draws it (<see cref="ManaBoltView"/>).</para>
    /// </summary>
    [RequireComponent(typeof(PlayerController), typeof(PlayerHands))]
    public sealed class PlayerStaff : NetworkBehaviour
    {
        [Tooltip("Log every bolt that hits, on the host.")]
        [SerializeField] bool logHits;

        readonly ManaBoltFlight _flight = new ManaBoltFlight();
        readonly List<ManaBoltEnd> _ended = new List<ManaBoltEnd>();

        PlayerController _player;
        PlayerHands _hands;

        /// <summary>Every PC: a bolt left the staff (start, direction, speed and range).</summary>
        public event Action<ManaBolt, ProjectileStats> Fired;

        /// <summary>Every PC: a bolt hit a block or fizzled. The block ID is what it hit, as it was before the hit.</summary>
        public event Action<ManaBoltEnd, ushort> Ended;

        /// <summary>Every PC: <see cref="Time.time"/> when this PC last heard of a shot, for the recoil.</summary>
        public float LastShotTime { get; private set; } = float.NegativeInfinity;

        void Awake()
        {
            _player = GetComponent<PlayerController>();
            _hands = GetComponent<PlayerHands>();
        }

        public override void OnNetworkDespawn()
        {
            _flight.Clear();
            base.OnNetworkDespawn();
        }

        void FixedUpdate()
        {
            if (!IsServer || !IsSpawned)
                return;

            PlayerInputFrame input = _player.ServerInput;
            if (_player.HasLanded && input.IsPressed(PlayerButtons.Mine) && _hands.TryGetHeldProjectile(out ProjectileStats stats) &&
                _flight.TryLaunch(Time.time, _player.Aim.Eyes(transform.position), _player.Aim.Direction(input), stats, out ManaBolt bolt))
            {
                BoltFiredRpc(bolt.Id, bolt.Position, bolt.Direction, _hands.HeldItem);
            }

            if (_flight.Count == 0)
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
                    Debug.Log($"[Voxelforge] {_player.PlayerId}'s Mana Bolt hit {end.Hit.Block}: {result}.", this);
            }
            BoltEndedRpc(end.Id, end.Point, end.Normal, end.HitBlock, blockId);
        }

        [Rpc(SendTo.ClientsAndHost)]
        void BoltFiredRpc(ushort boltId, Vector3 origin, Vector3 direction, ushort itemId)
        {
            if (_hands.Items == null || !_hands.Items.TryGetProjectile(itemId, out ProjectileStats stats))
                return;
            LastShotTime = Time.time;
            Fired?.Invoke(new ManaBolt
            {
                Id = boltId, Position = origin, Direction = direction, Speed = stats.speed, Remaining = stats.range, Damage = stats.damage,
            }, stats);
        }

        [Rpc(SendTo.ClientsAndHost)]
        void BoltEndedRpc(ushort boltId, Vector3 point, Vector3 normal, bool hitBlock, ushort blockId) =>
            Ended?.Invoke(new ManaBoltEnd { Id = boltId, HitBlock = hitBlock, Point = point, Normal = normal }, blockId);
    }
}
