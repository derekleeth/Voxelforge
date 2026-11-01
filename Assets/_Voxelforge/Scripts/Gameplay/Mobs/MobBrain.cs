using System.Collections.Generic;
using Unity.Mathematics;
using Voxelforge.Core;

namespace Voxelforge.Gameplay
{
    /// <summary>What a mob is doing (Roadmap 5.2). Sent as a byte for animation, so only add to the end.</summary>
    public enum MobState : byte
    {
        /// <summary>Standing about at home.</summary>
        Idle,
        /// <summary>Strolling to a spot near home.</summary>
        Wander,
        /// <summary>Going after its target.</summary>
        Chase,
        /// <summary>The telegraph before an attack: a boar pawing the ground.</summary>
        Windup,
        /// <summary>Running in a straight line (<see cref="MobAttackKind.Charge"/>).</summary>
        Charge,
        /// <summary>A moment's pause after an attack.</summary>
        Recover,
        /// <summary>Ran into a wall mid-charge and sees stars.</summary>
        Dazed,
        /// <summary>Too far from home: running back, then healing up (a leash).</summary>
        Return,
        /// <summary>Stunned by a hero (Roadmap 4.7, Hoofquake).</summary>
        Stunned,
        /// <summary>Its health ran out.</summary>
        Defeated,
        /// <summary>Standing and calling its pack before the fight (a wolf's howl, step C).</summary>
        Howl,
        /// <summary>Waiting its turn to attack, circling the hero (a wolf, step C).</summary>
        Circle,
        /// <summary>Hurt and alone: running away (a wolf, step C).</summary>
        Flee,
    }

    /// <summary>Another mob of its own kind nearby, as a mob sees it (Roadmap 5.2 step C).</summary>
    public struct AllySense
    {
        public ulong Id;
        public float3 Feet;
        /// <summary>The hero it's after, or none.</summary>
        public PlayerId Target;
        /// <summary>It's in a fight (<see cref="MobBrain.IsFighting"/>).</summary>
        public bool Fighting;
        /// <summary>It's taken one of the attacking places (<see cref="MobBrain.IsBiting"/>).</summary>
        public bool Biting;
        /// <summary>It's had its turn and is letting the others in (<see cref="MobBrain.IsResting"/>).</summary>
        public bool Resting;
    }

    /// <summary>One hero, as a mob sees them.</summary>
    public struct HeroSense
    {
        public PlayerId Id;
        public float3 Feet;
        /// <summary>Knocked out (Roadmap 5.2): mobs leave them alone.</summary>
        public bool KnockedOut;
    }

    /// <summary>Everything a <see cref="MobBrain"/> knows for one tick.</summary>
    public struct MobSenses
    {
        public float Now;
        public float Dt;
        public float3 Feet;
        /// <summary>How far it really moved sideways in the last tick, in metres: a charge into a wall moves nowhere.</summary>
        public float Moved;
        public IReadOnlyList<HeroSense> Heroes;
        /// <summary>Its own id, so packmates agree on who goes first.</summary>
        public ulong Self;
        /// <summary>Mobs of its own kind within its <see cref="MobDefinition.PackRadius"/>, or null.</summary>
        public IReadOnlyList<AllySense> Allies;
    }

    /// <summary>How a mob wants to move this tick.</summary>
    public enum MobMove : byte
    {
        Stand,
        /// <summary>Along a nav-grid path to <see cref="MobIntent.Destination"/>.</summary>
        PathTo,
        /// <summary>Straight along <see cref="MobIntent.Direction"/>, whatever is in the way (a charge).</summary>
        Straight,
    }

    /// <summary>What a <see cref="MobBrain"/> wants its body to do this tick.</summary>
    public struct MobIntent
    {
        public MobMove Move;
        public float3 Destination;
        public float2 Direction;
        /// <summary>Times its walking speed (<see cref="Voxelforge.Voxels.MotorInput.SpeedScale"/>).</summary>
        public float SpeedScale;
        /// <summary>Turn to face <see cref="FaceToward"/> instead of where it's going.</summary>
        public bool Face;
        public float3 FaceToward;
    }

    /// <summary>Something a mob did this tick that the world should hear about.</summary>
    public enum MobEventKind : byte
    {
        /// <summary>
        /// It picked a fight with <see cref="MobEvent.Target"/>. Idle mobs of its kind within its
        /// <see cref="MobDefinition.CallRadius"/> join in (<see cref="MobBrain.Join"/>).
        /// </summary>
        Aggro,
        /// <summary>It started winding up attack <see cref="MobEvent.Attack"/> at <see cref="MobEvent.Target"/>.</summary>
        Windup,
        /// <summary>Attack <see cref="MobEvent.Attack"/> hit <see cref="MobEvent.Target"/>: the host hurts and shoves them.</summary>
        Strike,
        /// <summary>It charged into a wall.</summary>
        Dazed,
        /// <summary>It got home from a leash and healed up.</summary>
        Returned,
        /// <summary>Hurt and alone, it ran away from <see cref="MobEvent.Target"/>.</summary>
        Fled,
    }

    public struct MobEvent
    {
        public MobEventKind Kind;
        public PlayerId Target;
        /// <summary>Which of the definition's <see cref="MobDefinition.Attacks"/>.</summary>
        public int Attack;
        /// <summary>Which way the attack was going, for knockback (a charge's line, or from the mob to its target).</summary>
        public float2 Direction;
    }

    /// <summary>
    /// What one mob decides to do, tick by tick (Roadmap 5.2): wander near home, pick a fight, chase, wind up and attack,
    /// charge, give up and go home. Plain C#, so every state change is tested without a scene. <see cref="Mob"/> runs
    /// one per mob on the host: it hands in what the mob senses (<see cref="MobSenses"/>), moves the body the way the
    /// <see cref="MobIntent"/> says, and carries out its <see cref="Events"/> (a <see cref="MobEventKind.Strike"/>
    /// hurts a hero). Targets are always heroes by <see cref="PlayerId"/> (CLAUDE.md Law #1).
    /// <para>
    /// Roadmap 5.3: once it's fighting, it goes after the top of its <see cref="MobRules.Threat"/> table, changing its
    /// mind only between attacks (never mid-windup or mid-charge), so a tank can hold it while the others cast.
    /// </para>
    /// <para>
    /// Step C adds packs and herds: a mob calls its kind into its fights (<see cref="Join"/>), a wolf howls first, only
    /// <see cref="MobDefinition.MaxBiters"/> attack one hero at once while the rest circle and take turns, a wolf alone
    /// and hurt runs away, and a rhino calms down when nobody has hit it for a while.
    /// </para>
    /// </summary>
    public sealed class MobBrain
    {
        /// <summary>A charge hits a hero whose feet come this close, in metres.</summary>
        public const float ChargeHitRadius = 1.3f;

        /// <summary>It only charges at a hero standing at most this much higher or lower, in metres: a charge runs on the level.</summary>
        public const float ChargeMaxRise = 1.5f;

        /// <summary>A melee attack still lands if the target stepped this much past its range during the windup.</summary>
        public const float MeleeGrace = 0.75f;

        /// <summary>Seconds it pauses after an attack before chasing again.</summary>
        public const float RecoverSeconds = 0.5f;

        /// <summary>A charge that moves less than this share of its speed for <see cref="StuckSeconds"/> hit a wall.</summary>
        const float StuckShare = 0.25f;
        const float StuckSeconds = 0.1f;

        /// <summary>It's home from a leash this close to where it spawned, in metres.</summary>
        public const float HomeDistance = 1.5f;

        /// <summary>A leash that can't get home in this long gives up and makes where it stands home.</summary>
        public const float ReturnGiveUpSeconds = 15f;

        /// <summary>A pack biter steps out to let a circling packmate in after this many strikes.</summary>
        public const int BitesPerTurn = 2;

        /// <summary>Seconds a biter that stepped out waits before it may bite again.</summary>
        public const float TurnRestSeconds = 2.5f;

        /// <summary>How fast the ones waiting their turn circle the hero, in degrees per second.</summary>
        public const float CircleDegreesPerSecond = 35f;

        /// <summary>How far a fleeing mob runs, in metres, and for at most how long.</summary>
        public const float FleeDistance = 18f;
        public const float FleeSeconds = 6f;

        readonly MobDefinition _mob;
        readonly MobRules _rules;
        readonly float[] _readyAt;
        Random _random; // A struct: not readonly, or every draw would come from a copy.
        readonly List<MobEvent> _events = new List<MobEvent>();

        float3 _home;
        float _stateUntil;
        float _stateSince;
        int _attack = -1;
        float3 _wanderTo;
        float2 _chargeDirection;
        float _charged;
        float _stuck;
        int _seenHits;
        float _provokedAt;
        int _bites;
        float _restUntil = float.NegativeInfinity;
        float _circleAngle;
        float3 _fleeTo;
        float _lastTick;

        public MobBrain(MobDefinition mob, MobRules rules, float3 home, uint seed, float now = 0f)
        {
            _mob = mob;
            _rules = rules;
            _home = home;
            _random = new Random(seed == 0 ? 1u : seed);
            _readyAt = new float[mob.Attacks.Length];
            Enter(MobState.Idle, now, now + _random.NextFloat(1f, 4f));
        }

        public MobState State { get; private set; }

        /// <summary>The hero it's after, or none.</summary>
        public PlayerId Target { get; private set; }

        /// <summary>Where it spawned: it wanders near here, and runs back here when it's pulled too far.</summary>
        public float3 Home => _home;

        /// <summary>The attack it's winding up or charging with, or -1.</summary>
        public int CurrentAttack => _attack;

        /// <summary>What it did during the last <see cref="Tick"/>.</summary>
        public IReadOnlyList<MobEvent> Events => _events;

        /// <summary>It's in a fight: calling, chasing, circling or attacking.</summary>
        public bool IsFighting => State == MobState.Chase || State == MobState.Windup || State == MobState.Charge ||
                                  State == MobState.Recover || State == MobState.Dazed || State == MobState.Howl ||
                                  State == MobState.Circle;

        /// <summary>It's taken one of its pack's attacking places on its target (not calling or circling).</summary>
        public bool IsBiting => State == MobState.Chase || State == MobState.Windup || State == MobState.Charge ||
                                State == MobState.Recover || State == MobState.Dazed;

        /// <summary>It's had its turn at attacking (as of the last <see cref="Tick"/>) and is letting a packmate in.</summary>
        public bool IsResting => _lastTick < _restUntil;

        /// <summary>
        /// A packmate called (Roadmap 5.2 step C): if it's idling or wandering, it goes after <paramref name="hero"/> too,
        /// without calling again. Returns whether it joined.
        /// </summary>
        public bool Join(PlayerId hero, float now)
        {
            if (!hero.IsValid || _rules.IsDefeated || (State != MobState.Idle && State != MobState.Wander))
                return false;
            Target = hero;
            _provokedAt = now;
            _rules.Threat.Add(hero, ThreatRules.NoticeThreat);
            Enter(MobState.Chase, now, now);
            return true;
        }

        public MobIntent Tick(in MobSenses senses)
        {
            _events.Clear();
            float now = senses.Now;
            _lastTick = now;
            ForgetAbsent(senses);

            if (_rules.IsDefeated)
            {
                if (State != MobState.Defeated)
                    Enter(MobState.Defeated, now, float.PositiveInfinity);
                return Stand();
            }
            if (_rules.IsStunned(now))
            {
                if (State != MobState.Stunned)
                    Enter(MobState.Stunned, now, _rules.StunnedUntil);
                return Stand();
            }
            if (State == MobState.Stunned)
                Enter(Target.IsValid ? MobState.Chase : MobState.Idle, now, now);

            // A new hit: go after whoever did it, unless it's busy fighting someone or heading home.
            if (_rules.HitCount != _seenHits)
            {
                _seenHits = _rules.HitCount;
                _provokedAt = now;
                if (State != MobState.Return && State != MobState.Flee && !TryGetTarget(senses, out _) &&
                    TryFind(senses, _rules.LastAttacker, out HeroSense attacker) && !attacker.KnockedOut)
                    StartFight(attacker.Id, now);
            }

            if (IsFighting)
            {
                // Hurt with no packmate fighting nearby: run for it.
                if (_mob.FleeBelowShare > 0f && _rules.Share < _mob.FleeBelowShare && !HasFightingPackmate(senses))
                    StartFleeing(senses, now);
                // Nobody has hit it for a while: it calms down (a rhino).
                else if (_mob.CalmSeconds > 0f && now - _provokedAt > _mob.CalmSeconds)
                    GoHome(now);
                // Pulled too far from home: give up and run back (a leash).
                else if (math.distance(senses.Feet.xz, _home.xz) > _mob.LeashRadius)
                    GoHome(now);
            }

            switch (State)
            {
                case MobState.Idle:
                    if (TryAggro(senses, now))
                        return Fight(senses, now);
                    if (now >= _stateUntil)
                    {
                        float angle = _random.NextFloat(0f, 2f * math.PI);
                        float distance = _random.NextFloat(0.3f, 1f) * _mob.WanderRadius;
                        _wanderTo = _home + new float3(math.sin(angle) * distance, 0f, math.cos(angle) * distance);
                        Enter(MobState.Wander, now, now + 10f);
                    }
                    return Stand();

                case MobState.Wander:
                    if (TryAggro(senses, now))
                        return Fight(senses, now);
                    if (now >= _stateUntil || math.distance(senses.Feet.xz, _wanderTo.xz) < 1f)
                    {
                        Enter(MobState.Idle, now, now + _random.NextFloat(3f, 7f));
                        return Stand();
                    }
                    return new MobIntent { Move = MobMove.PathTo, Destination = _wanderTo, SpeedScale = 1f };

                case MobState.Chase:
                case MobState.Circle:
                case MobState.Howl:
                    return Fight(senses, now);

                case MobState.Flee:
                    if (now >= _stateUntil || math.distance(senses.Feet.xz, _fleeTo.xz) < HomeDistance)
                    {
                        GoHome(now);
                        return new MobIntent { Move = MobMove.PathTo, Destination = _home, SpeedScale = _mob.RunSpeed };
                    }
                    return new MobIntent { Move = MobMove.PathTo, Destination = _fleeTo, SpeedScale = _mob.RunSpeed };

                case MobState.Windup:
                    return Windup(senses, now);

                case MobState.Charge:
                    return Charge(senses, now);

                case MobState.Recover:
                case MobState.Dazed:
                    if (now >= _stateUntil)
                        return Chase(senses, now);
                    return TryGetTarget(senses, out HeroSense watched) ? FaceOnly(watched.Feet) : Stand();

                case MobState.Return:
                    if (math.distance(senses.Feet.xz, _home.xz) < HomeDistance || now - _stateSince > ReturnGiveUpSeconds)
                    {
                        if (now - _stateSince > ReturnGiveUpSeconds)
                            _home = senses.Feet;
                        _rules.Reset();
                        _seenHits = _rules.HitCount;
                        Add(MobEventKind.Returned, PlayerId.None);
                        Enter(MobState.Idle, now, now + _random.NextFloat(2f, 5f));
                        return Stand();
                    }
                    return new MobIntent { Move = MobMove.PathTo, Destination = _home, SpeedScale = _mob.RunSpeed };

                default:
                    return Stand();
            }
        }

        /// <summary>A call to arms (step C): howling until it's done, then the chase.</summary>
        MobIntent Fight(in MobSenses senses, float now)
        {
            if (State == MobState.Howl && now < _stateUntil)
                return TryGetTarget(senses, out HeroSense heard) ? FaceOnly(heard.Feet) : Stand();
            return Chase(senses, now);
        }

        MobIntent Chase(in MobSenses senses, float now)
        {
            Retarget(senses, now);
            if (!TryGetTarget(senses, out HeroSense target) && !TryRetarget(senses, now, out target))
            {
                GoHome(now);
                return new MobIntent { Move = MobMove.PathTo, Destination = _home, SpeedScale = _mob.RunSpeed };
            }
            float distance = math.distance(senses.Feet, target.Feet);
            if (!MayAttack(senses, target))
                return Circle(senses, target, now);
            if (State != MobState.Chase)
                Enter(MobState.Chase, now, now);

            MobAttack[] attacks = _mob.Attacks;
            for (int i = 0; i < attacks.Length; i++)
            {
                if (now < _readyAt[i] || distance < attacks[i].MinRange || distance > attacks[i].Range)
                    continue;
                if (attacks[i].Kind == MobAttackKind.Charge && math.abs(target.Feet.y - senses.Feet.y) > ChargeMaxRise)
                    continue;
                _attack = i;
                _readyAt[i] = now + attacks[i].Cooldown;
                // A charge's line is fixed when the telegraph starts, so a hero who sees it coming can step aside.
                _chargeDirection = math.normalizesafe(target.Feet.xz - senses.Feet.xz, new float2(0f, 1f));
                Enter(MobState.Windup, now, now + attacks[i].Windup);
                Add(MobEventKind.Windup, target.Id, i, _chargeDirection);
                return FaceOnly(target.Feet);
            }

            // Close enough to bite: stand and face them until an attack is ready.
            if (distance <= _mob.MeleeReach * 0.8f)
                return FaceOnly(target.Feet);
            return new MobIntent { Move = MobMove.PathTo, Destination = target.Feet, SpeedScale = _mob.RunSpeed };
        }

        MobIntent Windup(in MobSenses senses, float now)
        {
            MobAttack attack = _mob.Attacks[_attack];
            bool hasTarget = TryGetTarget(senses, out HeroSense target);
            if (now < _stateUntil)
                return attack.Kind == MobAttackKind.Charge
                    ? FaceOnly(senses.Feet + new float3(_chargeDirection.x, 0f, _chargeDirection.y))
                    : hasTarget ? FaceOnly(target.Feet) : Stand();

            if (attack.Kind == MobAttackKind.Charge)
            {
                _charged = 0f;
                _stuck = 0f;
                Enter(MobState.Charge, now, now);
                return Straight();
            }

            if (hasTarget && math.distance(senses.Feet, target.Feet) <= attack.Range + MeleeGrace)
            {
                Add(MobEventKind.Strike, target.Id, _attack, math.normalizesafe(target.Feet.xz - senses.Feet.xz));
                // A pack takes turns: after a couple of bites it steps out and lets a circling packmate in.
                if (_mob.MaxBiters > 0 && ++_bites >= BitesPerTurn)
                {
                    _bites = 0;
                    _restUntil = now + RecoverSeconds + TurnRestSeconds;
                }
            }
            Enter(MobState.Recover, now, now + RecoverSeconds);
            return hasTarget ? FaceOnly(target.Feet) : Stand();
        }

        MobIntent Charge(in MobSenses senses, float now)
        {
            MobAttack attack = _mob.Attacks[_attack];
            _charged += senses.Moved;

            // Whoever is in its way gets bowled over, not just its target.
            if (senses.Heroes != null)
            {
                foreach (HeroSense hero in senses.Heroes)
                {
                    if (hero.KnockedOut || math.abs(hero.Feet.y - senses.Feet.y) > 2f ||
                        math.distance(hero.Feet.xz, senses.Feet.xz) > ChargeHitRadius)
                        continue;
                    Add(MobEventKind.Strike, hero.Id, _attack, _chargeDirection);
                    Enter(MobState.Recover, now, now + RecoverSeconds);
                    return Stand();
                }
            }

            // Barely moving after the first moment: it ran into something.
            float expected = _mob.Motor.WalkSpeed * attack.ChargeSpeed * senses.Dt;
            if (now - _stateSince > senses.Dt * 1.5f && senses.Moved < expected * StuckShare)
            {
                _stuck += senses.Dt;
                if (_stuck >= StuckSeconds)
                {
                    Add(MobEventKind.Dazed, PlayerId.None, _attack, _chargeDirection);
                    Enter(MobState.Dazed, now, now + attack.DazedSeconds);
                    return Stand();
                }
            }
            else
            {
                _stuck = 0f;
            }

            if (_charged >= attack.ChargeDistance)
            {
                Enter(MobState.Recover, now, now + RecoverSeconds);
                return Stand();
            }
            return Straight();
        }

        MobIntent Straight() => new MobIntent
        {
            Move = MobMove.Straight, Direction = _chargeDirection, SpeedScale = _mob.Attacks[_attack].ChargeSpeed,
        };

        static MobIntent Stand() => new MobIntent { Move = MobMove.Stand };

        static MobIntent FaceOnly(float3 toward) => new MobIntent { Move = MobMove.Stand, Face = true, FaceToward = toward };

        /// <summary>An aggressive mob picks a fight with the nearest hero in its aggro radius.</summary>
        bool TryAggro(in MobSenses senses, float now)
        {
            if (_mob.Temperament != MobTemperament.Aggressive || !TryNearest(senses, _mob.AggroRadius, out HeroSense hero))
                return false;
            StartFight(hero.Id, now);
            return true;
        }

        /// <summary>
        /// Roadmap 5.3: between attacks, it turns on whoever has passed its target's threat by the margin
        /// (<see cref="ThreatTable.PickTarget"/>), or on a taunter.
        /// </summary>
        void Retarget(in MobSenses senses, float now)
        {
            PlayerId pick = _rules.Threat.PickTarget(Target, new ThreatPick
            {
                Now = now, Heroes = senses.Heroes, Feet = senses.Feet, MeleeReach = _mob.MeleeReach, Home = _home,
                Leash = _mob.LeashRadius,
            });
            if (pick.IsValid)
                Target = pick;
        }

        /// <summary>Heroes who are knocked out or gone lose all their threat: waking up, they start again.</summary>
        void ForgetAbsent(in MobSenses senses)
        {
            if (senses.Heroes == null)
                return;
            IReadOnlyList<ThreatEntry> entries = _rules.Threat.Entries;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                PlayerId hero = entries[i].Hero;
                if (!TryFind(senses, hero, out HeroSense sense) || sense.KnockedOut)
                    _rules.Threat.Remove(hero);
            }
        }

        /// <summary>Its target is gone or knocked out, with nobody else on its table: the last hero to hit it, or (if aggressive) the nearest one.</summary>
        bool TryRetarget(in MobSenses senses, float now, out HeroSense hero)
        {
            if (TryFind(senses, _rules.LastAttacker, out hero) && !hero.KnockedOut &&
                math.distance(hero.Feet.xz, _home.xz) <= _mob.LeashRadius)
            {
                Target = hero.Id;
                return true;
            }
            if (_mob.Temperament == MobTemperament.Aggressive && TryNearest(senses, _mob.AggroRadius, out hero))
            {
                Target = hero.Id;
                _rules.Threat.Add(hero.Id, ThreatRules.NoticeThreat);
                return true;
            }
            hero = default;
            return false;
        }

        /// <summary>
        /// Step C: whether it may attack <paramref name="target"/> now, or must circle and wait its turn. Packmates on the
        /// same hero line up the same way on every tick, so at most <see cref="MobDefinition.MaxBiters"/> attack:
        /// those already attacking first, then the nearest, then the lowest id.
        /// </summary>
        bool MayAttack(in MobSenses senses, in HeroSense target)
        {
            if (_mob.MaxBiters <= 0 || senses.Allies == null)
                return true;
            bool biting = IsBiting && senses.Now >= _restUntil;
            float distance = math.distance(senses.Feet, target.Feet);
            int ahead = 0;
            bool someoneWaiting = false;
            foreach (AllySense ally in senses.Allies)
            {
                // One that's had its turn isn't in the queue at all.
                if (ally.Target != target.Id || !ally.Fighting || (ally.Resting && !ally.Biting))
                    continue;
                if (!ally.Biting)
                    someoneWaiting = true;
                if (ally.Biting != biting)
                {
                    if (ally.Biting)
                        ahead++;
                    continue;
                }
                float theirs = math.distance(ally.Feet, target.Feet);
                if (theirs < distance || (theirs == distance && ally.Id < senses.Self))
                    ahead++;
            }
            // Its turn is over, but only if a packmate is waiting to take it.
            if (senses.Now < _restUntil && someoneWaiting)
                return false;
            return ahead < _mob.MaxBiters;
        }

        /// <summary>Waiting its turn: circling the hero at <see cref="MobDefinition.CircleRadius"/>, facing them.</summary>
        MobIntent Circle(in MobSenses senses, in HeroSense target, float now)
        {
            if (State != MobState.Circle)
            {
                Enter(MobState.Circle, now, now);
                float2 from = senses.Feet.xz - target.Feet.xz;
                _circleAngle = math.lengthsq(from) > 0.0001f ? math.atan2(from.x, from.y) : _random.NextFloat(0f, 2f * math.PI);
            }
            // Half the pack goes one way round and half the other.
            float way = (senses.Self & 1) == 0 ? 1f : -1f;
            _circleAngle += math.radians(CircleDegreesPerSecond) * senses.Dt * way;
            float radius = _mob.CircleRadius;
            float3 spot = target.Feet + new float3(math.sin(_circleAngle) * radius, 0f, math.cos(_circleAngle) * radius);
            return new MobIntent
            {
                Move = MobMove.PathTo, Destination = spot, SpeedScale = 1f, Face = true, FaceToward = target.Feet,
            };
        }

        bool HasFightingPackmate(in MobSenses senses)
        {
            if (senses.Allies == null)
                return false;
            foreach (AllySense ally in senses.Allies)
            {
                if (ally.Fighting && math.distance(ally.Feet.xz, senses.Feet.xz) <= _mob.PackRadius)
                    return true;
            }
            return false;
        }

        void StartFleeing(in MobSenses senses, float now)
        {
            float2 away = TryGetTarget(senses, out HeroSense from)
                ? math.normalizesafe(senses.Feet.xz - from.Feet.xz, new float2(0f, 1f))
                : math.normalizesafe(senses.Feet.xz - _home.xz, new float2(0f, 1f));
            _fleeTo = senses.Feet + new float3(away.x * FleeDistance, 0f, away.y * FleeDistance);
            Add(MobEventKind.Fled, Target);
            Target = PlayerId.None;
            _attack = -1;
            Enter(MobState.Flee, now, now + FleeSeconds);
        }

        void StartFight(PlayerId hero, float now)
        {
            Target = hero;
            _provokedAt = now;
            _rules.Threat.Add(hero, ThreatRules.NoticeThreat);
            Add(MobEventKind.Aggro, hero);
            // A pack hunter howls first, which gives its packmates (and the hero) a moment.
            if (_mob.CallSeconds > 0f)
                Enter(MobState.Howl, now, now + _mob.CallSeconds);
            else
                Enter(MobState.Chase, now, now);
        }

        void GoHome(float now)
        {
            Target = PlayerId.None;
            _attack = -1;
            Enter(MobState.Return, now, now);
        }

        bool TryGetTarget(in MobSenses senses, out HeroSense hero) =>
            TryFind(senses, Target, out hero) && !hero.KnockedOut;

        static bool TryFind(in MobSenses senses, PlayerId id, out HeroSense hero)
        {
            if (id.IsValid && senses.Heroes != null)
            {
                foreach (HeroSense candidate in senses.Heroes)
                {
                    if (candidate.Id == id)
                    {
                        hero = candidate;
                        return true;
                    }
                }
            }
            hero = default;
            return false;
        }

        static bool TryNearest(in MobSenses senses, float radius, out HeroSense nearest)
        {
            nearest = default;
            float best = radius * radius;
            bool found = false;
            if (senses.Heroes == null)
                return false;
            foreach (HeroSense hero in senses.Heroes)
            {
                float distance = math.distancesq(hero.Feet, senses.Feet);
                if (hero.KnockedOut || distance > best)
                    continue;
                best = distance;
                nearest = hero;
                found = true;
            }
            return found;
        }

        void Enter(MobState state, float now, float until)
        {
            State = state;
            _stateSince = now;
            _stateUntil = until;
        }

        void Add(MobEventKind kind, PlayerId target, int attack = -1, float2 direction = default) =>
            _events.Add(new MobEvent { Kind = kind, Target = target, Attack = attack, Direction = direction });
    }
}
