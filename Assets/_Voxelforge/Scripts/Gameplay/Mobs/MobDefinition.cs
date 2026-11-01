using System;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>Whether a mob picks fights (Roadmap 5.2).</summary>
    public enum MobTemperament : byte
    {
        /// <summary>Attacks any hero who comes within its aggro radius.</summary>
        Aggressive,
        /// <summary>Leaves heroes alone until one hurts it.</summary>
        Neutral,
    }

    /// <summary>How a <see cref="MobAttack"/> reaches its target.</summary>
    public enum MobAttackKind : byte
    {
        /// <summary>Winds up beside the hero, then strikes if they're still in reach.</summary>
        Melee,
        /// <summary>
        /// Winds up (the telegraph), then runs in a straight line at <see cref="MobAttack.ChargeSpeed"/> times its walking
        /// speed. It hits the first hero it reaches; a wall stops it and leaves it dazed.
        /// </summary>
        Charge,
    }

    /// <summary>One attack a mob can make (Roadmap 5.2). Data only: <see cref="MobBrain"/> decides when.</summary>
    [Serializable]
    public struct MobAttack
    {
        public MobAttackKind Kind;

        [Tooltip("Damage to the hero it hits. Mobs can knock heroes out.")]
        [Min(0f)] public float Damage;

        [Tooltip("It only starts this attack with its target at least this far away, in metres.")]
        [Min(0f)] public float MinRange;

        [Tooltip("And at most this far away, in metres. A melee hit also needs the target this close when it lands.")]
        [Min(0.5f)] public float Range;

        [Tooltip("Seconds of warning (the telegraph) before it strikes or sets off.")]
        [Min(0f)] public float Windup;

        [Tooltip("Seconds before it can use this attack again, from when it starts.")]
        [Min(0f)] public float Cooldown;

        [Tooltip("How hard it shoves the hero away, in metres per second.")]
        [Min(0f)] public float Knockback;

        [Tooltip("How hard it throws the hero up, in metres per second.")]
        [Min(0f)] public float Lift;

        [Header("Charge only")]
        [Tooltip("How many times its walking speed it charges at.")]
        [Min(1f)] public float ChargeSpeed;

        [Tooltip("The furthest it runs before giving up, in metres.")]
        [Min(1f)] public float ChargeDistance;

        [Tooltip("Seconds it stands dazed after running into a wall.")]
        [Min(0f)] public float DazedSeconds;
    }

    /// <summary>What a mob sounds like (Roadmap 5.2 step G). <see cref="MobFoley"/> plays them; any can be left empty.</summary>
    [Serializable]
    public struct MobSounds
    {
        [Tooltip("Picking a fight: a grunt, a howl, a bellow.")]
        public AudioClip Call;

        [Tooltip("Winding up a melee attack.")]
        public AudioClip Attack;

        [Tooltip("Winding up a charge: the boar's squeal.")]
        public AudioClip Charge;

        [Tooltip("An attack landing on a hero.")]
        public AudioClip Strike;

        public AudioClip Hurt;

        public AudioClip Death;

        [Tooltip("Plays every clip this much higher (a small slime squeaks).")]
        [Range(0.5f, 2f)] public float Pitch;
    }

    /// <summary>Where the biome spawner (Roadmap 5.2 step D) puts a mob.</summary>
    public enum MobSpawnPlace : byte
    {
        /// <summary>The spawner never places it (a slime that only comes from splitting).</summary>
        Nowhere,
        /// <summary>On open ground under the sky.</summary>
        Surface,
        /// <summary>On a cave floor well under the ground.</summary>
        Cave,
    }

    /// <summary>Where and in what numbers the biome spawner places a mob (Roadmap 5.2 step D).</summary>
    [Serializable]
    public struct MobSpawnRule
    {
        public MobSpawnPlace Place;

        [Tooltip("Biomes it lives in, matched against the start of the biome's name (\"Meadows\" matches \"Meadows (Elwynn style)\"). Empty: any biome.")]
        public string[] Biomes;

        [Tooltip("It also lives on any ground above the snow line, whatever the biome.")]
        public bool AboveSnowLine;

        [Tooltip("How many spawn together: a pair, a pack, a herd.")]
        [Min(1)] public int GroupMin;
        [Min(1)] public int GroupMax;

        [Tooltip("How likely it is picked over other mobs that live in the same place.")]
        [Min(0f)] public float Weight;
    }

    /// <summary>
    /// One kind of creature (Roadmap 5.2): its health, body, how it moves, what it does and how it fights. The host runs
    /// it (<see cref="Mob"/>, <see cref="MobBrain"/>), so these numbers are only trusted on the host. Create one with
    /// Assets &gt; Create &gt; Voxelforge &gt; Mob, then add it to the <see cref="MobBook"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxelforge/Mob", fileName = "NewMob")]
    public sealed class MobDefinition : ScriptableObject
    {
        [Tooltip("Permanent ID sent over the network (MobId). Never change or reuse one.")]
        [SerializeField] MobId id;

        [SerializeField] string displayName;

        [Tooltip("The networked body spawned for it. Its Mob component points back at this definition.")]
        [SerializeField] Mob prefab;

        [SerializeField, Min(1f)] float maxHealth = 50f;

        [Tooltip("How it walks: speed, jump, step height and its collision box (2 blocks tall at most, to fit the nav grid).")]
        [SerializeField] MotorSettings motor = MotorSettings.Default;

        [Tooltip("How many times its walking speed it runs at when chasing or going home.")]
        [SerializeField, Min(1f)] float runSpeed = 1.6f;

        [Tooltip("How fast it turns, in degrees per second.")]
        [SerializeField, Min(1f)] float turnSpeed = 540f;

        [Header("Behaviour")]
        [SerializeField] MobTemperament temperament;

        [Tooltip("An aggressive mob attacks heroes this close, in metres.")]
        [SerializeField, Min(0f)] float aggroRadius = 12f;

        [Tooltip("It gives up and runs home when it's this far from where it spawned, in metres.")]
        [SerializeField, Min(4f)] float leashRadius = 30f;

        [Tooltip("When idle, it wanders up to this far from home, in metres.")]
        [SerializeField, Min(0f)] float wanderRadius = 8f;

        [Header("Fighting")]
        [Tooltip("Tried in order: the first one that's ready, with the target in its range, is used.")]
        [SerializeField] MobAttack[] attacks = Array.Empty<MobAttack>();

        [Header("Packs and herds")]
        [Tooltip("When it picks a fight, idle mobs of its kind this close join in, in metres. 0: it fights alone.")]
        [SerializeField, Min(0f)] float callRadius;

        [Tooltip("Seconds it stands and calls (a wolf's howl) before its first chase. 0: it goes straight in.")]
        [SerializeField, Min(0f)] float callSeconds;

        [Tooltip("At most this many of its kind attack one hero at once; the rest circle until it's their turn. 0: no limit.")]
        [SerializeField, Min(0)] int maxBiters;

        [Tooltip("How far from the hero the ones waiting their turn circle, in metres.")]
        [SerializeField, Min(1f)] float circleRadius = 4f;

        [Tooltip("With no packmate fighting nearby, it runs away below this share of its health. 0: it never runs.")]
        [SerializeField, Range(0f, 1f)] float fleeBelowShare;

        [Tooltip("It calms down and goes home this many seconds after it was last hit. 0: it never calms down.")]
        [SerializeField, Min(0f)] float calmSeconds;

        [Header("Body")]
        [Tooltip("It gets about by hopping (a slime): it only moves while in the air.")]
        [SerializeField] bool hops;

        [Tooltip("Seconds it sits between hops.")]
        [SerializeField, Min(0f)] float hopPause = 0.4f;

        [Tooltip("When defeated, it bursts into this many of that kind, which carry on its fight.")]
        [SerializeField] MobDefinition splitInto;

        [SerializeField, Min(0)] int splitCount = 2;

        [Header("Spawning")]
        [Tooltip("Where the biome spawner puts it, and how many at a time.")]
        [SerializeField] MobSpawnRule spawn = new MobSpawnRule { GroupMin = 1, GroupMax = 1, Weight = 1f };

        [Header("Feedback")]
        [Tooltip("Built by Voxelforge > Build Sound Effects.")]
        [SerializeField] MobSounds sounds = new MobSounds { Pitch = 1f };

        public MobId Id => id;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public Mob Prefab => prefab;
        public float MaxHealth => maxHealth;
        public MotorSettings Motor => motor;
        public float RunSpeed => runSpeed;
        public float TurnSpeed => turnSpeed;
        public MobTemperament Temperament => temperament;
        public float AggroRadius => aggroRadius;
        public float LeashRadius => leashRadius;
        public float WanderRadius => wanderRadius;
        public MobAttack[] Attacks => attacks;
        public float CallRadius => callRadius;
        public float CallSeconds => callSeconds;
        public int MaxBiters => maxBiters;
        public float CircleRadius => circleRadius;
        public float FleeBelowShare => fleeBelowShare;
        public float CalmSeconds => calmSeconds;
        public bool Hops => hops;
        public float HopPause => hopPause;
        public MobDefinition SplitInto => splitInto;
        public int SplitCount => splitInto != null ? splitCount : 0;
        public MobSpawnRule Spawn => spawn;
        public MobSounds Sounds => sounds;

        /// <summary>How far round it looks for packmates, in metres.</summary>
        public float PackRadius => callRadius > 0f ? callRadius : 25f;

        /// <summary>The longest any of its attacks reaches: it chases until its target is this close or closer.</summary>
        public float MeleeReach
        {
            get
            {
                float reach = 0f;
                foreach (MobAttack attack in attacks)
                {
                    if (attack.Kind == MobAttackKind.Melee)
                        reach = Mathf.Max(reach, attack.Range);
                }
                return reach > 0f ? reach : 1.5f;
            }
        }

        /// <summary>Builds a definition in code, for tests and tools.</summary>
        internal static MobDefinition Create(MobId id, string displayName, float maxHealth, MotorSettings motor,
            MobTemperament temperament, float aggroRadius, float leashRadius, params MobAttack[] attacks)
        {
            var definition = CreateInstance<MobDefinition>();
            definition.id = id;
            definition.displayName = displayName;
            definition.maxHealth = maxHealth;
            definition.motor = motor;
            definition.temperament = temperament;
            definition.aggroRadius = aggroRadius;
            definition.leashRadius = leashRadius;
            definition.attacks = attacks;
            return definition;
        }

        /// <summary>For tests and tools: makes it a pack hunter (a wolf).</summary>
        internal MobDefinition WithPack(float callRadius, float callSeconds, int maxBiters, float circleRadius, float fleeBelowShare)
        {
            this.callRadius = callRadius;
            this.callSeconds = callSeconds;
            this.maxBiters = maxBiters;
            this.circleRadius = circleRadius;
            this.fleeBelowShare = fleeBelowShare;
            return this;
        }

        /// <summary>For tests and tools: makes it a herd beast that calms down (a rhino).</summary>
        internal MobDefinition WithHerd(float callRadius, float calmSeconds)
        {
            this.callRadius = callRadius;
            this.calmSeconds = calmSeconds;
            return this;
        }

        /// <summary>For tests and tools: where the biome spawner puts it.</summary>
        internal MobDefinition WithSpawn(MobSpawnRule rule)
        {
            spawn = rule;
            return this;
        }

        /// <summary>For tests and tools: it bursts into <paramref name="count"/> of <paramref name="into"/> (a slime).</summary>
        internal MobDefinition WithSplit(MobDefinition into, int count)
        {
            splitInto = into;
            splitCount = count;
            return this;
        }
    }
}
