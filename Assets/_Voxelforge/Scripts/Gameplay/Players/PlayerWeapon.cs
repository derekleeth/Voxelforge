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
        [SerializeField, Range(0.1f, 3f)] float holdSeconds = 1f;

        readonly NetworkVariable<bool> _attacking = new NetworkVariable<bool>();

        PlayerController _player;
        PlayerBlockBag _bag;
        PlayerHands _hands;
        PlayerTargeting _targeting;
        PlayerAbilityCues _cues;
        float _nextSwing;
        float _attackUntil = float.NegativeInfinity;

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

        /// <summary>Host only: the foe to hit: the body the hero aims at (if no block is in the way), else the Tab target in reach.</summary>
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
            if (_targeting != null && _targeting.TryGetTarget(targets, out ICombatCreature creature))
            {
                CombatTarget target = CombatTarget.Of(creature);
                float3 half = creature.HalfExtents;
                if (math.distance(eyes, target.Position) - math.max(half.x, half.z) <= range)
                {
                    foe = target;
                    return true;
                }
            }
            return false;
        }

        void FixedUpdate()
        {
            if (!IsServer || !IsSpawned)
                return;
            float now = Time.time;
            PlayerInputFrame input = _player.ServerInput;
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

        void Swing(ItemDefinition weapon, in CombatTarget foe, float now)
        {
            _nextSwing = now + weapon.Weapon.swingSeconds;
            CombatTargets targets = _player.Registry.Targets;
            float damage = weapon.Weapon.damage * GearRules.PowerScale(_bag.GearStats.power);
            TargetDamage result = targets.ServerDamage(foe, damage, _player.PlayerId);
            if (result.Dealt > 0f && _cues != null)
                _cues.ServerSend(AbilityCue.HitTarget(AbilityIds.WeaponStrike, foe, result));
        }
    }
}
