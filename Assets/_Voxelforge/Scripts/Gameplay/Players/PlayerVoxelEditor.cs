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
    /// <b>Aiming:</b> a ray from the hero's eyes in the direction their camera faces picks the block, so turning the
    /// camera moves the target. Dig and place are temporary dev tools; mining (3.2) and ghost-block placement (3.3)
    /// replace them and keep the aiming.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerVoxelEditor : NetworkBehaviour
    {
        [Tooltip("How far from the hero's chest a block can be edited, in metres. The host enforces it.")]
        [SerializeField, Min(1f)] float maxReach = 8f;

        [Tooltip("Log every edit request and its result on the host.")]
        [SerializeField] bool logEdits;

        [Header("Temporary dev controls (until 3.2 / 3.3)")]
        [Tooltip("Dig: left mouse / right trigger. Place: right mouse / left trigger.")]
        [SerializeField] bool devControls = true;

        [Tooltip("The block dev placement puts down.")]
        [SerializeField] ushort devPlaceBlockId = BlockIds.Dirt;

        [Tooltip("Unlit material for the outline around the block Dig will hit. Empty = no outline.")]
        [SerializeField] Material highlightMaterial;

        [Tooltip("How thick the outline's bars are, in metres.")]
        [SerializeField, Range(0.005f, 0.1f)] float highlightThickness = 0.035f;

        [Header("Aiming")]
        [Tooltip("How far the aim ray reaches from the hero's eyes, in metres.")]
        [SerializeField, Range(1f, 8f)] float aimReach = 5f;

        [Tooltip("Height of the hero's eyes above their feet, where the aim ray starts.")]
        [SerializeField, Range(0.5f, 2f)] float eyeHeight = 1.5f;

        [Tooltip("Aim pitch = camera pitch x this + offset. The orbit camera only looks 10-70 degrees down, so this " +
                 "stretches it to reach walls and ceilings as well as the floor.")]
        [SerializeField, Range(0.5f, 3f)] float aimPitchScale = 1.5f;

        [Tooltip("Degrees added to the scaled camera pitch. Negative aims higher.")]
        [SerializeField, Range(-60f, 30f)] float aimPitchOffset = -30f;

        const float ChestHeight = 1.2f;

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
            float3 chest = (float3)transform.position + new float3(0f, ChestHeight, 0f);
            if (math.distance(chest, (float3)block + 0.5f) > maxReach)
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
            if (!IsSpawned || !IsOwner || !devControls || !_player.HasLanded)
            {
                _highlight?.Show(null);
                return;
            }

            PlayerButtons buttons = _player.OwnerInput.Buttons;
            PlayerButtons pressed = buttons & ~_previousButtons;
            _previousButtons = buttons;

            bool hasTarget = TryAim(_player.OwnerInput, out VoxelHit hit);
            if ((pressed & PlayerButtons.DevDig) != 0 && hasTarget)
                RequestModifyVoxel(hit.Block, BlockIds.Air);
            else if ((pressed & PlayerButtons.DevPlace) != 0 && hasTarget && TryGetPlaceTarget(hit, out int3 place))
                RequestModifyVoxel(place, devPlaceBlockId);

            ShowHighlight(hasTarget ? hit.Block : (int3?)null);
        }

        /// <summary>The block this player is aiming at: a ray from the hero's eyes along their camera's view.</summary>
        bool TryAim(PlayerInputFrame input, out VoxelHit hit)
        {
            hit = default;
            VoxelWorld world = _player.World;
            if (world == null)
                return false; // A remote PC can't see blocks yet (Phase 6).

            float pitch = math.clamp(input.ViewPitch * aimPitchScale + aimPitchOffset, -89f, 89f);
            float3 direction = Quaternion.Euler(pitch, input.ViewYaw, 0f) * Vector3.forward;
            float3 eyes = (float3)transform.position + new float3(0f, eyeHeight, 0f);
            return VoxelQueries.Raycast(world, eyes, direction, aimReach, out hit);
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
            _highlight ??= new BlockHighlight($"Dig Target ({_player.PlayerId})", highlightMaterial, _player.TeamColor, highlightThickness);
            _highlight.Show(block);
        }
    }
}
