using System;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using Voxelforge.Core;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// A circle a spell leaves on the ground for a while (Roadmap 4.6): Hallowed Forge, and the Shaman's totems in step E.
    /// <b>The host</b> spawns it (<see cref="PlayerSupport"/>), pulses it (<see cref="ZoneRules"/>) and despawns it when its
    /// time is up; each pulse heals the heroes standing in it (<see cref="SupportRules.InZone"/>) in its caster's name.
    /// Every PC sees which spell it is, where and since when through network variables, and hears each pulse, so
    /// <see cref="AbilityZoneView"/> can draw it from the spell's look. A server-owned entity: no PC can move or end it.
    /// </summary>
    public sealed class AbilityZone : NetworkBehaviour
    {
        [Tooltip("Data/Abilities/AbilityBook.asset: the zone's spell, by ID, on every PC.")]
        [SerializeField] AbilityBook abilities;

        readonly NetworkVariable<ushort> _abilityId = new NetworkVariable<ushort>();
        readonly NetworkVariable<byte> _caster = new NetworkVariable<byte>();
        readonly NetworkVariable<double> _startTime = new NetworkVariable<double>();

        // Host state.
        ZoneRules _rules;
        PlayerRegistry _players;
        AbilityDefinition _ability;
        PlayerId _casterId;
        bool _running;

        /// <summary>Every PC: the spell that made this zone, or null.</summary>
        public AbilityDefinition Ability =>
            abilities != null && abilities.TryGet(_abilityId.Value, out AbilityDefinition ability) ? ability : null;

        /// <summary>Every PC: who cast it.</summary>
        public PlayerId Caster => new PlayerId(_caster.Value);

        /// <summary>Every PC: seconds since it was placed, on the network's shared clock.</summary>
        public float Age => IsSpawned ? (float)(NetworkManager.ServerTime.Time - _startTime.Value) : 0f;

        /// <summary>Every PC: a zone was placed; views listen for it.</summary>
        public static event Action<AbilityZone> Spawned;

        /// <summary>Every PC: the zone pulsed (healed, or struck in step E).</summary>
        public event Action<AbilityZone> Pulsed;

        /// <summary>
        /// Host only, before spawning: <paramref name="ability"/> cast by <paramref name="caster"/>, centred where the zone
        /// was placed. <paramref name="players"/> are who it can heal.
        /// </summary>
        public void ServerInit(AbilityDefinition ability, PlayerId caster, PlayerRegistry players)
        {
            _ability = ability;
            _players = players;
            _casterId = caster;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer)
            {
                // Network variables are only written once spawned, so they go out with the spawn itself.
                _abilityId.Value = _ability != null ? _ability.Id : AbilityIds.None;
                _caster.Value = _casterId.Value;
                _startTime.Value = NetworkManager.ServerTime.Time;
                _rules = new ZoneRules(Time.time, _ability != null ? _ability.Duration : 0f,
                    _ability != null ? _ability.PulseInterval : 1f);
                _running = true;
            }
            Spawned?.Invoke(this);
        }

        void FixedUpdate()
        {
            if (!IsServer || !_running)
                return;
            float now = Time.time;
            if (_rules.TryPulse(now))
                Pulse();
            if (_rules.IsOver(now))
            {
                _running = false;
                NetworkObject.Despawn(destroy: true);
            }
        }

        void Pulse()
        {
            if (_ability == null || _players == null)
                return;
            float3 centre = transform.position;
            float radius = _ability.AoeRadius;
            if (_ability.HealAmount > 0f)
            {
                foreach (PlayerController player in _players.Players)
                {
                    if (player != null && SupportRules.InZone(centre, radius, player.transform.position) &&
                        player.TryGetComponent(out PlayerHealth health))
                        health.ServerHeal(_ability.HealAmount, Caster, _ability.Id);
                }
            }
            PulsedRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        void PulsedRpc() => Pulsed?.Invoke(this);
    }
}
