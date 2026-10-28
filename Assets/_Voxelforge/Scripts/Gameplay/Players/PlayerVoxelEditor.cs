using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// How one player changes blocks (Roadmap 3.1, D-014). The owning PC picks a block and asks with
    /// <see cref="RequestModifyVoxel"/>; the host checks the request came from this player's owner and is within reach,
    /// then hands it to <see cref="VoxelWorldManager.RequestModifyVoxel"/> with <b>this player's</b> ID. The ID never
    /// travels in the message, so a PC can't edit in another player's name (CLAUDE.md Laws #1-#3).
    /// <para>
    /// It also outlines the block the hero aims at (<see cref="PlayerController.Aim"/>), which is the block
    /// <see cref="PlayerMiner"/> mines. Dev placement is temporary; ghost-block placement (3.3) replaces it.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerVoxelEditor : NetworkBehaviour
    {
        [Tooltip("Log every edit request and its result on the host.")]
        [SerializeField] bool logEdits;

        [Header("Temporary dev controls (until 3.3)")]
        [Tooltip("Place: right mouse / left trigger.")]
        [SerializeField] bool devControls = true;

        [Tooltip("The block dev placement puts down.")]
        [SerializeField] ushort devPlaceBlockId = BlockIds.Dirt;

        [Header("Target outline")]
        [Tooltip("Unlit material for the outline around the aimed-at block. Empty = no outline.")]
        [SerializeField] Material highlightMaterial;

        [Tooltip("How thick the outline's bars are, in metres.")]
        [SerializeField, Range(0.005f, 0.1f)] float highlightThickness = 0.035f;

        PlayerController _player;
        PlayerButtons _previousButtons;
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

        /// <summary>Owner only: asks the host to set the block at <paramref name="coord"/> to <paramref name="blockId"/>.</summary>
        public void RequestModifyVoxel(int3 coord, ushort blockId)
        {
            if (!IsOwner)
            {
                Debug.LogError($"Only the owning PC can edit blocks for {_player.PlayerId}.", this);
                return;
            }
            RequestModifyVoxelRpc(new Vector3Int(coord.x, coord.y, coord.z), blockId);
        }

        [Rpc(SendTo.Server)]
        void RequestModifyVoxelRpc(Vector3Int coord, ushort blockId, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
                return;

            VoxelWorldManager world = _player.WorldManager;
            if (world == null || !_player.HasLanded)
                return;

            var block = new int3(coord.x, coord.y, coord.z);
            if (!_player.Aim.InReach(transform.position, block))
            {
                if (logEdits)
                    Debug.Log($"[Voxelforge] {_player.PlayerId} tried to edit {block}, out of reach.", this);
                return;
            }

            VoxelEditResult result = world.RequestModifyVoxel(block, blockId, _player.PlayerId);
            if (logEdits)
                Debug.Log($"[Voxelforge] {_player.PlayerId} set {block} to block {blockId}: {result}.", this);
        }

        void Update()
        {
            if (!IsSpawned || !IsOwner || !_player.HasLanded)
            {
                _highlight?.Show(null);
                return;
            }

            PlayerButtons buttons = _player.OwnerInput.Buttons;
            PlayerButtons pressed = buttons & ~_previousButtons;
            _previousButtons = buttons;

            // A remote PC can't see blocks yet (Phase 6), so it has no target.
            bool hasTarget = _player.Aim.TryAim(_player.World, transform.position, _player.OwnerInput, out VoxelHit hit);
            if (devControls && (pressed & PlayerButtons.DevPlace) != 0 && hasTarget && TryGetPlaceTarget(hit, out int3 place))
                RequestModifyVoxel(place, devPlaceBlockId);

            ShowHighlight(hasTarget ? hit.Block : (int3?)null);
        }

        /// <summary>
        /// The open block against the face we're aiming at, unless it would overlap this hero.
        /// (3.3 adds full placement checks, including other players.)
        /// </summary>
        bool TryGetPlaceTarget(VoxelHit hit, out int3 target)
        {
            target = hit.Block + hit.Normal;
            if (math.all(hit.Normal == 0) || !_player.World.GetVoxel(target).IsAir)
                return false;

            float3 half = _player.Motor.HalfExtents;
            float3 center = (float3)transform.position + new float3(0f, half.y, 0f);
            bool overlapsHero = math.all(center + half > target) & math.all(center - half < (float3)target + 1f);
            return !overlapsHero;
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
