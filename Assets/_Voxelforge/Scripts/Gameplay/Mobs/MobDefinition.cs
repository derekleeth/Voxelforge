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
    }
}
