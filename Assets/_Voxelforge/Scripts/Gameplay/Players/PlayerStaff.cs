using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// This player's projectiles and blasts (Roadmap 3.6, 4.1, 4.3): the M.G.R.S.'s Mana Bolts, any other projectile
    /// ability, ground slams, and stomps that stun (Roadmap 4.7). <see cref="PlayerCaster"/> decides <i>when</i> one goes off; then <b>the host</b>
    /// launches it from the hero's eyes along their aim (<see cref="PlayerController.Aim"/>), moves it
    /// (<see cref="ManaBoltFlight"/>), works out what the hit damages, one block or a whole crater
    /// (<see cref="AbilityImpact"/>), and applies it through <see cref="VoxelWorldManager.RequestDamageVoxels"/> in this
    /// player's name. No position ever comes from a PC (CLAUDE.md Laws #1-#3).
    /// <para>Rules only (Roadmap 4.2): every PC hears <see cref="AbilityCue"/>s for the launch and the hit through
    /// <see cref="PlayerAbilityCues"/>, and draws them itself (<see cref="ManaBoltView"/>).</para>
    /// </summary>
    [RequireComponent(typeof(PlayerController), typeof(PlayerAbilityCues))]
    public sealed class PlayerStaff : NetworkBehaviour
    {
        [Tooltip("Log every bolt that hits, on the host.")]
        [SerializeField] bool logHits;

        readonly ManaBoltFlight _flight = new ManaBoltFlight();
        readonly List<ManaBoltEnd> _ended = new List<ManaBoltEnd>();
        readonly List<VoxelDamageRequest> _damage = new List<VoxelDamageRequest>();
        readonly List<CombatTarget> _found = new List<CombatTarget>();

        PlayerController _player;
        PlayerCaster _caster;
        PlayerAbilityCues _cues;

        void Awake()
        {
            _player = GetComponent<PlayerController>();
            _caster = GetComponent<PlayerCaster>();
            _cues = GetComponent<PlayerAbilityCues>();
        }

        public override void OnNetworkDespawn()
        {
            _flight.Clear();
            base.OnNetworkDespawn();
        }

        /// <summary>Host only: launches a bolt of <paramref name="ability"/> (a finished cast, <see cref="PlayerCaster"/>).</summary>
        public void ServerLaunch(AbilityDefinition ability, float3 origin, float3 direction)
        {
            if (IsServer && IsSpawned && _flight.TryLaunch(origin, direction, ability, out ManaBolt bolt))
                _cues.ServerSend(AbilityCue.Launch(bolt));
        }

        /// <summary>
        /// Host only: <paramref name="ability"/> goes off on the block at <paramref name="hit"/> (a ground slam), or on the
        /// target it <paramref name="struck"/> there.
        /// </summary>
        public void ServerStrikeAt(AbilityDefinition ability, in VoxelHit hit, in CombatTarget struck = default)
        {
            VoxelWorldManager world = _player.WorldManager;
            if (!IsServer || !IsSpawned || ability == null || world == null || world.World == null)
                return;
            ushort blockId = world.World.GetVoxel(hit.Block).BlockId;
            VoxelBatchResult result = Apply(world, ability, hit.Point, hit.Block, ability.BlockDamage, struck);
            _cues.ServerSend(AbilityCue.Blast(ability.Id, hit.Point, (float3)hit.Normal, blockId, result.Broken, struck.IsValid));
        }

        /// <summary>
        /// Host only: <paramref name="ability"/> taunts <paramref name="foe"/> (a <see cref="AbilityDelivery.Taunt"/>,
        /// Roadmap 5.3 Oathbrand): it fights only this hero for the ability's Duration. Every PC sees the brand land as a
        /// burst of the spell's sparks and hears its ring on the foe, and the hero's own view says "TAUNTED!".
        /// </summary>
        public void ServerTaunt(AbilityDefinition ability, in CombatTarget foe)
        {
            CombatTargets targets = Targets;
            if (!IsServer || !IsSpawned || ability == null || targets == null ||
                !targets.ServerTaunt(foe, _player.PlayerId, ability.Duration))
                return;
            // An impact on no block at all, so the sparks are all the spell's own gold.
            _cues.ServerSend(AbilityCue.Blast(ability.Id, foe.Position, new float3(0f, 1f, 0f), ushort.MaxValue, onTarget: true));
            _cues.ServerSend(AbilityCue.TauntTarget(ability.Id, foe));
        }

        /// <summary>
        /// Host only: <paramref name="ability"/> stomps the ground at the hero's feet (a <see cref="AbilityDelivery.Quake"/>,
        /// Roadmap 4.7 Hoofquake). Every target within its radius is hurt, more near the middle, and stunned for its
        /// duration. The ground shakes but isn't damaged.
        /// </summary>
        public void ServerQuake(AbilityDefinition ability)
        {
            VoxelWorldManager world = _player.WorldManager;
            if (!IsServer || !IsSpawned || ability == null)
                return;
            float3 feet = transform.position;
            int3 under = (int3)math.floor(feet - new float3(0f, 0.5f, 0f));
            ushort blockId = world != null && world.World != null ? world.World.GetVoxel(under).BlockId : BlockIds.Air;
            // The burst first, so the numbers and stuns land on a ground that's already shaking.
            _cues.ServerSend(AbilityCue.Blast(ability.Id, feet, new float3(0f, 1f, 0f), blockId));

            CombatTargets targets = Targets;
            if (targets == null || ability.AoeRadius <= 0f)
                return;
            // Measured from the hero's middle, so dummies a step up or down are still in reach.
            float3 middle = feet + new float3(0f, 0.9f, 0f);
            HurtTargets(ability, middle, default, area: true);
            if (ability.Duration <= 0f)
                return;
            _found.Clear();
            targets.FindAll(middle, ability.AoeRadius, _found);
            foreach (CombatTarget target in _found)
            {
                if (targets.ServerStun(target, ability.Duration, _player.PlayerId, ability.ThreatScale))
                    _cues.ServerSend(AbilityCue.StunTarget(ability.Id, target));
            }
        }

        void FixedUpdate()
        {
            if (!IsServer || !IsSpawned || _flight.Count == 0)
                return;
            VoxelWorldManager world = _player.WorldManager;
            _ended.Clear();
            _flight.Step(world != null ? world.World : null, Time.fixedDeltaTime, _ended, Targets);
            foreach (ManaBoltEnd end in _ended)
                Strike(world, end);
        }

        void Strike(VoxelWorldManager world, ManaBoltEnd end)
        {
            ushort blockId = BlockIds.Air;
            int broken = 0;
            if (end.HitBlock && world != null && world.World != null)
            {
                blockId = world.World.GetVoxel(end.Hit.Block).BlockId;
                AbilityBook book = _caster != null ? _caster.Book : null;
                AbilityDefinition ability = null;
                book?.TryGet(end.AbilityId, out ability);
                broken = Apply(world, ability, end.Point, end.Hit.Block, end.Damage, end.Target).Broken;
            }
            _cues.ServerSend(AbilityCue.End(end, blockId, broken));
        }

        VoxelBatchResult Apply(VoxelWorldManager world, AbilityDefinition ability, float3 point, int3 block, float damage,
            in CombatTarget struck)
        {
            bool area = ability != null && ability.AoeRadius > 0f;
            bool onTarget = struck.IsValid;
            float dealt = HurtTargets(ability, point, struck, area);

            // A spell that hits a target spends itself on it: no crater, and the Training Dummy isn't broken
            // (Roadmap 4.6 play-test: blasts dug a pit round the dummy). Craters only happen on the ground.
            _damage.Clear();
            if (!onTarget)
                AbilityImpact.Resolve(ability, point, block, damage, _damage);
            VoxelBatchResult result = world.RequestDamageVoxels(_damage, _player.PlayerId, area);
            if (logHits)
                Debug.Log($"[Voxelforge] {_player.PlayerId}'s {(ability != null ? ability.DisplayName : "bolt")} hit {block}: " +
                          $"{result.Broken} broken, {result.Damaged} damaged, {result.Skipped} untouched, {dealt:0} to targets.", this);
            return result;
        }

        CombatTargets Targets => _player.Registry != null ? _player.Registry.Targets : null;

        /// <summary>
        /// Host: the ability's creature damage (Roadmap 4.6) to the target it hit, or, with an area, to every target in it:
        /// full on the one it struck and in the middle, falling to <see cref="AbilityDefinition.CraterFalloff"/> at the
        /// edge like the blocks.
        /// Every target hurt gets its own <see cref="AbilityCueKind.TargetHit"/> cue. Returns the total damage dealt.
        /// </summary>
        float HurtTargets(AbilityDefinition ability, float3 point, in CombatTarget struck, bool area)
        {
            CombatTargets targets = Targets;
            if (targets == null || ability == null || ability.CreatureDamage <= 0f)
                return 0f;
            if (!area)
                return struck.IsValid ? Hurt(targets, ability, struck, ability.CreatureDamage) : 0f;

            float dealt = 0f;
            _found.Clear();
            targets.FindAll(point, ability.AoeRadius, _found);
            TargetKey struckKey = struck.Key;
            foreach (CombatTarget target in _found)
            {
                // The target the spell struck takes it all; others in the blast less, the further they are.
                float t = struckKey.IsValid && target.Key == struckKey ? 0f
                    : math.saturate(math.distance(target.Position, point) / ability.AoeRadius);
                dealt += Hurt(targets, ability, target, ability.CreatureDamage * math.lerp(1f, ability.CraterFalloff, t));
            }
            return dealt;
        }

        float Hurt(CombatTargets targets, AbilityDefinition ability, in CombatTarget target, float amount)
        {
            TargetDamage result = targets.ServerDamage(target, amount, _player.PlayerId, ability.ThreatScale);
            if (result.Dealt > 0f)
                _cues.ServerSend(AbilityCue.HitTarget(ability.Id, target, result));
            return result.Dealt;
        }
    }
}
