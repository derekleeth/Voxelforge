using System;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using Voxelforge.Core;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>A moment in a mob's life every PC hears about, for its sounds and effects (Roadmap 5.2 step G).</summary>
    public enum MobCueKind : byte
    {
        /// <summary>It picked a fight: a grunt, a howl, a bellow.</summary>
        Call,
        /// <summary>It started winding up attack <c>attack</c>.</summary>
        Windup,
        /// <summary>Attack <c>attack</c> landed on a hero.</summary>
        Strike,
        /// <summary>A hero hurt it.</summary>
        Hurt,
        /// <summary>Its health ran out.</summary>
        Defeated,
        /// <summary>It charged into a wall.</summary>
        Dazed,
    }

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
    /// animation needs (<see cref="MobAnimator"/>). Its targets are heroes by <see cref="PlayerId"/> (CLAUDE.md Law #1).
    /// </para>
    /// <para>
    /// Step C: it tells its brain about mobs of its own kind nearby and calls them into its fights, hops if it's a slime,
    /// and bursts into smaller mobs that carry on its fight if its kind splits.
    /// </para>
    /// <para>
    /// Step G: the host tells every PC when it calls, winds up, strikes, is hurt, is defeated or is dazed
    /// (<see cref="Cued"/>), and every PC sees it vanish after its defeat (<see cref="Vanishing"/>), for
    /// <see cref="MobFoley"/>'s sounds and <see cref="MobEffects"/>.
    /// </para>
    /// <para>
    /// Roadmap 5.7: if the Profession Book says its kind can be skinned (or salvaged), its body lies there longer, and
    /// every hero who fought it may skin it once (<see cref="PlayerGatherer"/>). Who still may is synced, so each player's
    /// view can show the hint. Once everyone has, it goes as usual.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class Mob : NetworkBehaviour, ICombatCreature
    {
        [Tooltip("What kind of creature this body is.")]
        [SerializeField] MobDefinition definition;

        [Tooltip("Seconds it lies there after being defeated, before it leaves the world.")]
        [SerializeField, Min(0f)] float defeatedSeconds = 2f;

        [Tooltip("Seconds its body lies there if it can be skinned or salvaged (Roadmap 5.7), for its fighters to get to it.")]
        [SerializeField, Min(0f)] float skinnableSeconds = 20f;

        [Tooltip("How hard the mobs it splits into burst out sideways and up, in metres per second.")]
        [SerializeField] Vector2 splitPop = new Vector2(3f, 5f);

        readonly NetworkVariable<byte> _state = new NetworkVariable<byte>();
        // The attack it's winding up or making, +1 (0 for none), so a boar paws before a charge but rears before a gore.
        readonly NetworkVariable<byte> _attack = new NetworkVariable<byte>();
        // Who it's fighting and how close others are to pulling it (Roadmap 5.3), for each player's own view.
        readonly NetworkVariable<MobThreatView> _threat = new NetworkVariable<MobThreatView>();
        // The heroes (PlayerId values) who may still skin its body (Roadmap 5.7).
        readonly NetworkList<byte> _skinners = new NetworkList<byte>();
        readonly List<PlayerId> _fighters = new List<PlayerId>();

        /// <summary>Seconds between threat view updates, at most; and a share must move this much (of 255) to be sent.</summary>
        const float ThreatViewEvery = 0.2f;
        const int ThreatViewStep = 5;

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
        float _nextHop;
        float _nextThreatView;
        readonly List<HeroSense> _heroes = new List<HeroSense>();
        readonly List<AllySense> _allies = new List<AllySense>();
        readonly List<Mob> _packmates = new List<Mob>();

        public MobDefinition Definition => definition;

        /// <summary>Every PC: the host said it called, wound up, struck, was hurt, was defeated or was dazed. The int is
        /// the attack, for <see cref="MobCueKind.Windup"/> and <see cref="MobCueKind.Strike"/>.</summary>
        public event Action<Mob, MobCueKind, int> Cued;

        /// <summary>Every PC: a defeated mob is leaving the world (its body disappears in a poof).</summary>
        public event Action<Mob> Vanishing;

        /// <summary>
        /// Host only: a mob was just defeated, by the hit from the <see cref="PlayerId"/> (Roadmap 5.5). Its
        /// <see cref="Rules"/> still hold everyone who fought it, so <see cref="PersonalLoot"/> can roll for each.
        /// </summary>
        public static event Action<Mob, PlayerId> ServerDefeated;

        /// <summary>Every PC: what it's doing, as the host last said.</summary>
        public MobState State => (MobState)_state.Value;

        /// <summary>Every PC: which of its <see cref="MobDefinition.Attacks"/> it's winding up or making, or -1.</summary>
        public int CurrentAttack => _attack.Value - 1;

        /// <summary>Every PC: who it's fighting and how close each hero is to pulling it, as the host last said (5.3).</summary>
        public MobThreatView ThreatView => _threat.Value;

        /// <summary>Host only: its brain, for tests and the debug readout.</summary>
        public MobBrain Brain => _brain;

        /// <summary>Host only: its health.</summary>
        public MobRules Rules => _rules;

        public ulong CreatureId => NetworkObjectId;

        public bool IsTargetable => IsSpawned && State != MobState.Defeated && (_rules == null || !_rules.IsDefeated);

        public float3 Feet => IsServer && _placed ? _body.FeetPosition(definition.Motor) : (float3)transform.position;

        public float3 HalfExtents => definition.Motor.HalfExtents;

        /// <summary>Every PC: its body can still be skinned by <paramref name="hero"/> (they fought it and haven't yet).</summary>
        public bool CanBeSkinnedBy(PlayerId hero)
        {
            if (!IsSpawned || State != MobState.Defeated || !hero.IsValid)
                return false;
            foreach (byte skinner in _skinners)
            {
                if (skinner == hero.Value)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Host only: <paramref name="hero"/> skinned its body (Roadmap 5.7). False if they may not (any more). When nobody
        /// is left to skin it, it goes soon after.
        /// </summary>
        internal bool ServerTakeSkin(PlayerId hero)
        {
            if (!IsServer || !CanBeSkinnedBy(hero))
                return false;
            _skinners.Remove(hero.Value);
            if (_skinners.Count == 0)
                _despawnAt = math.min(_despawnAt, Time.time + defeatedSeconds);
            return true;
        }

        // Roadmap 5.7: everyone who fought it may skin it, if its kind can be; then its body waits for them.
        void ServerOfferSkinning(PlayerId killer)
        {
            ProfessionBook book = _players != null ? _players.Professions : null;
            if (book == null || _rules == null || !book.TryGetNode(GatherNodeKind.Mob, (ushort)definition.Id, out _))
                return;
            LootShares.Fighters(_rules.Threat.Entries, killer, hero => _players.TryGet(hero, out _), _fighters);
            foreach (PlayerId fighter in _fighters)
                _skinners.Add(fighter.Value);
            if (_skinners.Count > 0)
                _despawnAt = Time.time + math.max(defeatedSeconds, skinnableSeconds);
        }

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

        /// <summary>Host only, after <see cref="ServerInitialize"/>: throws it (a slime bursting out of a bigger one).</summary>
        internal void ServerPop(float3 impulse) => VoxelBodyMotor.Shove(ref _body, impulse);

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            CombatCreatures.RaiseSpawned(this);
        }

        public override void OnNetworkDespawn()
        {
            if (State == MobState.Defeated)
                Vanishing?.Invoke(this);
            CombatCreatures.RaiseDespawned(this);
            base.OnNetworkDespawn();
        }

        public TargetDamage ServerDamage(float amount, PlayerId instigator, float threatScale = 1f)
        {
            if (!IsServer || _rules == null)
                return default;
            TargetDamage result = _rules.Hit(amount, instigator, threatScale);
            if (result.Toppled)
            {
                _despawnAt = Time.time + defeatedSeconds;
                _state.Value = (byte)MobState.Defeated;
                ServerOfferSkinning(instigator);
                ServerDefeated?.Invoke(this, instigator);
                ServerSplit();
            }
            if (result.Dealt > 0f)
                CueRpc(result.Toppled ? MobCueKind.Defeated : MobCueKind.Hurt, 0);
            return result;
        }

        public bool ServerStun(float seconds, PlayerId instigator, float threatScale = 1f) =>
            IsServer && _rules != null && _rules.Stun(Time.time, seconds, instigator, threatScale);

        public bool ServerTaunt(float seconds, PlayerId taunter) =>
            IsServer && _rules != null && _rules.Taunt(Time.time, seconds, taunter);

        public ThreatTable ServerThreat => IsServer && _rules != null && !_rules.IsDefeated ? _rules.Threat : null;

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
            GatherPackmates(feet);
            MobIntent intent = _brain.Tick(new MobSenses
            {
                Now = now, Dt = Time.fixedDeltaTime, Feet = feet, Moved = _moved, Heroes = _heroes,
                Self = NetworkObjectId, Allies = _allies,
            });
            foreach (MobEvent e in _brain.Events)
            {
                switch (e.Kind)
                {
                    case MobEventKind.Strike:
                        Strike(e, feet);
                        CueRpc(MobCueKind.Strike, (byte)math.max(0, e.Attack));
                        break;
                    case MobEventKind.Aggro:
                        CallPack(e.Target, feet, now);
                        CueRpc(MobCueKind.Call, 0);
                        break;
                    case MobEventKind.Windup:
                        CueRpc(MobCueKind.Windup, (byte)math.max(0, e.Attack));
                        break;
                    case MobEventKind.Dazed:
                        CueRpc(MobCueKind.Dazed, (byte)math.max(0, e.Attack));
                        break;
                }
            }
            if (now >= _nextThreatView)
                SendThreatView(feet, now);
            if (_state.Value != (byte)_brain.State)
                _state.Value = (byte)_brain.State;
            byte attack = (byte)(_brain.CurrentAttack + 1);
            if (_attack.Value != attack)
                _attack.Value = attack;

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
            if (definition.Hops)
                Hop(ref input, now);
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

        [Rpc(SendTo.ClientsAndHost)]
        void CueRpc(MobCueKind kind, byte attack) => Cued?.Invoke(this, kind, attack);

        /// <summary>
        /// A slime gets about in hops: on the ground it sits for <see cref="MobDefinition.HopPause"/>, then jumps the way
        /// it wants to go, and only moves while it's in the air.
        /// </summary>
        void Hop(ref MotorInput input, float now)
        {
            if (!_body.Grounded)
            {
                _nextHop = now + definition.HopPause;
                return;
            }
            if (math.lengthsq(input.Move) < 0.0001f)
                return;
            if (now < _nextHop)
            {
                input.Move = float2.zero;
                input.Jump = false;
                return;
            }
            input.Jump = true;
        }

        /// <summary>Host: shares who it's fighting and the heroes' shares, if they've changed enough to be worth it.</summary>
        void SendThreatView(float3 feet, float now)
        {
            _nextThreatView = now + ThreatViewEvery;
            PlayerId fighting = _brain.IsFighting ? _brain.Target : PlayerId.None;
            MobThreatView view = MobThreatView.From(_rules.Threat, fighting, new ThreatPick
            {
                Now = now, Heroes = _heroes, Feet = feet, MeleeReach = definition.MeleeReach,
            });
            if (view.Differs(_threat.Value, ThreatViewStep))
                _threat.Value = view;
        }

        /// <summary>Host: mobs of its own kind within its pack radius, for its brain to line up with.</summary>
        void GatherPackmates(float3 feet)
        {
            _allies.Clear();
            _packmates.Clear();
            CombatTargets targets = _players != null ? _players.Targets : null;
            if (targets == null)
                return;
            float radius = definition.PackRadius;
            foreach (ICombatCreature creature in targets.Creatures)
            {
                if (!(creature is Mob other) || other == this || other.definition != definition || other._brain == null ||
                    !other.IsTargetable || math.distance(other.Feet, feet) > radius)
                    continue;
                MobBrain brain = other._brain;
                _packmates.Add(other);
                _allies.Add(new AllySense
                {
                    Id = other.NetworkObjectId, Feet = other.Feet, Target = brain.Target, Fighting = brain.IsFighting,
                    Biting = brain.IsBiting, Resting = brain.IsResting,
                });
            }
        }

        /// <summary>Host: it picked a fight, so its kind within its call radius join in (a wolf pack, a rhino herd).</summary>
        void CallPack(PlayerId hero, float3 feet, float now)
        {
            if (definition.CallRadius <= 0f)
                return;
            foreach (Mob packmate in _packmates)
            {
                if (packmate != null && math.distance(packmate.Feet, feet) <= definition.CallRadius)
                    packmate._brain.Join(hero, now);
            }
        }

        /// <summary>
        /// Host: defeated, it bursts into <see cref="MobDefinition.SplitCount"/> smaller mobs (a slime), thrown out round
        /// it, which go straight after whoever it was fighting.
        /// </summary>
        void ServerSplit()
        {
            MobDefinition into = definition.SplitInto;
            int count = definition.SplitCount;
            if (into == null || into.Prefab == null || count <= 0 || _world == null || !_placed)
                return;
            float3 feet = _body.FeetPosition(definition.Motor);
            PlayerId fighting = _brain.Target.IsValid ? _brain.Target : _rules.LastAttacker;
            float now = Time.time;
            var random = new Unity.Mathematics.Random((uint)(NetworkObjectId * 2654435761UL) | 1u);
            float start = random.NextFloat(0f, 2f * math.PI);
            for (int i = 0; i < count; i++)
            {
                float angle = start + i * 2f * math.PI / count;
                Mob child = Instantiate(into.Prefab, (Vector3)feet, Quaternion.identity);
                child.ServerInitialize(_world, _players, feet, random.NextUInt(1u, uint.MaxValue));
                child.ServerPop(new float3(math.sin(angle) * splitPop.x, splitPop.y, math.cos(angle) * splitPop.x));
                child.NetworkObject.Spawn(destroyWithScene: true);
                child._brain.Join(fighting, now);
            }
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
