using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using Voxelforge.Core;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// One creature in the world (Roadmap 5.2), of the kind its <see cref="MobDefinition"/> says.
    /// <para>
    /// <b>The host</b> runs it: a <see cref="MobBrain"/> decides what to do, a <see cref="NavPathFollower"/> finds the way
    /// over the 5.1 nav grid, and the same <see cref="VoxelBodyMotor"/> heroes use moves its body. Its hits hurt heroes
    /// through <see cref="PlayerHealth"/> (they can knock them out) and shove them back. Spells hurt it through
    /// <see cref="CombatTargets"/> (<see cref="ICombatCreature"/>), and its health lives in <see cref="MobRules"/>.
    /// </para>
    /// <para>
    /// Every PC sees it through a server-authority <c>NetworkTransform</c> and its <see cref="State"/>, which is all its
    /// animation needs (<see cref="MobStandInView"/>). Its targets are heroes by <see cref="PlayerId"/> (CLAUDE.md Law #1).
    /// </para>
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class Mob : NetworkBehaviour, ICombatCreature
    {
        [Tooltip("What kind of creature this body is.")]
        [SerializeField] MobDefinition definition;

        [Tooltip("Seconds it lies there after being defeated, before it leaves the world.")]
        [SerializeField, Min(0f)] float defeatedSeconds = 2f;

        readonly NetworkVariable<byte> _state = new NetworkVariable<byte>();

        // Host-only state.
        VoxelWorldManager _world;
        PlayerRegistry _players;
        MobRules _rules;
        MobBrain _brain;
        NavPathFollower _follower;
        VoxelBody _body;
        bool _placed;
        float3 _previousFeet;
        float _yaw, _previousYaw;
        float _moved;
        float _despawnAt = float.PositiveInfinity;
        readonly List<HeroSense> _heroes = new List<HeroSense>();

        public MobDefinition Definition => definition;

        /// <summary>Every PC: what it's doing, as the host last said.</summary>
        public MobState State => (MobState)_state.Value;

        /// <summary>Host only: its brain, for tests and the debug readout.</summary>
        public MobBrain Brain => _brain;

        /// <summary>Host only: its health.</summary>
        public MobRules Rules => _rules;

        public ulong CreatureId => NetworkObjectId;

        public bool IsTargetable => IsSpawned && State != MobState.Defeated && (_rules == null || !_rules.IsDefeated);

        public float3 Feet => IsServer && _placed ? _body.FeetPosition(definition.Motor) : (float3)transform.position;

        public float3 HalfExtents => definition.Motor.HalfExtents;

        /// <summary>
        /// Host only, before spawning: it stands at <paramref name="feet"/>, which becomes its home.
        /// <paramref name="seed"/> picks where it wanders, so two mobs side by side don't move as one.
        /// </summary>
        internal void ServerInitialize(VoxelWorldManager world, PlayerRegistry players, float3 feet, uint seed)
        {
            _world = world;
            _players = players;
            MotorSettings motor = definition.Motor;
            _rules = new MobRules(definition.MaxHealth);
            _brain = new MobBrain(definition, _rules, feet, seed, Time.time);
            _follower = new NavPathFollower(world.Paths, NavAgentProfile.For(motor));
            _body = new VoxelBody { Position = feet + new float3(0f, motor.HalfExtents.y + 0.001f, 0f) };
            _previousFeet = feet;
            _yaw = _previousYaw = new Unity.Mathematics.Random(seed == 0 ? 1u : seed).NextFloat(0f, 360f);
            _placed = true;
            transform.SetPositionAndRotation(feet, Quaternion.Euler(0f, _yaw, 0f));
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            CombatCreatures.RaiseSpawned(this);
        }

        public override void OnNetworkDespawn()
        {
            CombatCreatures.RaiseDespawned(this);
            base.OnNetworkDespawn();
        }

        public TargetDamage ServerDamage(float amount, PlayerId instigator)
        {
            if (!IsServer || _rules == null)
                return default;
            TargetDamage result = _rules.Hit(amount, instigator);
            if (result.Toppled)
            {
                _despawnAt = Time.time + defeatedSeconds;
                _state.Value = (byte)MobState.Defeated;
            }
            return result;
        }

        public bool ServerStun(float seconds, PlayerId instigator) =>
            IsServer && _rules != null && _rules.Stun(Time.time, seconds, instigator);

        void FixedUpdate()
        {
            if (!IsServer || !IsSpawned || !_placed || _world == null || _world.World == null)
                return;
            float now = Time.time;
            if (now >= _despawnAt)
            {
                NetworkObject.Despawn();
                return;
            }

            MotorSettings motor = definition.Motor;
            _previousFeet = _body.FeetPosition(motor);
            _previousYaw = _yaw;
            float3 feet = _previousFeet;

            GatherHeroes();
            MobIntent intent = _brain.Tick(new MobSenses
            {
                Now = now, Dt = Time.fixedDeltaTime, Feet = feet, Moved = _moved, Heroes = _heroes,
            });
            foreach (MobEvent e in _brain.Events)
            {
                if (e.Kind == MobEventKind.Strike)
                    Strike(e, feet);
            }
            if (_state.Value != (byte)_brain.State)
                _state.Value = (byte)_brain.State;

            MotorInput input = default;
            switch (intent.Move)
            {
                case MobMove.PathTo:
                    _follower.SetDestination(intent.Destination);
                    input = _follower.Tick(feet, now);
                    break;
                case MobMove.Straight:
                    _follower.Stop();
                    input.Move = intent.Direction;
                    break;
                default:
                    _follower.Stop();
                    break;
            }
            input.SpeedScale = intent.SpeedScale;
            VoxelBodyMotor.Step(ref _body, input, motor, _world.World, Time.fixedDeltaTime);
            float3 after = _body.FeetPosition(motor);
            _moved = math.distance(after.xz, feet.xz);

            float2 facing = intent.Face ? intent.FaceToward.xz - feet.xz : input.Move;
            if (math.lengthsq(facing) > 0.0001f)
                _yaw = math.fmod(MoveDirection.TurnToward(_yaw, MoveDirection.YawOf(facing), definition.TurnSpeed * Time.fixedDeltaTime), 360f);
        }

        void Update()
        {
            if (!IsServer || !_placed)
                return;
            // The motor ticks at the fixed rate; blend between ticks like heroes do.
            float alpha = math.saturate((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
            transform.SetPositionAndRotation(math.lerp(_previousFeet, _body.FeetPosition(definition.Motor), alpha),
                Quaternion.Euler(0f, Mathf.LerpAngle(_previousYaw, _yaw, alpha), 0f));
        }

        void GatherHeroes()
        {
            _heroes.Clear();
            if (_players == null)
                return;
            foreach (PlayerController hero in _players.Players)
            {
                if (hero == null || !hero.IsSpawned || !hero.HasLanded)
                    continue;
                BodyBounds bounds = hero.Bounds;
                _heroes.Add(new HeroSense
                {
                    Id = hero.PlayerId, Feet = bounds.Center - new float3(0f, bounds.HalfExtents.y, 0f),
                    KnockedOut = hero.IsKnockedOut,
                });
            }
        }

        /// <summary>Host: one of its attacks landed on a hero. It hurts (it can knock them out) and shoves them away.</summary>
        void Strike(in MobEvent e, float3 feet)
        {
            if (_players == null || !_players.TryGet(e.Target, out PlayerRecord record) || record.Controller == null)
                return;
            MobAttack attack = definition.Attacks[e.Attack];
            PlayerController hero = record.Controller;
            if (hero.TryGetComponent(out PlayerHealth health))
                health.ServerDamage(attack.Damage, PlayerId.None, (Vector3)(feet + new float3(0f, HalfExtents.y, 0f)), floorShare: 0f);
            float2 away = math.lengthsq(e.Direction) > 0.0001f
                ? math.normalize(e.Direction)
                : math.normalizesafe(((float3)hero.transform.position).xz - feet.xz);
            hero.ServerShove(new float3(away.x * attack.Knockback, attack.Lift, away.y * attack.Knockback));
        }
    }
}
