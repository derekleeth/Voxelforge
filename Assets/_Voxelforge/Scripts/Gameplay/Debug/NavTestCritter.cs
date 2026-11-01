using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using Voxelforge.Core;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// A stand-in creature for play-testing the nav grid (Roadmap 5.1) until real mobs arrive in 5.2. It chases one hero,
    /// chosen by <see cref="PlayerId"/> when it's spawned (CLAUDE.md Law #1), around walls, up steps and down ledges.
    /// Dig a trench or build a wall in its way and it finds a new way round.
    /// <para>
    /// Since 5.2 step A it's also a <see cref="ICombatCreature"/>: spells hurt and stun it, it's defeated at 0 health,
    /// and it bites its hero when it catches them, with a little knockback, which can knock them out. That tests
    /// creature targets and knock-outs before the real mobs exist.
    /// </para>
    /// <para>
    /// <b>The host</b> moves it with the same <see cref="VoxelBodyMotor"/> heroes use, steered by a
    /// <see cref="NavPathFollower"/>, and a server-authority <c>NetworkTransform</c> shows it on every PC (Law #2).
    /// <see cref="NavTestCritters"/> spawns them.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NavTestCritter : NetworkBehaviour, ICombatCreature
    {
        [SerializeField] MotorSettings motor = new MotorSettings
        {
            WalkSpeed = 3.5f,
            JumpHeight = 1.25f,
            Gravity = 25f,
            MaxFallSpeed = 50f,
            StepHeight = 1f,
            HalfExtents = new float3(0.3f, 0.9f, 0.3f),
        };

        [Tooltip("It stops this close to its hero, in metres, and sets off again past Chase Distance.")]
        [SerializeField, Range(0.5f, 5f)] float stopDistance = 1.5f;

        [SerializeField, Range(1f, 8f)] float chaseDistance = 2.5f;

        [SerializeField, Min(1f)] float turnSpeed = 540f;

        [Header("Fighting (Roadmap 5.2 step A)")]
        [SerializeField, Min(1f)] float maxHealth = 60f;

        [Tooltip("Damage of one bite. It can knock a hero out.")]
        [SerializeField, Min(0f)] float biteDamage = 12f;

        [Tooltip("Seconds between bites while it's close enough.")]
        [SerializeField, Min(0.1f)] float biteEvery = 1.5f;

        [Tooltip("How hard a bite shoves the hero away, in metres per second.")]
        [SerializeField, Min(0f)] float biteShove = 5f;

        [Tooltip("Seconds it lies there after being defeated, before it leaves the world.")]
        [SerializeField, Min(0f)] float defeatedSeconds = 0.75f;

        [Tooltip("Draws its path, host only. Turned on and off by NavTestCritters.")]
        [SerializeField] LineRenderer pathLine;

        // Host-only state.
        VoxelWorldManager _world;
        PlayerRegistry _players;
        PlayerId _target;
        NavPathFollower _follower;
        VoxelBody _body;
        bool _placed;
        bool _resting;
        float3 _previousFeet;
        float _yaw, _previousYaw;
        bool _showPath;
        float _health;
        float _nextBiteAt;
        float _stunnedUntil = float.NegativeInfinity;
        float _despawnAt = float.PositiveInfinity;
        bool _defeated;

        /// <summary>Host only: the hero it chases.</summary>
        public PlayerId Target => _target;

        /// <summary>Host only: what it's doing, for the debug readout.</summary>
        public NavPathFollower Follower => _follower;

        public ulong CreatureId => NetworkObjectId;

        public bool IsTargetable => IsSpawned && !_defeated;

        public float3 Feet => IsServer && _placed ? _body.FeetPosition(motor) : (float3)transform.position;

        public float3 HalfExtents => motor.HalfExtents;

        /// <summary>Host only, before spawning: where it stands and who it chases.</summary>
        internal void ServerInitialize(VoxelWorldManager world, PlayerRegistry players, PlayerId target, float3 feet)
        {
            _world = world;
            _players = players;
            _target = target;
            _follower = new NavPathFollower(world.Paths, NavAgentProfile.For(motor));
            _body = new VoxelBody { Position = feet + new float3(0f, motor.HalfExtents.y + 0.001f, 0f) };
            _previousFeet = feet;
            _placed = true;
            _health = maxHealth;
            transform.position = feet;
        }

        public TargetDamage ServerDamage(float amount, PlayerId instigator, float threatScale = 1f)
        {
            if (!IsServer || _defeated || amount <= 0f)
                return default;
            float dealt = math.min(amount, _health);
            _health -= dealt;
            if (_health <= 0f)
            {
                _defeated = true;
                _despawnAt = Time.time + defeatedSeconds;
            }
            return new TargetDamage(dealt, _health, maxHealth, _defeated);
        }

        /// <summary>A test critter only follows whoever it was told to: it keeps no threat.</summary>
        public ThreatTable ServerThreat => null;

        public MobThreatView ThreatView => default;

        public bool ServerTaunt(float seconds, PlayerId taunter) => false;

        public bool ServerStun(float seconds, PlayerId instigator, float threatScale = 1f)
        {
            if (!IsServer || _defeated || seconds <= 0f)
                return false;
            _stunnedUntil = math.max(_stunnedUntil, Time.time + seconds);
            return true;
        }

        /// <summary>Host only: shows or hides its path line.</summary>
        internal void ShowPath(bool show)
        {
            _showPath = show;
            if (pathLine != null)
                pathLine.enabled = show;
        }

        public override void OnNetworkSpawn()
        {
            if (pathLine != null)
                pathLine.enabled = IsServer && _showPath;
            CombatCreatures.RaiseSpawned(this);
        }

        public override void OnNetworkDespawn()
        {
            CombatCreatures.RaiseDespawned(this);
            base.OnNetworkDespawn();
        }

        void FixedUpdate()
        {
            if (!IsServer || !IsSpawned || !_placed || _world == null || _world.World == null)
                return;

            _previousFeet = _body.FeetPosition(motor);
            _previousYaw = _yaw;
            float3 feet = _previousFeet;
            MotorInput input = default;
            if (_defeated)
            {
                if (Time.time >= _despawnAt)
                {
                    NetworkObject.Despawn();
                    return;
                }
                _follower.Stop();
            }
            else if (Time.time < _stunnedUntil)
            {
                _follower.Stop();
            }
            // It leaves a knocked-out hero alone (Roadmap 5.2).
            else if (_players != null && _players.TryGet(_target, out PlayerRecord record) && record.Controller != null &&
                record.Controller.HasLanded && !record.Controller.IsKnockedOut)
            {
                BodyBounds hero = record.Controller.Bounds;
                float3 heroFeet = hero.Center - new float3(0f, hero.HalfExtents.y, 0f);
                float distance = math.distance(heroFeet, feet);
                if (_resting && distance > chaseDistance)
                    _resting = false;
                else if (!_resting && distance < stopDistance)
                    _resting = true;

                if (_resting)
                {
                    _follower.Stop();
                    if (distance < stopDistance + 0.5f && Time.time >= _nextBiteAt)
                        Bite(record.Controller, feet);
                }
                else
                {
                    _follower.SetDestination(heroFeet);
                    input = _follower.Tick(feet, Time.time);
                }
            }
            else
            {
                _follower.Stop();
            }

            VoxelBodyMotor.Step(ref _body, input, motor, _world.World, Time.fixedDeltaTime);
            if (math.lengthsq(input.Move) > 0.01f)
                _yaw = math.fmod(MoveDirection.TurnToward(_yaw, MoveDirection.YawOf(input.Move), turnSpeed * Time.fixedDeltaTime), 360f);
        }

        void Bite(PlayerController hero, float3 feet)
        {
            _nextBiteAt = Time.time + biteEvery;
            if (!hero.TryGetComponent(out PlayerHealth health))
                return;
            health.ServerDamage(biteDamage, PlayerId.None, (Vector3)(feet + new float3(0f, 0.5f, 0f)), floorShare: 0f);
            float3 away = math.normalizesafe(((float3)hero.transform.position - feet) * new float3(1f, 0f, 1f));
            hero.ServerShove(away * biteShove + new float3(0f, biteShove * 0.6f, 0f));
        }

        void Update()
        {
            if (!IsServer || !_placed)
                return;

            // The motor ticks at the fixed rate; blend between ticks like heroes do.
            float alpha = math.saturate((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
            transform.SetPositionAndRotation(math.lerp(_previousFeet, _body.FeetPosition(motor), alpha),
                Quaternion.Euler(0f, Mathf.LerpAngle(_previousYaw, _yaw, alpha), 0f));
            if (_showPath)
                DrawPath();
        }

        void DrawPath()
        {
            if (pathLine == null || _follower == null)
                return;
            var waypoints = _follower.Waypoints;
            int first = _follower.NextWaypoint;
            int count = math.max(0, waypoints.Count - first);
            pathLine.positionCount = count + 1;
            pathLine.SetPosition(0, transform.position + Vector3.up * 0.15f);
            for (int i = 0; i < count; i++)
            {
                int3 cell = waypoints[first + i];
                pathLine.SetPosition(i + 1, new Vector3(cell.x + 0.5f, cell.y + 0.15f, cell.z + 0.5f));
            }
            // Green for a path to the hero, amber for "as close as I can get".
            Color color = _follower.Status == NavPathStatus.Complete ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.7f, 0.2f);
            pathLine.startColor = pathLine.endColor = color;
        }
    }
}
