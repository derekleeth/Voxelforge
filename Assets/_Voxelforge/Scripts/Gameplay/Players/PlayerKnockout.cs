using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Wakes a knocked-out hero at home (Roadmap 5.2). When a creature takes a hero's health to 0
    /// (<see cref="PlayerHealth.IsKnockedOut"/>), they lie still for <see cref="HeroHealth.KnockOutSeconds"/>; then
    /// <b>the host</b> puts them at their own Home Totem, or at the world's spawn if they have none (or it was broken), with
    /// <see cref="HeroHealth.WakeShare"/> of their health. Nothing is lost.
    /// <para>Like hearthing (<see cref="PlayerHearth"/>), the chunk loader streams in the terrain where they'll wake while
    /// they wait (<see cref="IChunkViewerSource"/>), so a far-away home is ready in time. Every PC shows the countdown
    /// in that player's own view from <see cref="WakeAtLocal"/>.</para>
    /// </summary>
    [RequireComponent(typeof(PlayerController), typeof(PlayerHealth))]
    public sealed class PlayerKnockout : NetworkBehaviour, IChunkViewerSource
    {
        [Tooltip("Extra seconds the host waits for a far-away Home Totem's terrain before waking the hero at the world spawn instead.")]
        [SerializeField, Range(1f, 30f)] float homeWaitSeconds = 10f;

        PlayerController _player;
        PlayerHealth _health;
        PlayerHearth _hearth;

        // Host state.
        bool _out;
        bool _toHome;
        int3 _homeBlock;

        /// <summary>Every PC: when this PC expects the hero to wake (<see cref="Time.time"/>), for the countdown.</summary>
        public float WakeAtLocal { get; private set; } = float.NegativeInfinity;

        /// <summary>Every PC: the hero has a Home Totem to wake at (otherwise the world's spawn).</summary>
        public bool WakesAtHome => _hearth != null && _hearth.HasHome;

        void Awake()
        {
            _player = GetComponent<PlayerController>();
            _health = GetComponent<PlayerHealth>();
            _hearth = GetComponent<PlayerHearth>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _health.Changed += OnHealthEvent;
        }

        public override void OnNetworkDespawn()
        {
            _health.Changed -= OnHealthEvent;
            _out = false;
            base.OnNetworkDespawn();
        }

        void OnHealthEvent(HealthEvent e)
        {
            if (e.Kind == HealthEventKind.KnockedOut)
                WakeAtLocal = Time.time + e.Amount;
            else if (e.Kind == HealthEventKind.Woke)
                WakeAtLocal = float.NegativeInfinity;
        }

        /// <summary>The chunk loader streams in where this hero will wake while they're knocked out.</summary>
        public bool TryGetViewerPosition(out float3 position)
        {
            position = _toHome ? (float3)_homeBlock + 0.5f : _player.WorldSpawnViewer;
            return IsServer && IsSpawned && _out;
        }

        void FixedUpdate()
        {
            if (!IsServer || !IsSpawned)
                return;
            if (!_health.IsKnockedOut)
            {
                _out = false;
                return;
            }
            if (!_out)
            {
                // Just knocked out: choose where they'll wake, so its terrain starts loading now.
                _out = true;
                _toHome = _hearth != null && _hearth.HasHome;
                _homeBlock = _hearth != null ? _hearth.ServerHomeBlock : default;
                return;
            }
            if (!_health.ServerReadyToWake)
                return;

            VoxelWorldManager world = _player.WorldManager;
            if (world == null || world.World == null)
                return;
            float3 feet;
            float yaw = transform.eulerAngles.y;
            if (_toHome)
            {
                if (!HomeTotems.IsStanding(world.World, world.DeltaLog, _homeBlock))
                {
                    _toHome = false; // Someone broke it: the world's spawn it is.
                    return;
                }
                if (!HomeTotems.IsReady(world.World, _homeBlock))
                {
                    if (Time.time - _health.ServerWakeAt > homeWaitSeconds)
                        _toHome = false;
                    return; // Still streaming in.
                }
                if (!HomeTotems.TryFindLanding(world.World, _homeBlock, _player.Motor.HalfExtents, out feet, out yaw))
                {
                    _toHome = false;
                    return;
                }
            }
            else if (!_player.TryFindWorldSpawn(out feet))
            {
                return; // Still streaming in.
            }

            _player.ServerTeleport(feet, yaw);
            _health.ServerWake();
            _out = false;
        }
    }
}
