using Unity.Netcode;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Mining (Roadmap 3.2, D-015). While the owner holds Mine, <b>the host</b> aims with the controls it received
    /// (<see cref="PlayerController.ServerInput"/>, <see cref="PlayerController.Aim"/>) and wears the block down through
    /// <see cref="VoxelWorldManager.RequestDamageVoxel"/> in this player's name. No block position ever comes from a
    /// PC, so there's nothing to forge (CLAUDE.md Laws #1-#3). Tougher blocks (higher durability) take longer.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerMiner : NetworkBehaviour
    {
        [Tooltip("Mining damage per second with bare hands. A block breaks when its damage reaches its durability " +
                 "(Dirt 20 = 0.5 s, Stone 60 = 1.5 s at 40). Tools (3.5) will multiply it.")]
        [SerializeField, Min(0f)] float miningPower = 40f;

        [Tooltip("Log every block this player breaks, on the host.")]
        [SerializeField] bool logBreaks;

        PlayerController _player;

        void Awake()
        {
            _player = GetComponent<PlayerController>();
        }

        void FixedUpdate()
        {
            if (!IsServer || !IsSpawned || !_player.HasLanded)
                return;

            PlayerInputFrame input = _player.ServerInput;
            VoxelWorldManager world = _player.WorldManager;
            if (!input.IsPressed(PlayerButtons.Mine) || world == null)
                return;
            if (!_player.Aim.TryAim(world.World, transform.position, input, out VoxelHit hit))
                return;

            VoxelDamageResult result = world.RequestDamageVoxel(hit.Block, miningPower * Time.fixedDeltaTime, _player.PlayerId);
            if (logBreaks && result == VoxelDamageResult.Broken)
                Debug.Log($"[Voxelforge] {_player.PlayerId} broke the block at {hit.Block}.", this);
        }
    }
}
