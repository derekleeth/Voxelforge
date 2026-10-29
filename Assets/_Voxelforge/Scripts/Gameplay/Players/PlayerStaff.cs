using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// This player's projectiles (Roadmap 3.6, 4.1): the M.G.R.S.'s Mana Bolts and any other projectile ability.
    /// <see cref="PlayerCaster"/> decides <i>when</i> one goes off; then <b>the host</b> launches it from the hero's eyes
    /// along their aim (<see cref="PlayerController.Aim"/>), moves it (<see cref="ManaBoltFlight"/>), works out what the
    /// hit damages (<see cref="AbilityImpact"/>) and applies it through <see cref="VoxelWorldManager.RequestDamageVoxel"/>
    /// in this player's name. No position ever comes from a PC (CLAUDE.md Laws #1-#3).
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
            if (end.HitBlock && world != null && world.World != null)
            {
                blockId = world.World.GetVoxel(end.Hit.Block).BlockId;
                AbilityBook book = _caster != null ? _caster.Book : null;
                AbilityDefinition ability = null;
                book?.TryGet(end.AbilityId, out ability);
                _damage.Clear();
                AbilityImpact.Resolve(ability, end, _damage);
                foreach (VoxelDamageRequest request in _damage)
                {
                    VoxelDamageResult result = world.RequestDamageVoxel(request.Block, request.Amount, _player.PlayerId);
                    if (logHits)
                        Debug.Log($"[Voxelforge] {_player.PlayerId}'s bolt hit {request.Block}: {result}.", this);
                }
            }
            _cues.ServerSend(AbilityCue.End(end, blockId));
        }
    }
}
