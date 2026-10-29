using System;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// This player's spells (Roadmap 4.1): their mana pool, cooldowns and cast bar. <b>The host</b> runs the rules
    /// (<see cref="AbilityCaster"/>) from the input it received, and when a cast finishes it hands the ability to its
    /// delivery, so far <see cref="PlayerStaff"/> for projectiles (CLAUDE.md Laws #1 and #2).
    /// <list type="bullet">
    /// <item>Holding Mine with a ranged item (the M.G.R.S.) casts that item's ability, as in D-019.</item>
    /// <item>The temporary <see cref="PlayerButtons.Ability1"/> button casts <c>testAbility</c> until the 4.4 action bar.</item>
    /// </list>
    /// <para>Every PC hears casts start, finish and stop, to draw cast bars and cooldowns in that player's own view.</para>
    /// </summary>
    [RequireComponent(typeof(PlayerController), typeof(PlayerHands), typeof(PlayerStaff))]
    public sealed class PlayerCaster : NetworkBehaviour
    {
        [Tooltip("Data/Abilities/AbilityBook.asset: every ability, by ID.")]
        [SerializeField] AbilityBook abilities;

        [Tooltip("Cast by the temporary Ability 1 button until the 4.4 action bar.")]
        [SerializeField] AbilityDefinition testAbility;

        [Tooltip("Mana when full. Classes and races (4.6/4.7) will set this.")]
        [SerializeField, Min(0f)] float maxMana = 100f;

        [Tooltip("Mana back per second.")]
        [SerializeField, Min(0f)] float manaRegen = 10f;

        [Tooltip("How long a message (\"Not enough mana\") stays up, in seconds.")]
        [SerializeField, Range(0.5f, 6f)] float messageSeconds = 1.5f;

        // Moving the stick further than this, or stepping this far, cancels a cast (the same as hearthing, 3.8).
        const float MoveDeadzone = 0.2f;
        const float DriftAllowed = 0.35f;
        // The host only sends mana when it changed by at least this much, or reached full.
        const float ManaSendStep = 0.5f;

        readonly NetworkVariable<float> _mana = new NetworkVariable<float>();
        readonly NetworkVariable<float> _maxMana = new NetworkVariable<float>();

        PlayerController _player;
        PlayerHands _hands;
        PlayerStaff _staff;

        // Host state.
        AbilityCaster _caster;
        readonly List<AbilityDefinition> _finished = new List<AbilityDefinition>();
        PlayerButtons _previousButtons;
        float3 _castFeet;
        bool _toldNoMana;

        // Every PC.
        readonly Dictionary<ushort, float> _readyAtLocal = new Dictionary<ushort, float>();
        AbilityDefinition _castingLocal;
        float _castStartLocal;

        // Owner state.
        string _message;
        float _messageUntil;

        public AbilityBook Book => abilities;

        /// <summary>Every PC: this player's mana, as the host last sent it.</summary>
        public float Mana => _mana.Value;

        public float MaxMana => _maxMana.Value;

        /// <summary>Every PC: the ability being cast (one with a cast time), or null.</summary>
        public AbilityDefinition Casting => _castingLocal;

        /// <summary>Every PC: how far through <see cref="Casting"/> the hero is (0-1).</summary>
        public float CastProgress => _castingLocal != null
            ? Mathf.Clamp01((Time.time - _castStartLocal) / Mathf.Max(_castingLocal.CastTime, 0.01f))
            : 0f;

        /// <summary>Owner only: a short message ("Not enough mana"), or null.</summary>
        public string Message => Time.time < _messageUntil ? _message : null;

        /// <summary>Every PC: a cast with a cast time started.</summary>
        public event Action<AbilityDefinition> CastStarted;

        /// <summary>Every PC: an ability went off (instant or at the end of its cast).</summary>
        public event Action<AbilityDefinition> CastFinished;

        /// <summary>Every PC: a cast stopped before it went off, and why.</summary>
        public event Action<AbilityDefinition, CastCheck> CastStopped;

        void Awake()
        {
            _player = GetComponent<PlayerController>();
            _hands = GetComponent<PlayerHands>();
            _staff = GetComponent<PlayerStaff>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (!IsServer)
                return;
            _caster = new AbilityCaster(maxMana, manaRegen);
            _maxMana.Value = _caster.MaxMana;
            _mana.Value = _caster.Mana;
        }

        public override void OnNetworkDespawn()
        {
            _caster = null;
            _castingLocal = null;
            _readyAtLocal.Clear();
            base.OnNetworkDespawn();
        }

        /// <summary>
        /// Every PC: how recharged <paramref name="ability"/> is, 0 (just cast) to 1 (ready). Instant feedback on this PC,
        /// from when it heard the ability went off.
        /// </summary>
        public float CooldownReady(AbilityDefinition ability)
        {
            if (ability == null || ability.Cooldown <= 0f || !_readyAtLocal.TryGetValue(ability.Id, out float readyAt))
                return 1f;
            return Mathf.Clamp01(1f - (readyAt - Time.time) / ability.Cooldown);
        }

        void FixedUpdate()
        {
            if (!IsServer || !IsSpawned || _caster == null)
                return;

            float now = Time.time;
            PlayerInputFrame input = _player.ServerInput;
            PlayerButtons pressed = input.Buttons & ~_previousButtons;
            _previousButtons = input.Buttons;
            _finished.Clear();

            AbilityDefinition wasCasting = _caster.Casting;
            bool moving = wasCasting != null && Moved(input);
            if (_caster.Tick(now, Time.fixedDeltaTime, moving, _finished, out CastCheck result) && result != CastCheck.Ok)
            {
                CastStoppedRpc(wasCasting.Id, result);
                RefusedRpc(result);
            }

            if (_player.HasLanded)
            {
                // The M.G.R.S.: holding Mine keeps casting it. Running dry is said once per press, so holding doesn't spam.
                if ((pressed & PlayerButtons.Mine) != 0)
                    _toldNoMana = false;
                if (input.IsPressed(PlayerButtons.Mine) && _hands.TryGetHeldAbility(out AbilityDefinition held))
                {
                    CastCheck check = TryCast(held, now);
                    if (check == CastCheck.NotEnoughMana && !_toldNoMana)
                    {
                        _toldNoMana = true;
                        RefusedRpc(check);
                    }
                }
                if ((pressed & PlayerButtons.Ability1) != 0 && testAbility != null)
                {
                    CastCheck check = TryCast(testAbility, now);
                    if (check != CastCheck.Ok)
                        RefusedRpc(check);
                }
            }

            foreach (AbilityDefinition ability in _finished)
            {
                Deliver(ability, input);
                CastFinishedRpc(ability.Id);
            }

            float mana = _caster.Mana;
            if (Mathf.Abs(_mana.Value - mana) >= ManaSendStep || (mana >= _caster.MaxMana && _mana.Value < _caster.MaxMana))
                _mana.Value = mana;
        }

        CastCheck TryCast(AbilityDefinition ability, float now)
        {
            CastCheck check = _caster.TryBegin(ability, now, _finished);
            if (check == CastCheck.Ok && _caster.IsCasting)
            {
                _castFeet = transform.position;
                CastStartedRpc(ability.Id);
            }
            return check;
        }

        /// <summary>Host: the hero is moving, jumping or drifting from where the cast started.</summary>
        bool Moved(PlayerInputFrame input) =>
            math.length(input.Move) > MoveDeadzone || input.IsPressed(PlayerButtons.Jump) ||
            math.distance(((float3)transform.position).xz, _castFeet.xz) > DriftAllowed;

        /// <summary>Host: a finished ability takes effect, from the hero's eyes along the aim the host received.</summary>
        void Deliver(AbilityDefinition ability, PlayerInputFrame input)
        {
            switch (ability.Delivery)
            {
                case AbilityDelivery.Projectile:
                    _staff.ServerLaunch(ability, _player.Aim.Eyes(transform.position), _player.Aim.Direction(input));
                    break;
            }
        }

        bool TryGetAbility(ushort abilityId, out AbilityDefinition ability)
        {
            ability = null;
            return abilities != null && abilities.TryGet(abilityId, out ability);
        }

        [Rpc(SendTo.ClientsAndHost)]
        void CastStartedRpc(ushort abilityId)
        {
            if (!TryGetAbility(abilityId, out AbilityDefinition ability))
                return;
            _castingLocal = ability;
            _castStartLocal = Time.time;
            CastStarted?.Invoke(ability);
        }

        [Rpc(SendTo.ClientsAndHost)]
        void CastFinishedRpc(ushort abilityId)
        {
            if (!TryGetAbility(abilityId, out AbilityDefinition ability))
                return;
            if (_castingLocal == ability)
                _castingLocal = null;
            if (ability.Cooldown > 0f)
                _readyAtLocal[abilityId] = Time.time + ability.Cooldown;
            CastFinished?.Invoke(ability);
        }

        [Rpc(SendTo.ClientsAndHost)]
        void CastStoppedRpc(ushort abilityId, CastCheck reason)
        {
            if (!TryGetAbility(abilityId, out AbilityDefinition ability))
                return;
            if (_castingLocal == ability)
                _castingLocal = null;
            CastStopped?.Invoke(ability, reason);
        }

        [Rpc(SendTo.Owner)]
        void RefusedRpc(CastCheck reason)
        {
            _message = Explain(reason);
            _messageUntil = Time.time + messageSeconds;
        }

        static string Explain(CastCheck reason)
        {
            switch (reason)
            {
                case CastCheck.NotEnoughMana:
                    return "Not enough mana";
                case CastCheck.OnCooldown:
                    return "Not ready yet";
                case CastCheck.Busy:
                    return "Already casting";
                case CastCheck.Interrupted:
                    return "Interrupted: stand still while you cast";
                default:
                    return "Can't cast that";
            }
        }
    }
}
