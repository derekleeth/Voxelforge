using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Outlines the block a player aims at (Roadmap 3.1, D-014), which is the block <see cref="PlayerMiner"/> mines.
    /// Presentation only, on the owning PC. Block changes happen on the host: mining in <see cref="PlayerMiner"/> and
    /// building in <see cref="PlayerBuilder"/>, both through <see cref="VoxelWorldManager.RequestModifyVoxel"/>
    /// (CLAUDE.md Laws #2-#3).
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerVoxelEditor : NetworkBehaviour
    {
        [Header("Target outline")]
        [Tooltip("Unlit material for the outline around the aimed-at block. Empty = no outline.")]
        [SerializeField] Material highlightMaterial;

        [Tooltip("How thick the outline's bars are, in metres.")]
        [SerializeField, Range(0.005f, 0.1f)] float highlightThickness = 0.035f;

        PlayerController _player;
        BlockHighlight _highlight;

        void Awake()
        {
            _player = GetComponent<PlayerController>();
        }

        public override void OnNetworkDespawn()
        {
            _highlight?.Dispose();
            _highlight = null;
        }

        public override void OnDestroy()
        {
            _highlight?.Dispose();
            _highlight = null;
            base.OnDestroy();
        }

        void Update()
        {
            if (!IsSpawned || !IsOwner || !_player.HasLanded)
            {
                _highlight?.Show(null);
                return;
            }

            // A remote PC can't see blocks yet (Phase 6), so it has no target.
            bool hasTarget = _player.Aim.TryAim(_player.World, transform.position, _player.OwnerInput, out VoxelHit hit);
            ShowHighlight(hasTarget ? hit.Block : (int3?)null);
        }

        void ShowHighlight(int3? block)
        {
            if (highlightMaterial == null)
                return;
            _highlight ??= new BlockHighlight($"Aim Target ({_player.PlayerId})", highlightMaterial, _player.TeamColor, highlightThickness);
            _highlight.Show(block);
        }
    }
}
