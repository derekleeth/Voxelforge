using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// This hero's target (Roadmap 5.2). Pressing Target (Tab, T, Numpad Enter or R3) picks the nearest creature within
    /// <see cref="range"/>; each press after that moves on to the next one further away, wrapping round
    /// (<see cref="TargetCycle"/>). <b>The host</b> picks it from the controls it received and keeps it in a network
    /// variable, so every PC can show it. While they have a target, the hero faces it when standing still
    /// (<see cref="PlayerController"/>), and bolts and ground slams from the action bar aim at it (<see cref="PlayerCaster"/>).
    /// The target is dropped when it's defeated or leaves the world, gets further than <see cref="keepRange"/>, or the
    /// hero is knocked out. Each player has their own (CLAUDE.md Law #1).
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerTargeting : NetworkBehaviour
    {
        [Tooltip("A Target press picks creatures this close, in metres.")]
        [SerializeField, Min(1f)] float range = 40f;

        [Tooltip("The target is dropped once it's further than this, in metres.")]
        [SerializeField, Min(1f)] float keepRange = 45f;

        readonly NetworkVariable<ulong> _target = new NetworkVariable<ulong>();
        readonly List<(ulong id, float distance)> _scratch = new List<(ulong id, float distance)>();

        PlayerController _player;
        PlayerButtons _previousButtons;

        /// <summary>Every PC: the targeted creature's <see cref="ICombatCreature.CreatureId"/>, or 0 for none.</summary>
        public ulong TargetId => _target.Value;

        public bool HasTarget => _target.Value != 0;

        void Awake() => _player = GetComponent<PlayerController>();

        public override void OnNetworkDespawn()
        {
            _previousButtons = PlayerButtons.None;
            base.OnNetworkDespawn();
        }

        /// <summary>Every PC: the targeted creature, if it's still in the world and can be hit.</summary>
        public bool TryGetTarget(CombatTargets targets, out ICombatCreature creature)
        {
            creature = null;
            return _target.Value != 0 && targets != null && targets.TryGetCreature(_target.Value, out creature) &&
                   creature.IsTargetable;
        }

        /// <summary>Host only: the middle of the target's body, where spells aim.</summary>
        public bool ServerTryGetTargetMiddle(out float3 middle)
        {
            middle = default;
            if (!IsServer || !TryGetTarget(Targets, out ICombatCreature creature))
                return false;
            middle = CombatTarget.Of(creature).Position;
            return true;
        }

        CombatTargets Targets => _player.Registry != null ? _player.Registry.Targets : null;

        void FixedUpdate()
        {
            if (!IsServer || !IsSpawned)
                return;
            PlayerButtons buttons = _player.ServerInput.Buttons;
            bool pressed = (buttons & ~_previousButtons & PlayerButtons.Target) != 0;
            _previousButtons = buttons;

            CombatTargets targets = Targets;
            float3 feet = transform.position;
            ulong target = _target.Value;
            if (target != 0 && (_player.IsKnockedOut || !TryGetTarget(targets, out ICombatCreature current) ||
                                math.distance(current.Feet, feet) > keepRange))
                target = 0;
            if (pressed && targets != null)
                target = TargetCycle.Next(targets.Creatures, feet, range, target, _scratch);
            if (_target.Value != target)
                _target.Value = target;
        }
    }
}
