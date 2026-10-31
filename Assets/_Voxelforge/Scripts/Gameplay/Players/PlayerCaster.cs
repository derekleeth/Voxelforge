using System;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// This player's spells (Roadmap 4.1): their mana pool, cooldowns and cast bar. <b>The host</b> runs the rules
    /// (<see cref="AbilityCaster"/>) from the input it received, and when a cast finishes it hands the ability to its
    /// delivery: <see cref="PlayerStaff"/> for projectiles and ground slams, <see cref="PlayerSupport"/> for shields and
    /// zones, <see cref="EarthWorks"/> for raised earth (CLAUDE.md Laws #1 and #2).
    /// <list type="bullet">
    /// <item>Holding Mine with a ranged item (the M.G.R.S.) casts that item's ability, as in D-019.</item>
    /// <item>The action bar buttons cast the slots of <see cref="PlayerActionBar"/> (4.4).</item>
    /// </list>
    /// <para>Rules only (Roadmap 4.2): casts starting, going off and stopping go to every PC as <see cref="AbilityCue"/>s
    /// (<see cref="PlayerAbilityCues"/>). Each PC times the cast bar and cooldowns in that player's own view from them.</para>
    /// </summary>
    [RequireComponent(typeof(PlayerController), typeof(PlayerHands), typeof(PlayerStaff))]
    [RequireComponent(typeof(PlayerAbilityCues))]
    public sealed class PlayerCaster : NetworkBehaviour
    {
        [Tooltip("Data/Abilities/AbilityBook.asset: every ability, by ID.")]
        [SerializeField] AbilityBook abilities;

        [Tooltip("Mana when full. Classes and races (4.6/4.7) will set this.")]
        [SerializeField, Min(0f)] float maxMana = 100f;

        [Tooltip("Mana back per second.")]
        [SerializeField, Min(0f)] float manaRegen = 10f;

        // Moving the stick further than this, or stepping this far, cancels a cast (the same as hearthing, 3.8).
        const float MoveDeadzone = 0.2f;
        const float DriftAllowed = 0.35f;
        // The host only sends mana when it changed by at least this much, or reached full.
        const float ManaSendStep = 0.5f;
        // How far a wall or totem cast at a cliff face drops to find the ground under it, in blocks.
        const int MaxGroundDrop = 4;

        readonly NetworkVariable<float> _mana = new NetworkVariable<float>();
        readonly NetworkVariable<float> _maxMana = new NetworkVariable<float>();

        PlayerController _player;
        PlayerHands _hands;
        PlayerStaff _staff;
        PlayerAbilityCues _cues;
        PlayerActionBar _actionBar;
        PlayerSupport _support;

        // Host state.
        AbilityCaster _caster;
        readonly List<AbilityDefinition> _finished = new List<AbilityDefinition>();
        PlayerButtons _previousButtons;
        float3 _castFeet;
        bool _toldNoMana;
        // The class's regeneration, and a short boost on top of it from a Wellspring Totem (Roadmap 4.6).
        float _baseRegen;
        float _regenBonus;
        float _regenBonusUntil;

        // Every PC.
        readonly Dictionary<ushort, float> _readyAtLocal = new Dictionary<ushort, float>();
        AbilityDefinition _castingLocal;
        float _castStartLocal;

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

        /// <summary>Owner only: the host refused a cast ("Not enough mana"), for the combat text (4.4).</summary>
        public event Action<CastCheck> Refused;

        void Awake()
        {
            _player = GetComponent<PlayerController>();
            _hands = GetComponent<PlayerHands>();
            _staff = GetComponent<PlayerStaff>();
            _cues = GetComponent<PlayerAbilityCues>();
            _actionBar = GetComponent<PlayerActionBar>();
            _support = GetComponent<PlayerSupport>();
        }

        void OnEnable() => _cues.Received += OnCue;

        void OnDisable() => _cues.Received -= OnCue;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (!IsServer)
                return;
            _caster = new AbilityCaster(maxMana, manaRegen);
            _baseRegen = manaRegen;
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
        /// Host only: a new mana pool and regeneration (a class, Roadmap 4.6). Any cast in progress stops for free, and
        /// the hero keeps the same share of their mana so changing class never looks like spending or gaining it.
        /// </summary>
        public void ServerSetPool(float maxMana, float regen)
        {
            if (!IsServer || _caster == null)
                return;
            if (_caster.Casting != null)
            {
                _cues.ServerSend(AbilityCue.Cast(AbilityCueKind.CastStopped, _caster.Casting.Id, transform.position,
                    CastCheck.Interrupted));
                _caster.Cancel();
            }
            float share = _caster.MaxMana > 0f ? _caster.Mana / _caster.MaxMana : 1f;
            _caster.SetMaxMana(maxMana);
            _caster.SetMana(maxMana * share);
            _baseRegen = regen;
            _maxMana.Value = _caster.MaxMana;
            _mana.Value = _caster.Mana;
        }

        /// <summary>
        /// Host only: mana comes back <paramref name="bonus"/> faster again (0.5 = half as fast again) for
        /// <paramref name="seconds"/>, e.g. while standing by a Wellspring Totem, which renews it every pulse (Roadmap 4.6).
        /// The biggest boost going wins; boosts don't add up.
        /// </summary>
        public void ServerBoostRegen(float bonus, float seconds)
        {
            if (!IsServer)
                return;
            float now = Time.time;
            if (now >= _regenBonusUntil || bonus >= _regenBonus)
                _regenBonus = bonus;
            _regenBonusUntil = Mathf.Max(_regenBonusUntil, now + seconds);
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

            _caster.Regen = _baseRegen * (1f + (now < _regenBonusUntil ? _regenBonus : 0f));
            AbilityDefinition wasCasting = _caster.Casting;
            bool moving = wasCasting != null && Moved(input);
            if (_caster.Tick(now, Time.fixedDeltaTime, moving, _finished, out CastCheck result) && result != CastCheck.Ok)
            {
                _cues.ServerSend(AbilityCue.Cast(AbilityCueKind.CastStopped, wasCasting.Id, transform.position, result));
                RefusedRpc(result);
            }

            if (_player.HasLanded)
            {
                // The M.G.R.S.: holding Mine keeps casting it. Running dry is said once per press, so holding doesn't spam.
                if ((pressed & PlayerButtons.Mine) != 0)
                    _toldNoMana = false;
                if (input.IsPressed(PlayerButtons.Mine) && _hands.TryGetHeldAbility(out AbilityDefinition held))
                {
                    CastCheck check = TryCast(held, now, input);
                    if (check == CastCheck.NotEnoughMana && !_toldNoMana)
                    {
                        _toldNoMana = true;
                        RefusedRpc(check);
                    }
                }
                for (int i = 0; i < PlayerActionBar.SlotCount && _actionBar != null; i++)
                {
                    if ((pressed & ActionSlotButtons.For(i)) == 0 || !_actionBar.TryGet(i, out AbilityDefinition ability))
                        continue;
                    CastCheck check = TryCast(ability, now, input);
                    if (check != CastCheck.Ok)
                        RefusedRpc(check);
                }
            }

            foreach (AbilityDefinition ability in _finished)
            {
                _cues.ServerSend(AbilityCue.Cast(AbilityCueKind.Released, ability.Id, transform.position));
                Deliver(ability, input);
            }

            float mana = _caster.Mana;
            if (Mathf.Abs(_mana.Value - mana) >= ManaSendStep || (mana >= _caster.MaxMana && _mana.Value < _caster.MaxMana))
                _mana.Value = mana;
        }

        CastCheck TryCast(AbilityDefinition ability, float now, PlayerInputFrame input)
        {
            // A ground slam, wall or totem needs ground in range, checked before any mana is spent.
            if (ability.NeedsAimedBlock && !TryAimAtBlock(ability, input, out _))
                return CastCheck.NoTarget;
            CastCheck check = _caster.TryBegin(ability, now, _finished);
            if (check == CastCheck.Ok && _caster.IsCasting)
            {
                _castFeet = transform.position;
                _cues.ServerSend(AbilityCue.Cast(AbilityCueKind.CastBegan, ability.Id, transform.position));
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
                case AbilityDelivery.GroundSlam:
                    if (TryAimAtBlock(ability, input, out VoxelHit hit))
                        _staff.ServerStrikeAt(ability, hit);
                    break;
                case AbilityDelivery.Shield:
                    if (_support != null)
                        _support.ServerShield(ability, input);
                    break;
                case AbilityDelivery.Zone:
                    if (_support != null)
                        _support.ServerZone(ability);
                    break;
                case AbilityDelivery.Pillar:
                    EarthWorks earth = _player.Registry != null ? _player.Registry.Earth : null;
                    if (earth != null)
                        earth.ServerRaisePillar(_player, ability);
                    break;
                case AbilityDelivery.Wall:
                    EarthWorks walls = _player.Registry != null ? _player.Registry.Earth : null;
                    if (walls != null && TryAimAtBlock(ability, input, out VoxelHit wallHit))
                    {
                        // A wall stands on the ground under where the hero aims, even when they aim at a cliff face.
                        int3 anchor = EarthRules.TryFindTotemSpot(_player.World, wallHit, MaxGroundDrop, out int3 spot)
                            ? spot
                            : wallHit.Block + wallHit.Normal;
                        walls.ServerRaiseWall(_player, ability, anchor, _player.Aim.Direction(input));
                    }
                    break;
                case AbilityDelivery.Totem:
                    if (_support != null && TryAimAtBlock(ability, input, out VoxelHit totemHit) &&
                        EarthRules.TryFindTotemSpot(_player.World, totemHit, MaxGroundDrop, out int3 totemSpot))
                        _support.ServerTotem(ability, totemSpot);
                    break;
                case AbilityDelivery.Quake:
                    _staff.ServerQuake(ability);
                    break;
                case AbilityDelivery.SelfHeal:
                    if (_support != null)
                        _support.ServerSelfHeal(ability);
                    break;
            }
        }

        /// <summary>Host: the block (or target's body, Roadmap 4.6) the hero aims at within <paramref name="ability"/>'s range.</summary>
        bool TryAimAtBlock(AbilityDefinition ability, PlayerInputFrame input, out VoxelHit hit)
        {
            hit = default;
            VoxelWorldManager world = _player.WorldManager;
            CombatTargets targets = _player.Registry != null ? _player.Registry.Targets : null;
            return world != null && world.World != null && ManaBoltFlight.TryHit(world.World, targets,
                _player.Aim.Eyes(transform.position), _player.Aim.Direction(input), ability.Range, out hit);
        }

        /// <summary>Every PC: keeps the cast bar and cooldowns in step with what the host said.</summary>
        void OnCue(AbilityCue cue, AbilityDefinition ability)
        {
            switch (cue.Kind)
            {
                case AbilityCueKind.CastBegan:
                    _castingLocal = ability;
                    _castStartLocal = Time.time;
                    break;
                case AbilityCueKind.CastStopped:
                    if (_castingLocal == ability)
                        _castingLocal = null;
                    break;
                case AbilityCueKind.Released:
                    if (_castingLocal == ability)
                        _castingLocal = null;
                    if (ability.Cooldown > 0f)
                        _readyAtLocal[ability.Id] = Time.time + ability.Cooldown;
                    break;
            }
        }

        [Rpc(SendTo.Owner)]
        void RefusedRpc(CastCheck reason) => Refused?.Invoke(reason);
    }
}
