using System;
using Unity.Netcode;
using UnityEngine;
using Voxelforge.Core;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// This hero's health and shield (Roadmap 4.6). <b>The host</b> runs the rules (<see cref="HeroHealth"/>) and is the
    /// only one that hurts, heals or shields a hero; every PC sees the numbers through network variables and hears
    /// what happened as <see cref="HealthEvent"/>s, for the health bar and combat text in that player's own view.
    /// <para>There's no fall damage on purpose. In 4.6 only a sparring Training Dummy hurts heroes, and never below
    /// <see cref="SparringFloor"/>, so nobody is knocked out until Phase 5 brings monsters.</para>
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerHealth : NetworkBehaviour
    {
        [Tooltip("Health when full. Classes (4.6) and races (4.7) change it.")]
        [SerializeField, Min(1f)] float maxHealth = 100f;

        /// <summary>The share of max health sparring can't take a hero under.</summary>
        public const float SparringFloor = 0.1f;

        // The host only sends health when it changed by at least this much, or reached full.
        const float SendStep = 0.5f;

        readonly NetworkVariable<float> _health = new NetworkVariable<float>();
        readonly NetworkVariable<float> _maxHealth = new NetworkVariable<float>();
        readonly NetworkVariable<float> _shield = new NetworkVariable<float>();

        // Host state.
        HeroHealth _rules;

        /// <summary>Every PC: health as the host last sent it.</summary>
        public float Health => _health.Value;

        public float MaxHealth => _maxHealth.Value;

        /// <summary>Every PC: damage the shield will still soak up.</summary>
        public float Shield => _shield.Value;

        /// <summary>Every PC: something happened to this hero's health.</summary>
        public event Action<HealthEvent> Changed;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (!IsServer)
                return;
            _rules = new HeroHealth(maxHealth);
            Send(force: true);
        }

        public override void OnNetworkDespawn()
        {
            _rules = null;
            base.OnNetworkDespawn();
        }

        /// <summary>
        /// Host only: hurts the hero. Their shield soaks up what it can first, and health never goes under
        /// <paramref name="floorShare"/> of max (sparring uses <see cref="SparringFloor"/>).
        /// </summary>
        public DamageResult ServerDamage(float amount, PlayerId source, Vector3 sourcePosition, float floorShare = SparringFloor,
            ushort abilityId = AbilityIds.None)
        {
            if (!IsServer || _rules == null)
                return default;
            DamageResult result = _rules.Damage(amount, Time.time, _rules.Max * Mathf.Clamp01(floorShare));
            if (result.Taken > 0f)
                Tell(HealthEventKind.Damaged, result.Taken, source, abilityId, sourcePosition);
            else if (result.Absorbed > 0f)
                Tell(HealthEventKind.Absorbed, result.Absorbed, source, abilityId, sourcePosition);
            Send(force: true);
            return result;
        }

        /// <summary>Host only: heals the hero. Returns how much it really healed (nothing past full).</summary>
        public float ServerHeal(float amount, PlayerId source, ushort abilityId = AbilityIds.None)
        {
            if (!IsServer || _rules == null)
                return 0f;
            float healed = _rules.Heal(amount);
            if (healed > 0f)
            {
                Tell(HealthEventKind.Healed, healed, source, abilityId, transform.position);
                Send(force: true);
            }
            return healed;
        }

        /// <summary>Host only: a shield that soaks up <paramref name="amount"/> damage for <paramref name="seconds"/>.</summary>
        public void ServerShield(float amount, float seconds, PlayerId source, ushort abilityId = AbilityIds.None)
        {
            if (!IsServer || _rules == null)
                return;
            _rules.SetShield(amount, Time.time + seconds);
            Tell(HealthEventKind.Shielded, amount, source, abilityId, transform.position);
            Send(force: true);
        }

        /// <summary>Host only: tells every PC something is flying at this hero from <paramref name="from"/>, landing in <paramref name="seconds"/>.</summary>
        public void ServerIncoming(Vector3 from, float seconds)
        {
            if (IsServer && IsSpawned)
                Tell(HealthEventKind.Incoming, seconds, PlayerId.None, AbilityIds.None, from);
        }

        /// <summary>Host only: a new most health (a class, 4.6), keeping the same share of it.</summary>
        public void ServerSetMaxHealth(float max)
        {
            if (!IsServer || _rules == null)
                return;
            _rules.SetMax(max);
            Send(force: true);
        }

        void FixedUpdate()
        {
            if (!IsServer || !IsSpawned || _rules == null)
                return;
            _rules.Tick(Time.time, Time.fixedDeltaTime);
            Send(force: false);
        }

        void Send(bool force)
        {
            if (_maxHealth.Value != _rules.Max)
                _maxHealth.Value = _rules.Max;
            if (_shield.Value != _rules.Shield)
                _shield.Value = _rules.Shield;
            float health = _rules.Current;
            if (force || Mathf.Abs(_health.Value - health) >= SendStep || (_rules.IsFull && _health.Value < health))
                _health.Value = health;
        }

        void Tell(HealthEventKind kind, float amount, PlayerId source, ushort abilityId, Vector3 sourcePosition) =>
            ChangedRpc(new HealthEvent
            {
                Kind = kind, Amount = amount, Source = source.Value, AbilityId = abilityId, SourcePosition = sourcePosition,
            });

        [Rpc(SendTo.ClientsAndHost)]
        void ChangedRpc(HealthEvent e) => Changed?.Invoke(e);
    }
}
