using System;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using Voxelforge.Core;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// A circle a spell leaves on the ground for a while (Roadmap 4.6): Hallowed Forge, and the Shaman's totems.
    /// <b>The host</b> spawns it (<see cref="PlayerSupport"/>), pulses it (<see cref="ZoneRules"/>) and despawns it when its
    /// time is up, when a newer totem of the same kind replaces it, or, for a totem, when the block it stands on goes.
    /// Each pulse, in its caster's name:
    /// <list type="bullet">
    /// <item>heals the heroes standing in it (<see cref="SupportRules.InZone"/>) and quickens their mana;</item>
    /// <item>strikes the nearest target in reach (<see cref="CombatTargets"/>), told to every PC like any spell hit
    /// through the caster's <see cref="AbilityCueKind.TargetHit"/> cue.</item>
    /// </list>
    /// Every PC sees which spell it is, where and since when through network variables, and hears each pulse, so
    /// <see cref="AbilityZoneView"/> can draw it from the spell's look. A server-owned entity: no PC can move or end it.
    /// </summary>
    public sealed class AbilityZone : NetworkBehaviour
    {
        [Tooltip("Data/Abilities/AbilityBook.asset: the zone's spell, by ID, on every PC.")]
        [SerializeField] AbilityBook abilities;

        /// <summary>How high on a totem its strikes come from, in metres over its feet.</summary>
        public const float StrikeHeight = 1.5f;

        readonly NetworkVariable<ushort> _abilityId = new NetworkVariable<ushort>();
        readonly NetworkVariable<byte> _caster = new NetworkVariable<byte>();
        readonly NetworkVariable<double> _startTime = new NetworkVariable<double>();

        // Host state.
        ZoneRules _rules;
        PlayerController _casterHero;
        PlayerRegistry _players;
        VoxelWorldManager _world;
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

        /// <summary>Host only: still pulsing (not yet ended).</summary>
        public bool IsRunning => _running;

        /// <summary>Every PC: a zone was placed; views listen for it.</summary>
        public static event Action<AbilityZone> Spawned;

        /// <summary>Every PC: the zone pulsed. If it struck something, <c>struck</c> is true and the point is where.</summary>
        public event Action<AbilityZone, bool, Vector3> Pulsed;

        /// <summary>Host only, before spawning: <paramref name="ability"/> cast by <paramref name="caster"/>, where the zone was placed.</summary>
        public void ServerInit(AbilityDefinition ability, PlayerController caster)
        {
            _ability = ability;
            _casterHero = caster;
            _casterId = caster.PlayerId;
            _players = caster.Registry;
            _world = caster.WorldManager;
        }

        /// <summary>Host only: ends the zone now, e.g. a newer totem of the same kind replaced it.</summary>
        public void ServerEnd()
        {
            if (!IsServer || !_running)
                return;
            _running = false;
            NetworkObject.Despawn(destroy: true);
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
            if (_rules.IsOver(now) || LostItsGround())
            {
                ServerEnd();
                return;
            }
            if (_rules.TryPulse(now))
                Pulse();
        }

        // A totem stands on a block: when that block goes (mined, blasted, crumbled), so does the totem.
        bool LostItsGround()
        {
            if (_ability == null || _ability.Delivery != AbilityDelivery.Totem || _world == null || _world.World == null)
                return false;
            var under = (int3)math.floor((float3)transform.position + new float3(0f, -0.5f, 0f));
            return !VoxelBodyMotor.IsSolid(under, _world.World);
        }

        void Pulse()
        {
            if (_ability == null)
                return;
            float3 centre = transform.position;
            if ((_ability.HealAmount > 0f || _ability.ManaRegenBonus > 0f) && _players != null)
            {
                foreach (PlayerController player in _players.Players)
                {
                    if (player == null || !SupportRules.InZone(centre, _ability.AoeRadius, player.transform.position))
                        continue;
                    if (_ability.HealAmount > 0f && player.TryGetComponent(out PlayerHealth health))
                        health.ServerHeal(_ability.HealAmount, _casterId, _ability.Id);
                    // Renewed every pulse, so it lasts a little longer than one to never flicker off between them.
                    if (_ability.ManaRegenBonus > 0f && player.TryGetComponent(out PlayerCaster caster))
                        caster.ServerBoostRegen(_ability.ManaRegenBonus, _ability.PulseInterval + 0.25f);
                }
            }

            bool struck = false;
            Vector3 point = default;
            if (_ability.CreatureDamage > 0f)
                struck = Strike(centre + new float3(0f, StrikeHeight, 0f), out point);
            PulsedRpc(struck, point);
        }

        /// <summary>Host: the nearest target within the zone's radius takes its damage, in the caster's name.</summary>
        bool Strike(float3 from, out Vector3 point)
        {
            point = default;
            CombatTargets targets = _players != null ? _players.Targets : null;
            if (targets == null || !targets.TryFindNearest(from, _ability.AoeRadius, out CombatTarget target))
                return false;
            TargetDamage result = targets.ServerDamage(target, _ability.CreatureDamage, _casterId);
            point = (Vector3)target.Position;
            // The hit goes out on the caster's own channel, so their combat text and the dummy's bar show it like any spell.
            if (result.Dealt > 0f && _casterHero != null && _casterHero.IsSpawned &&
                _casterHero.TryGetComponent(out PlayerAbilityCues cues))
                cues.ServerSend(AbilityCue.HitTarget(_ability.Id, point, result));
            return true;
        }

        [Rpc(SendTo.ClientsAndHost)]
        void PulsedRpc(bool struck, Vector3 point) => Pulsed?.Invoke(this, struck, point);
    }
}
