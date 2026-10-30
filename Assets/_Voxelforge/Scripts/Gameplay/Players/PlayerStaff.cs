using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// This player's projectiles and blasts (Roadmap 3.6, 4.1, 4.3): the M.G.R.S.'s Mana Bolts, any other projectile
    /// ability, and ground slams. <see cref="PlayerCaster"/> decides <i>when</i> one goes off; then <b>the host</b>
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

        /// <summary>Host only: <paramref name="ability"/> goes off on the block at <paramref name="hit"/> (a ground slam).</summary>
        public void ServerStrikeAt(AbilityDefinition ability, in VoxelHit hit)
        {
            VoxelWorldManager world = _player.WorldManager;
            if (!IsServer || !IsSpawned || ability == null || world == null || world.World == null)
                return;
            ushort blockId = world.World.GetVoxel(hit.Block).BlockId;
            VoxelBatchResult result = Apply(world, ability, hit.Point, hit.Block, ability.BlockDamage, out float dealt);
            _cues.ServerSend(AbilityCue.Blast(ability.Id, hit.Point, (float3)hit.Normal, blockId, result.Broken, dealt));
        }

        void FixedUpdate()
        {
            if (!IsServer || !IsSpawned || _flight.Count == 0)
                return;
            VoxelWorldManager world = _player.WorldManager;
            _ended.Clear();
            _flight.Step(world != null ? world.World : null, Time.fixedDeltaTime, _ended);
            foreach (ManaBoltEnd end in _ended)
                Strike(world, end);
        }

        void Strike(VoxelWorldManager world, ManaBoltEnd end)
        {
            ushort blockId = BlockIds.Air;
            int broken = 0;
            float dealt = 0f;
            if (end.HitBlock && world != null && world.World != null)
            {
                blockId = world.World.GetVoxel(end.Hit.Block).BlockId;
                AbilityBook book = _caster != null ? _caster.Book : null;
                AbilityDefinition ability = null;
                book?.TryGet(end.AbilityId, out ability);
                broken = Apply(world, ability, end.Point, end.Hit.Block, end.Damage, out dealt).Broken;
            }
            _cues.ServerSend(AbilityCue.End(end, blockId, broken, dealt));
        }

        VoxelBatchResult Apply(VoxelWorldManager world, AbilityDefinition ability, float3 point, int3 block, float damage,
            out float dealt)
        {
            bool area = ability != null && ability.AoeRadius > 0f;
            dealt = HurtTargets(ability, point, block, area);

            _damage.Clear();
            // A spell hurts a Training Dummy instead of breaking it (a crater already passes it by: it's blast-proof).
            CombatTargets targets = Targets;
            if (area || targets == null || !targets.IsTarget(block))
                AbilityImpact.Resolve(ability, point, block, damage, _damage);
            VoxelBatchResult result = world.RequestDamageVoxels(_damage, _player.PlayerId, area);
            if (logHits)
                Debug.Log($"[Voxelforge] {_player.PlayerId}'s {(ability != null ? ability.DisplayName : "bolt")} hit {block}: " +
                          $"{result.Broken} broken, {result.Damaged} damaged, {result.Skipped} untouched, {dealt:0} to targets.", this);
            return result;
        }

        CombatTargets Targets => _player.Registry != null ? _player.Registry.Targets : null;

        /// <summary>
        /// Host: the ability's creature damage (Roadmap 4.6) to the target it hit, or, for a crater, to every target in it:
        /// full in the middle, falling to <see cref="AbilityDefinition.CraterFalloff"/> at the edge like the blocks.
        /// Returns the total damage dealt.
        /// </summary>
        float HurtTargets(AbilityDefinition ability, float3 point, int3 block, bool area)
        {
            CombatTargets targets = Targets;
            if (targets == null || ability == null || ability.CreatureDamage <= 0f)
                return 0f;
            if (!area)
                return targets.IsTarget(block)
                    ? targets.ServerDamage(new CombatTarget(block), ability.CreatureDamage, _player.PlayerId)
                    : 0f;

            float dealt = 0f;
            _found.Clear();
            targets.FindAll(point, ability.AoeRadius, _found);
            foreach (CombatTarget target in _found)
            {
                float t = math.saturate(math.distance(target.Position, point) / ability.AoeRadius);
                float amount = ability.CreatureDamage * math.lerp(1f, ability.CraterFalloff, t);
                dealt += targets.ServerDamage(target, amount, _player.PlayerId);
            }
            return dealt;
        }
    }
}
