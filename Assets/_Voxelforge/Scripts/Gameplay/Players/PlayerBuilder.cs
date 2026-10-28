using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Building with the blocks in your bag (Roadmap 3.3, D-016). The owning PC holds a block (<see cref="PlayerHands"/>,
    /// 3.5), shows a ghost block where
    /// it would go, and asks with <see cref="RequestPlaceRpc"/>, which carries <b>only the block type</b>. The host aims
    /// itself with the controls it received (<see cref="PlayerController.ServerInput"/>), like mining, checks the cell
    /// with <see cref="VoxelPlacement"/> against every player, takes the block from this player's
    /// <see cref="PlayerBlockBag"/> and places it through <see cref="VoxelWorldManager.RequestModifyVoxel"/> in this
    /// player's name. No block position ever comes from a PC (CLAUDE.md Laws #1-#3).
    /// </summary>
    [RequireComponent(typeof(PlayerController), typeof(PlayerBlockBag), typeof(PlayerHands))]
    public sealed class PlayerBuilder : NetworkBehaviour
    {
        [Tooltip("Holding Place puts down another block this often, in seconds.")]
        [SerializeField, Range(0.1f, 1f)] float repeatSeconds = 0.25f;

        [Tooltip("Host: the shortest time allowed between two placements, in seconds, so a PC can't spam them.")]
        [SerializeField, Range(0f, 0.5f)] float minPlaceInterval = 0.15f;

        [Tooltip("Materials/BlockGhost.mat: the see-through preview of the next block. Empty = no preview.")]
        [SerializeField] Material ghostMaterial;

        [Tooltip("Whether the ghost block starts on. Each player can flip it with Toggle Ghost (G / Y / ').")]
        [SerializeField] bool ghostOnAtStart = true;

        [Tooltip("Log every placement request and its result on the host.")]
        [SerializeField] bool logPlacements;

        PlayerController _player;
        PlayerBlockBag _bag;
        PlayerHands _hands;
        PlayerCrafter _crafter;
        PlayerHearth _hearth;
        BlockGhost _ghost;
        readonly List<BodyBounds> _bodies = new List<BodyBounds>();

        // Owner state.
        ushort _selected;
        PlayerButtons _previousButtons;
        float _nextRepeat;
        bool _ghostOn;

        // Host state.
        float _lastPlaceTime = float.NegativeInfinity;

        void Awake()
        {
            _player = GetComponent<PlayerController>();
            _bag = GetComponent<PlayerBlockBag>();
            _hands = GetComponent<PlayerHands>();
            _crafter = GetComponent<PlayerCrafter>();
            _hearth = GetComponent<PlayerHearth>();
            _ghostOn = ghostOnAtStart;
        }

        public override void OnNetworkDespawn()
        {
            _ghost?.Dispose();
            _ghost = null;
        }

        public override void OnDestroy()
        {
            _ghost?.Dispose();
            _ghost = null;
            base.OnDestroy();
        }

        void Update()
        {
            if (!IsSpawned || !IsOwner || !_player.HasLanded)
            {
                _ghost?.Hide();
                return;
            }

            PlayerButtons buttons = _player.OwnerInput.Buttons;
            PlayerButtons pressed = buttons & ~_previousButtons;
            _previousButtons = buttons;

            // Only blocks are built; a tool in hand (or the crafting window) means no ghost and no placing.
            _selected = ItemIds.IsBlock(_hands.OwnerSelected) ? _hands.OwnerSelected : BlockIds.Air;
            if ((_crafter != null && _crafter.IsMenuOpen) || (_hearth != null && _hearth.IsUsingPlace))
                _selected = BlockIds.Air;
            if ((pressed & PlayerButtons.ToggleGhost) != 0)
                _ghostOn = !_ghostOn;

            // A remote PC can't see blocks yet (Phase 6), so it has no ghost; the host still checks its requests.
            VoxelWorld world = _player.World;
            PlacementResult result = PlacementResult.NoFace;
            int3 cell = default;
            if (_selected != BlockIds.Air && _player.Aim.TryAim(world, transform.position, _player.OwnerInput, out VoxelHit hit))
                result = VoxelPlacement.Check(world, hit, _selected, GatherBodies(), out cell);
            // Out of the way while mining, so the cracks and the outline are easy to see (asked for in the play-test).
            bool ghostVisible = _ghostOn && (buttons & PlayerButtons.Mine) == 0;
            ShowGhost(world, result, cell, ghostVisible);

            bool place = (pressed & PlayerButtons.Place) != 0;
            if ((buttons & PlayerButtons.Place) != 0 && !place && Time.time >= _nextRepeat)
                place = true;
            if (place && result == PlacementResult.Ok)
            {
                RequestPlaceRpc(_selected);
                _nextRepeat = Time.time + repeatSeconds;
            }
        }

        [Rpc(SendTo.Server)]
        void RequestPlaceRpc(ushort blockId, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
                return;

            VoxelWorldManager world = _player.WorldManager;
            if (world == null || !_player.HasLanded || blockId == BlockIds.Air || !ItemIds.IsBlock(blockId) ||
                Time.time - _lastPlaceTime < minPlaceInterval)
                return;

            // The RPC is sent after this frame's controls, so ServerInput is the frame the player saw when they pressed.
            if (!_player.Aim.TryAim(world.World, transform.position, _player.ServerInput, out VoxelHit hit))
                return;
            PlacementResult check = VoxelPlacement.Check(world.World, hit, blockId, GatherBodies(), out int3 cell);
            if (check != PlacementResult.Ok || !_player.Aim.InReach(transform.position, cell))
            {
                if (logPlacements)
                    Debug.Log($"[Voxelforge] {_player.PlayerId} can't place block {blockId} at {cell}: {check}.", this);
                return;
            }
            if (!_bag.ServerTryTake(blockId, 1))
                return;

            VoxelEditResult result = world.RequestModifyVoxel(cell, blockId, _player.PlayerId);
            if (result == VoxelEditResult.Applied)
            {
                _lastPlaceTime = Time.time;
                if (blockId == BlockIds.HomeTotem && _hearth != null)
                    _hearth.ServerSetHome(cell); // Roadmap 3.8: it's this player's home now.
            }
            else
                _bag.ServerAdd(blockId, 1); // Give it back.
            if (logPlacements)
                Debug.Log($"[Voxelforge] {_player.PlayerId} placed block {blockId} at {cell}: {result}.", this);
        }

        /// <summary>Every hero a block mustn't trap: all players on the host, or just this one on a remote PC.</summary>
        List<BodyBounds> GatherBodies()
        {
            _bodies.Clear();
            PlayerRegistry registry = _player.Registry;
            if (registry == null)
            {
                _bodies.Add(_player.Bounds);
                return _bodies;
            }
            foreach (PlayerController player in registry.Players)
            {
                if (player != null && player.HasLanded)
                    _bodies.Add(player.Bounds);
            }
            return _bodies;
        }

        void ShowGhost(VoxelWorld world, PlacementResult result, int3 cell, bool visible)
        {
            if (ghostMaterial == null)
                return;
            _ghost ??= new BlockGhost($"Ghost Block ({_player.PlayerId})", ghostMaterial, _player.TeamColor);
            if (visible && (result == PlacementResult.Ok || result == PlacementResult.BlocksCharacter) &&
                _hands.Looks.TryGet(_selected, world.Blocks, out ItemLook look))
                _ghost.Show(look, cell, result == PlacementResult.Ok);
            else
                _ghost.Hide();
        }
    }
}
