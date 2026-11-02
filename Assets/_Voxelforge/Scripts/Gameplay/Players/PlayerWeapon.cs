using System;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Attacking with the weapon worn in the Main Hand (Roadmap 5.5 step G). <b>The host</b> decides, from the Mine button
    /// and the hero's aim: with a foe in reach (aimed at, or the Tab target), a melee weapon (cudgel, blade, maul) hits it
    /// every <see cref="WeaponStats.swingSeconds"/> instead of mining, and a ranged one (the wand) shoots its ability through
    /// <see cref="PlayerCaster"/>. Aimed at blocks, Mine still mines. Damage goes through <see cref="CombatTargets"/> in
    /// this hero's name (threat, loot and XP follow), scaled by their Power. <b>Every PC</b> sees <see cref="IsAttacking"/>:
    /// the weapon comes off the back into the hand (<see cref="HeldItemView"/>, <see cref="GearView"/>).
    /// </summary>
    [RequireComponent(typeof(PlayerController), typeof(PlayerBlockBag))]
    public sealed class PlayerWeapon : NetworkBehaviour
    {
        [Tooltip("How long the weapon stays in the hand after the last attack, in seconds.")]
        [SerializeField, Range(0.1f, 3f)] float holdSeconds = 1.5f;

        [Tooltip("Seconds from the start of a melee swing to the blow landing (the wind-up the hero's arm shows).")]
        [SerializeField, Range(0f, 0.6f)] float impactDelay = 0.22f;

        [Tooltip("A melee foe off to the side still counts if it's within this many degrees of where the hero looks.")]
        [SerializeField, Range(0f, 180f)] float meleeCone = 75f;

        // Where melee reach is measured from: the hero's chest, to the nearest side of the foe's body.
        const float ChestHeight = 1.2f;

        readonly NetworkVariable<bool> _attacking = new NetworkVariable<bool>();

        PlayerController _player;
        PlayerBlockBag _bag;
        PlayerHands _hands;
        PlayerTargeting _targeting;
        PlayerAbilityCues _cues;
        float _nextSwing;
        float _attackUntil = float.NegativeInfinity;
        // A melee blow on its way down (Roadmap 5.5 step G): it lands impactDelay after the swing starts.
        bool _blowPending;
        float _blowAt;
        CombatTarget _blowFoe;
        float _blowDamage;

        /// <summary>Every PC: the hero started a melee swing lasting this many seconds (<see cref="HeroSwingView"/> animates it).</summary>
        public event Action<float> Swung;

        /// <summary>Every PC: the hero is fighting with their worn weapon, so it's in their hand.</summary>
        public bool IsAttacking => _attacking.Value;

        /// <summary>Every PC: the weapon worn in the Main Hand, or 0.</summary>
        public ushort WornWeapon => _bag.Gear.MainHand;

        void Awake()
        {
            _player = GetComponent<PlayerController>();
            _bag = GetComponent<PlayerBlockBag>();
            _hands = GetComponent<PlayerHands>();
            _targeting = GetComponent<PlayerTargeting>();
            _cues = GetComponent<PlayerAbilityCues>();
        }

        /// <summary>
        /// Host only: Mine is down with a worn weapon and a foe in its reach (or range), so the Mine button attacks this
        /// step instead of mining (<see cref="PlayerMiner"/>).
        /// </summary>
        public bool ServerWantsToAttack(PlayerInputFrame input) =>
            IsServer && input.IsPressed(PlayerButtons.Mine) && TryGetWeapon(out ItemDefinition weapon) &&
            TryFindFoe(input, Reach(weapon), out _);

        /// <summary>
        /// Host only: the worn ranged weapon's ability (the wand's Shard Bolt), when Mine is down and a foe is in its range.
        /// <see cref="PlayerCaster"/> casts it like the M.G.R.S.'s bolt.
        /// </summary>
        public bool ServerTryGetRangedAttack(PlayerInputFrame input, out AbilityDefinition ability)
        {
            ability = null;
            if (!IsServer || !input.IsPressed(PlayerButtons.Mine) || !TryGetWeapon(out ItemDefinition weapon) || !weapon.IsRanged ||
                !TryFindFoe(input, weapon.Ability.Range, out _))
                return false;
            ability = weapon.Ability;
            return true;
        }

        // The worn Main Hand weapon, unless the hero holds something that shoots (the M.G.R.S. wins, as before).
        bool TryGetWeapon(out ItemDefinition weapon)
        {
            weapon = null;
            ushort id = _bag.Gear.MainHand;
            ItemRegistry items = _bag.Items;
            if (id == 0 || items == null || !items.TryGet(id, out weapon) || (_hands != null && _hands.TryGetHeldAbility(out _)))
                return false;
            return weapon.IsRanged || weapon.Weapon.IsMelee;
        }

        static float Reach(ItemDefinition weapon) => weapon.IsRanged ? weapon.Ability.Range : weapon.Weapon.reach;

        /// <summary>
        /// Host only: the foe to hit: the body the hero aims at (if no block is in the way), else the Tab target in reach,
        /// else (melee) the nearest foe in front of the hero within reach of their chest.
        /// </summary>
        bool TryFindFoe(PlayerInputFrame input, float range, out CombatTarget foe)
        {
            foe = default;
            CombatTargets targets = _player.Registry != null ? _player.Registry.Targets : null;
            VoxelWorldManager world = _player.WorldManager;
            if (targets == null || world == null || world.World == null || !_player.HasLanded || _player.IsKnockedOut)
                return false;
            float3 feet = transform.position;
            float3 eyes = _player.Aim.Eyes(feet);
            if (targets.Raycast(eyes, _player.Aim.Direction(input), range, out VoxelHit body, out CombatTarget struck) && struck.IsValid)
            {
                // A wall nearer than the body hides it; a Training Dummy's own block doesn't.
                bool blocked = _player.Aim.TryAim(world.World, feet, input, range, out VoxelHit block) &&
                               block.Distance < body.Distance - 0.05f && !block.Block.Equals(body.Block);
                if (!blocked)
                {
                    foe = struck;
                    return true;
                }
            }
            float3 chest = feet + new float3(0f, ChestHeight, 0f);
            if (_targeting != null && _targeting.TryGetTarget(targets, out ICombatCreature creature) &&
                DistanceToBody(chest, creature) <= range)
            {
                foe = CombatTarget.Of(creature);
                return true;
            }
            // Melee only: mobs look bigger than their bodies and fight from a step away, so anything close in front counts.
            if (range > MeleeSearchLimit || !targets.TryFindNearest(chest, range + 1f, out CombatTarget near) || !near.IsValid ||
                near.Kind != CombatTargetKind.Creature || !targets.TryGetCreature(near.CreatureId, out ICombatCreature nearCreature) ||
                DistanceToBody(chest, nearCreature) > range)
                return false;
            float3 look = _player.Aim.Direction(input);
            float3 toFoe = near.Position - chest;
            look.y = 0f;
            toFoe.y = 0f;
            if (math.lengthsq(toFoe) > 0.01f && math.lengthsq(look) > 0.01f &&
                math.degrees(math.acos(math.clamp(math.dot(math.normalize(look), math.normalize(toFoe)), -1f, 1f))) > meleeCone)
                return false;
            foe = near;
            return true;
        }

        // Melee reaches are a few metres; the wand's range is far longer and keeps to the aim and the Tab target.
        const float MeleeSearchLimit = 6f;

        /// <summary>From <paramref name="from"/> to the nearest point of the creature's body box, in metres.</summary>
        static float DistanceToBody(float3 from, ICombatCreature creature)
        {
            float3 feet = creature.Feet, half = creature.HalfExtents;
            float3 lo = feet - new float3(half.x, 0f, half.z), hi = feet + new float3(half.x, half.y * 2f, half.z);
            return math.distance(from, math.clamp(from, lo, hi));
        }

        void FixedUpdate()
        {
            if (!IsServer || !IsSpawned)
                return;
            float now = Time.time;
            PlayerInputFrame input = _player.ServerInput;
            if (_blowPending && now >= _blowAt)
                LandBlow();
            if (input.IsPressed(PlayerButtons.Mine) && TryGetWeapon(out ItemDefinition weapon))
            {
                if (weapon.IsRanged)
                {
                    // PlayerCaster shoots it; this only keeps the wand in the hand while it does.
                    if (TryFindFoe(input, weapon.Ability.Range, out _))
                        _attackUntil = now + holdSeconds;
                }
                else if (TryFindFoe(input, weapon.Weapon.reach, out CombatTarget foe))
                {
                    _attackUntil = now + holdSeconds;
                    if (now >= _nextSwing)
                        Swing(weapon, foe, now);
                }
            }
            bool attacking = now < _attackUntil;
            if (_attacking.Value != attacking)
                _attacking.Value = attacking;
        }

        // The swing starts now (everyone sees the wind-up); the blow lands impactDelay later, on whatever it was aimed at.
        void Swing(ItemDefinition weapon, in CombatTarget foe, float now)
        {
            float seconds = weapon.Weapon.swingSeconds;
            _nextSwing = now + seconds;
            _blowPending = true;
            _blowAt = now + Mathf.Min(impactDelay, seconds * 0.5f);
            _blowFoe = foe;
            _blowDamage = weapon.Weapon.damage * GearRules.PowerScale(_bag.CombatStats.power);
            SwungRpc(Mathf.Min(0.6f, seconds * 0.9f));
        }

        void LandBlow()
        {
            _blowPending = false;
            CombatTargets targets = _player.Registry != null ? _player.Registry.Targets : null;
            if (targets == null || _player.IsKnockedOut)
                return;
            TargetDamage result = targets.ServerDamage(_blowFoe, _blowDamage, _player.PlayerId);
            if (result.Dealt > 0f && _cues != null)
                _cues.ServerSend(AbilityCue.HitTarget(AbilityIds.WeaponStrike, _blowFoe, result));
        }

        [Rpc(SendTo.Everyone)]
        void SwungRpc(float seconds) => Swung?.Invoke(seconds);
    }
}
