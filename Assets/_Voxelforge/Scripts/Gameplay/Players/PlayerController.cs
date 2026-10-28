using System;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using Voxelforge.Core;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// One hero in the world. The host spawns it (see <see cref="PlayerRegistry"/>) and runs all of its movement;
    /// the owning client only reads controls and sends them up with <see cref="SubmitInputRpc"/> (CLAUDE.md Law #2).
    /// <para>
    /// The transform's position is the player's <b>feet</b>. A server-authority <c>NetworkTransform</c> on the
    /// same object sends it to every client.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PlayerController : NetworkBehaviour
    {
        [SerializeField] MotorSettings motor = MotorSettings.Default;

        [Tooltip("Blocks of open space a spawn point needs above the ground.")]
        [SerializeField, Min(1)] int spawnClearance = 2;

        [Tooltip("Placeholder body, tinted per local slot so players can tell each other apart.")]
        [SerializeField] Renderer bodyRenderer;

        [SerializeField] Color[] slotColors = { new Color(0.25f, 0.55f, 1f), new Color(1f, 0.45f, 0.2f), new Color(0.4f, 0.85f, 0.35f), new Color(0.85f, 0.35f, 0.9f) };

        readonly NetworkVariable<byte> _playerId = new NetworkVariable<byte>();
        readonly NetworkVariable<byte> _localSlot = new NetworkVariable<byte>();

        IPlayerInputSource _inputSource;
        uint _nextSequence;

        // Host-only state.
        PlayerId _assignedId;
        byte _assignedSlot;
        VoxelWorldManager _worldManager;
        int2 _spawnColumn;
        int _spawnTopY;
        bool _placed;
        VoxelBody _body;
        float3 _previousPosition;
        PlayerInputFrame _latestInput;
        bool _hasInput;

        /// <summary>Who this player is. Record this as the instigator of anything they do.</summary>
        public PlayerId PlayerId => new PlayerId(_playerId.Value);

        /// <summary>Which player on the owning PC this is: 0 = Player 1, 1 = Player 2.</summary>
        public byte LocalSlot => _localSlot.Value;

        /// <summary>Host only: standing on a solid block.</summary>
        public bool Grounded => _body.Grounded;

        /// <summary>Host only: still waiting for the terrain under the spawn point to generate.</summary>
        public bool IsWaitingForGround => !_placed || _body.WaitingForWorld;

        /// <summary>
        /// Host only, called by <see cref="PlayerRegistry"/> just before spawning. The ID and slot are written to
        /// the network in <see cref="OnNetworkSpawn"/>, so clients receive them with the spawn itself.
        /// </summary>
        internal void ServerInitialize(PlayerId playerId, byte localSlot, VoxelWorldManager worldManager, int2 spawnColumn, int spawnTopY)
        {
            _assignedId = playerId;
            _assignedSlot = localSlot;
            _worldManager = worldManager;
            _spawnColumn = spawnColumn;
            _spawnTopY = spawnTopY;
            name = $"Player {playerId}";
        }

        /// <summary>
        /// Raised on the owning PC when one of its players spawns, so <see cref="LocalPlayerManager"/> can give it
        /// the controls of its local slot. An event, not a global player list (CLAUDE.md Law #1).
        /// </summary>
        public static event Action<PlayerController> OwnerSpawned;

        /// <summary>Raised on the owning PC when one of its players despawns.</summary>
        public static event Action<PlayerController> OwnerDespawned;

        /// <summary>Owner only: where this player's controls come from.</summary>
        public void SetInputSource(IPlayerInputSource source)
        {
            if (!IsOwner)
            {
                Debug.LogError($"Only the owning PC can control {PlayerId}.", this);
                return;
            }
            _inputSource = source;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                _playerId.Value = _assignedId.Value;
                _localSlot.Value = _assignedSlot;
            }
            name = $"Player {PlayerId}";

            if (bodyRenderer != null && slotColors.Length > 0)
            {
                var block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", slotColors[LocalSlot % slotColors.Length]);
                bodyRenderer.SetPropertyBlock(block);
            }

            if (IsOwner)
                OwnerSpawned?.Invoke(this);
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner)
                OwnerDespawned?.Invoke(this);
            _inputSource = null;
        }

        /// <summary>
        /// Sends this frame's controls to the host. The host drops frames from anyone but the owner, and
        /// frames older than one it already has.
        /// </summary>
        [Rpc(SendTo.Server)]
        void SubmitInputRpc(PlayerInputFrame frame, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
                return;
            if (_hasInput && (int)(frame.Sequence - _latestInput.Sequence) <= 0)
                return;

            _latestInput = frame;
            _hasInput = true;
        }

        void Update()
        {
            if (!IsSpawned)
                return;

            if (IsOwner && _inputSource != null)
            {
                PlayerInputFrame frame = _inputSource.Read();
                frame.Sequence = _nextSequence++;
                SubmitInputRpc(frame);
            }

            // The motor ticks at the fixed rate; blend between ticks so movement looks smooth at 120+ FPS.
            if (IsServer && _placed)
            {
                float alpha = math.saturate((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
                transform.position = math.lerp(_previousPosition, _body.FeetPosition(motor), alpha);
            }
        }

        void FixedUpdate()
        {
            if (!IsServer || !IsSpawned || _worldManager == null || _worldManager.World == null)
                return;

            if (!_placed && !TryPlaceOnGround())
                return;

            _previousPosition = _body.FeetPosition(motor);
            var input = new MotorInput
            {
                Move = _latestInput.Move,
                Jump = _latestInput.IsPressed(PlayerButtons.Jump),
            };
            VoxelBodyMotor.Step(ref _body, input, motor, _worldManager.World, Time.fixedDeltaTime);
        }

        bool TryPlaceOnGround()
        {
            if (!SpawnPointFinder.TryFindSurface(_worldManager.World, _spawnColumn.x, _spawnColumn.y, _spawnTopY,
                    spawnClearance, out float3 feet))
                return false;

            _body = new VoxelBody { Position = feet + new float3(0f, motor.HalfExtents.y + 0.001f, 0f) };
            _previousPosition = feet;
            transform.position = feet;
            _placed = true;
            Debug.Log($"[Voxelforge] {PlayerId} (slot {LocalSlot}) landed at {feet}.", this);
            return true;
        }
    }
}
