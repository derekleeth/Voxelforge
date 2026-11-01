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
    /// <para>There's no fall damage on purpose. A sparring Training Dummy never takes a hero below
    /// <see cref="SparringFloor"/>, but a creature can knock them out (Roadmap 5.2): <see cref="IsKnockedOut"/> is set on
    /// every PC, and <see cref="PlayerKnockout"/> wakes them at home when <see cref="ServerReadyToWake"/> says so.</para>
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
        readonly NetworkVariable<bool> _knockedOut = new NetworkVariable<bool>();

        // Host state.
        HeroHealth _rules;
        PlayerBlockBag _bag;

        /// <summary>Every PC: health as the host last sent it.</summary>
        public float Health => _health.Value;

        public float MaxHealth => _maxHealth.Value;

        /// <summary>Every PC: damage the shield will still soak up.</summary>
        public float Shield => _shield.Value;

        /// <summary>Every PC: the hero's health ran out and they're lying there until they wake (Roadmap 5.2).</summary>
        public bool IsKnockedOut => _knockedOut.Value;

        /// <summary>Host only: a knocked-out hero has waited long enough to wake.</summary>
        public bool ServerReadyToWake => IsServer && _rules != null && _rules.ReadyToWake(Time.time);

        /// <summary>Host only: when a knocked-out hero may wake (<see cref="Time.time"/>).</summary>
        public float ServerWakeAt => _rules != null ? _rules.WakeAt : 0f;

        /// <summary>Every PC: something happened to this hero's health.</summary>
        public event Action<HealthEvent> Changed;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (!IsServer)
                return;
            _rules = new HeroHealth(maxHealth);
            TryGetComponent(out _bag); // Worn Armor (Roadmap 5.5).
            Send(force: true);
        }

        public override void OnNetworkDespawn()
        {
            _rules = null;
            base.OnNetworkDespawn();
        }

        /// <summary>
        /// Host only: hurts the hero. Their shield soaks up what it can first, and health never goes under
        /// <paramref name="floorShare"/> of max (sparring uses <see cref="SparringFloor"/>; creatures pass 0, so they can
        /// knock a hero out).
        /// </summary>
        public DamageResult ServerDamage(float amount, PlayerId source, Vector3 sourcePosition, float floorShare = SparringFloor,
            ushort abilityId = AbilityIds.None)
        {
            if (!IsServer || _rules == null)
                return default;
            // Worn Armor cuts every hit (Roadmap 5.5).
            if (_bag != null)
                amount = GearRules.AfterArmor(amount, _bag.GearStats.armor);
            DamageResult result = _rules.Damage(amount, Time.time, _rules.Max * Mathf.Clamp01(floorShare));
            if (result.Taken > 0f)
                Tell(HealthEventKind.Damaged, result.Taken, source, abilityId, sourcePosition);
            else if (result.Absorbed > 0f)
                Tell(HealthEventKind.Absorbed, result.Absorbed, source, abilityId, sourcePosition);
            if (result.KnockedOut)
            {
                _knockedOut.Value = true;
                Tell(HealthEventKind.KnockedOut, HeroHealth.KnockOutSeconds, source, abilityId, sourcePosition);
            }
            Send(force: true);
            return result;
        }

        /// <summary>
        /// Host only: wakes a knocked-out hero with <see cref="HeroHealth.WakeShare"/> of their health, once
        /// <see cref="PlayerKnockout"/> has put them back at home.
        /// </summary>
        public void ServerWake()
        {
            if (!IsServer || _rules == null || !_rules.IsKnockedOut)
                return;
            _rules.Wake();
            _knockedOut.Value = false;
            Tell(HealthEventKind.Woke, _rules.Current, PlayerId.None, AbilityIds.None, transform.position);
            Send(force: true);
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

        /// <summary>Host only: back to full health (a level-up, Roadmap 5.6). Not while knocked out.</summary>
        public void ServerRefill()
        {
            if (!IsServer || _rules == null)
                return;
            _rules.Refill();
            Send(force: true);
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
