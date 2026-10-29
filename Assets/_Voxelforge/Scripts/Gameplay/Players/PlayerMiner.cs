using Unity.Netcode;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Mining (Roadmap 3.2, D-015). While the owner holds Mine, <b>the host</b> aims with the controls it received
    /// (<see cref="PlayerController.ServerInput"/>, <see cref="PlayerController.Aim"/>) and wears the block down through
    /// <see cref="VoxelWorldManager.RequestDamageVoxel"/> in this player's name. No block position ever comes from a
    /// PC, so there's nothing to forge (CLAUDE.md Laws #1-#3). Tougher blocks (higher durability) take longer, and the
    /// tool the host agreed this player holds (<see cref="PlayerHands.HeldPower"/>, Roadmap 3.5) multiplies the power by
    /// the block's <see cref="HarvestKind"/>.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerMiner : NetworkBehaviour
    {
        [Tooltip("Mining damage per second with bare hands. A block breaks when its damage reaches its durability " +
                 "(Dirt 20 = 0.5 s, Stone 60 = 1.5 s at 40). The held tool multiplies it (3.5).")]
        [SerializeField, Min(0f)] float miningPower = 40f;

        [Tooltip("Log every block this player breaks, on the host.")]
        [SerializeField] bool logBreaks;

        readonly NetworkVariable<bool> _mining = new NetworkVariable<bool>();

        PlayerController _player;
        PlayerHands _hands;

        /// <summary>Every PC: this player is hitting a block right now, so their tool swings (<see cref="HeldItemView"/>).</summary>
        public bool IsMining => _mining.Value;

        void Awake()
        {
            _player = GetComponent<PlayerController>();
            _hands = GetComponent<PlayerHands>();
        }

        void FixedUpdate()
        {
            if (!IsServer || !IsSpawned || !_player.HasLanded)
                return;

            VoxelDamageResult result = Mine();
            bool mining = result == VoxelDamageResult.Damaged || result == VoxelDamageResult.Broken;
            if (_mining.Value != mining)
                _mining.Value = mining;
        }

        VoxelDamageResult Mine()
        {
            PlayerInputFrame input = _player.ServerInput;
            VoxelWorldManager world = _player.WorldManager;
            if (!input.IsPressed(PlayerButtons.Mine) || world == null)
                return VoxelDamageResult.NoDamage;
            if (_hands != null && _hands.TryGetHeldAbility(out _))
                return VoxelDamageResult.NoDamage; // Mine casts the M.G.R.S.'s ability instead (PlayerCaster, 4.1).
            if (!_player.Aim.TryAim(world.World, transform.position, input, out VoxelHit hit))
                return VoxelDamageResult.NoDamage;

            HarvestKind kind = world.World.Blocks[world.World.GetVoxel(hit.Block).BlockId].Harvest;
            float power = miningPower * (_hands != null ? _hands.HeldPower.For(kind) : 1f);
            VoxelDamageResult result = world.RequestDamageVoxel(hit.Block, power * Time.fixedDeltaTime, _player.PlayerId);
            if (logBreaks && result == VoxelDamageResult.Broken)
                Debug.Log($"[Voxelforge] {_player.PlayerId} broke the block at {hit.Block}.", this);
            return result;
        }
    }
}
