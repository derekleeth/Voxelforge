using System;
using Unity.Mathematics;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using Voxelforge.Core;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// One hero in the world. The host spawns it (see <see cref="PlayerRegistry"/>) and runs all of its movement;
    /// the owning client only reads controls and sends them up with <see cref="SubmitInputRpc"/> (CLAUDE.md Law #2).
    /// <para>
    /// The transform's position is the player's <b>feet</b>, and its yaw is the way the hero faces. A
    /// server-authority <c>NetworkTransform</c> on the same object sends both to every client. Animation and foot IK
    /// are worked out on each PC from that motion plus <see cref="Grounded"/> (see <see cref="HeroAnimator"/>).
    /// </para>
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PlayerController : NetworkBehaviour, IChunkViewerSource
    {
        [SerializeField] MotorSettings motor = MotorSettings.Default;

        [Tooltip("Blocks of open space a spawn point needs above the ground.")]
        [SerializeField, Min(1)] int spawnClearance = 2;

        [Tooltip("How fast the hero turns to face where they walk, in degrees per second.")]
        [SerializeField, Min(1f)] float turnSpeed = 720f;

        [Tooltip("The hero model. It follows this object, smoothed over steps, and cameras follow it.")]
        [SerializeField] Transform visualRoot;

        [Tooltip("Renderer with the team-color cloth, tinted per local slot so players can tell each other apart.")]
        [SerializeField] Renderer teamColorRenderer;

        [Tooltip("Which of the renderer's materials is the team-color cloth.")]
        [SerializeField, Min(0)] int teamColorMaterialIndex;

        [SerializeField] Color[] slotColors = { new Color(0.25f, 0.55f, 1f), new Color(1f, 0.45f, 0.2f), new Color(0.4f, 0.85f, 0.35f), new Color(0.85f, 0.35f, 0.9f) };

        [Tooltip("How this hero aims at blocks, for mining and building. The owner and the host both use it.")]
        [SerializeField] PlayerAim aim = new PlayerAim();

        readonly NetworkVariable<byte> _playerId = new NetworkVariable<byte>();
        readonly NetworkVariable<byte> _localSlot = new NetworkVariable<byte>();
        readonly NetworkVariable<bool> _landed = new NetworkVariable<bool>();
        readonly NetworkVariable<bool> _grounded = new NetworkVariable<bool>();

        IPlayerInputSource _inputSource;
        PlayerHands _hands;
        PlayerInventory _inventory;
        PlayerClassMenu _classMenu;
        PlayerRaceMenu _raceMenu;
        uint _nextSequence;
        // Buttons still held from inside the inventory window, ignored until let go so closing it doesn't also jump.
        PlayerButtons _swallowed;

        // Host-only state.
        PlayerId _assignedId;
        byte _assignedSlot;
        PlayerUuid _uuid;
        bool _hasSavedSpot;
        float3 _savedFeet;
        float _savedYaw;
        VoxelWorldManager _worldManager;
        PlayerRegistry _registry;
        int2 _spawnColumn;
        int _spawnTopY;
        bool _placed;
        VoxelBody _body;
        float3 _previousPosition;
        float _yaw;
        float _previousYaw;
        PlayerInputFrame _latestInput;
        bool _hasInput;

        Material _teamColorMaterial;

        /// <summary>Who this player is. Record this as the instigator of anything they do.</summary>
        public PlayerId PlayerId => new PlayerId(_playerId.Value);

        /// <summary>Host only: who this player is across sessions. Their saved profile is kept under it (Roadmap 3.4).</summary>
        public PlayerUuid Uuid => _uuid;

        /// <summary>Which player on the owning PC this is: 0 = Player 1, 1 = Player 2.</summary>
        public byte LocalSlot => _localSlot.Value;

        /// <summary>Standing on a solid block. The host decides it; every PC sees it, for animation.</summary>
        public bool Grounded => _grounded.Value;

        /// <summary>Host only: still waiting for the terrain under the spawn point to generate.</summary>
        public bool IsWaitingForGround => !_placed || _body.WaitingForWorld;

        /// <summary>True on every PC once this player has been placed on the ground. Cameras wait for it.</summary>
        public bool HasLanded => _landed.Value;

        /// <summary>The hero model: where the player <i>looks</i> like they are, smoothed over steps. Cameras follow this.</summary>
        public Transform VisualRoot => visualRoot != null ? visualRoot : transform;

        /// <summary>
        /// The blocks this PC can see, for presentation such as foot IK. Only the host has it for now; a remote PC
        /// gets null and skips those effects until it builds its own world in Phase 6.
        /// </summary>
        public VoxelWorld World => _worldManager != null ? _worldManager.World : null;

        /// <summary>Host only: the world this player lives in. Its edits go through here (see <see cref="PlayerVoxelEditor"/>).</summary>
        internal VoxelWorldManager WorldManager => _worldManager;

        /// <summary>Host only: every player in the session, for checks such as "would this block trap someone?".</summary>
        internal PlayerRegistry Registry => _registry;

        /// <summary>This player's team colour (by local slot), for the cloth and anything else that marks them out.</summary>
        public Color TeamColor => slotColors.Length > 0 ? slotColors[LocalSlot % slotColors.Length] : Color.white;

        /// <summary>
        /// Owner only: the frame just sent to the host, for owner-side tools such as aiming at blocks. While the
        /// inventory window is open (Roadmap 4.5) it has no movement and no buttons: the window has the controls.
        /// </summary>
        public PlayerInputFrame OwnerInput { get; private set; }

        /// <summary>
        /// Owner only: one of this player's windows is open and has their controls: the inventory (Roadmap 4.5) or the
        /// class window (4.6). The hero stands still, and the mouse player gets a free cursor.
        /// </summary>
        public bool WindowOpen => (_inventory != null && _inventory.IsOpen) || (_classMenu != null && _classMenu.IsOpen) ||
                                  ChoosingRace;

        /// <summary>Owner only: the race window (Roadmap 4.7) is up, and every other window stays shut until they choose.</summary>
        public bool ChoosingRace => _raceMenu != null && _raceMenu.IsOpen;

        /// <summary>Owner only: this frame's controls as pressed, even while a window has them (<see cref="PlayerInventory"/>).</summary>
        public PlayerInputFrame OwnerRawInput { get; private set; }

        /// <summary>Host only: the newest frame received from the owner. Host-side actions such as mining read it.</summary>
        public PlayerInputFrame ServerInput => _latestInput;

        /// <summary>Which block this hero aims at.</summary>
        public PlayerAim Aim => aim;

        /// <summary>The size of this player's collision box, for checks such as "would this block trap me?".</summary>
        public MotorSettings Motor => motor;

        /// <summary>
        /// This hero's collision box. On the host it's the motor's own body; elsewhere it follows the synced transform.
        /// </summary>
        public BodyBounds Bounds => BodyBounds.FromFeet(IsServer && _placed ? _body.FeetPosition(motor) : (float3)transform.position,
            motor.HalfExtents);

        /// <summary>
        /// Host only, called by <see cref="PlayerRegistry"/> just before spawning. The ID and slot are written to
        /// the network in <see cref="OnNetworkSpawn"/>, so clients receive them with the spawn itself.
        /// </summary>
        /// <param name="profile">
        /// Optional: where this player stood last time (Roadmap 3.4). They land on that exact spot if it's still clear,
        /// otherwise on the ground of that column.
        /// </param>
        internal void ServerInitialize(PlayerId playerId, byte localSlot, PlayerUuid uuid, VoxelWorldManager worldManager,
            PlayerRegistry registry, int2 spawnColumn, int spawnTopY, PlayerProfile profile)
        {
            _assignedId = playerId;
            _assignedSlot = localSlot;
            _uuid = uuid;
            _worldManager = worldManager;
            _registry = registry;
            _spawnColumn = spawnColumn;
            _spawnTopY = spawnTopY;
            _hasSavedSpot = profile != null;
            if (_hasSavedSpot)
            {
                _savedFeet = profile.Feet;
                _savedYaw = profile.Yaw;
                _spawnColumn = (int2)math.floor(profile.Feet.xz);
            }
            name = $"Player {playerId}";
        }

        /// <summary>
        /// Host only: where this player stands and faces, for their saved profile. False until they've landed,
        /// so a player still waiting for terrain keeps the spot they were saved at.
        /// </summary>
        public bool TryGetSaveState(out float3 feet, out float yaw)
        {
            feet = _placed ? _body.FeetPosition(motor) : default;
            yaw = _yaw;
            return IsServer && _placed;
        }

        /// <summary>
        /// Host only: where the <see cref="ChunkLoader"/> should load terrain. Before landing, that's the spawn column,
        /// so the ground under the spawn point generates first.
        /// </summary>
        public bool TryGetViewerPosition(out float3 position)
        {
            position = _placed ? (float3)transform.position : new float3(_spawnColumn.x + 0.5f, 0f, _spawnColumn.y + 0.5f);
            return IsServer && IsSpawned;
        }

        /// <summary>
        /// Raised on the owning PC when one of its players spawns, so <see cref="LocalPlayerManager"/> can give it
        /// the controls of its local slot. An event, not a global player list (CLAUDE.md Law #1).
        /// </summary>
        public static event Action<PlayerController> OwnerSpawned;

        /// <summary>Raised on the owning PC when one of its players despawns.</summary>
        public static event Action<PlayerController> OwnerDespawned;

        /// <summary>Raised on every PC when any player spawns, e.g. so footsteps play for remote heroes too (3.9).</summary>
        public static event Action<PlayerController> AnySpawned;

        /// <summary>Raised on every PC when any player despawns.</summary>
        public static event Action<PlayerController> AnyDespawned;

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

        void Awake()
        {
            _hands = GetComponent<PlayerHands>();
            _inventory = GetComponent<PlayerInventory>();
            _classMenu = GetComponent<PlayerClassMenu>();
            _raceMenu = GetComponent<PlayerRaceMenu>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                _playerId.Value = _assignedId.Value;
                _localSlot.Value = _assignedSlot;
            }
            name = $"Player {PlayerId}";

            TintTeamColor();

            if (IsOwner)
                OwnerSpawned?.Invoke(this);
            AnySpawned?.Invoke(this);
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner)
                OwnerDespawned?.Invoke(this);
            AnyDespawned?.Invoke(this);
            _inputSource = null;
            OwnerInput = default;
            OwnerRawInput = default;
        }

        public override void OnDestroy()
        {
            if (_teamColorMaterial != null)
                Destroy(_teamColorMaterial);
            base.OnDestroy();
        }

        /// <summary>
        /// Gives the team-color cloth its own copy of the material in this slot's color. A material copy rather
        /// than a property block keeps the SRP Batcher working.
        /// </summary>
        void TintTeamColor()
        {
            if (teamColorRenderer == null || slotColors.Length == 0)
                return;

            Material[] materials = teamColorRenderer.sharedMaterials;
            if (teamColorMaterialIndex >= materials.Length || materials[teamColorMaterialIndex] == null)
                return;

            if (_teamColorMaterial == null)
                _teamColorMaterial = new Material(materials[teamColorMaterialIndex]) { name = $"{materials[teamColorMaterialIndex].name} ({PlayerId})" };
            _teamColorMaterial.SetColor("_BaseColor", TeamColor);
            materials[teamColorMaterialIndex] = _teamColorMaterial;
            teamColorRenderer.sharedMaterials = materials;
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
                frame.HeldItem = _hands != null ? _hands.OwnerSelected : BlockIds.Air;
                OwnerRawInput = frame;
                frame.Buttons &= ~(PlayerButtons.Inventory | PlayerButtons.ClassMenu);
                if (WindowOpen)
                {
                    // The hero stands still while a window has the controls (Roadmap 4.5); the camera still turns.
                    frame.Move = float2.zero;
                    _swallowed = frame.Buttons;
                    frame.Buttons = PlayerButtons.None;
                }
                else
                {
                    _swallowed &= frame.Buttons;
                    frame.Buttons &= ~_swallowed;
                }
                OwnerInput = frame;
                SubmitInputRpc(frame);
            }

            // The motor ticks at the fixed rate; blend between ticks so movement looks smooth at 120+ FPS.
            if (IsServer && _placed)
            {
                float alpha = math.saturate((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
                transform.SetPositionAndRotation(math.lerp(_previousPosition, _body.FeetPosition(motor), alpha),
                    Quaternion.Euler(0f, Mathf.LerpAngle(_previousYaw, _yaw, alpha), 0f));
            }
        }

        void FixedUpdate()
        {
            if (!IsServer || !IsSpawned || _worldManager == null || _worldManager.World == null)
                return;

            if (!_placed && !TryPlaceOnGround())
                return;

            _previousPosition = _body.FeetPosition(motor);
            _previousYaw = _yaw;
            var input = new MotorInput
            {
                Move = MoveDirection.ToWorld(_latestInput.Move, _latestInput.ViewYaw),
                Jump = _latestInput.IsPressed(PlayerButtons.Jump),
            };
            VoxelBodyMotor.Step(ref _body, input, motor, _worldManager.World, Time.fixedDeltaTime);

            // Face where we walk. Standing still keeps the last facing.
            if (math.lengthsq(input.Move) > 0.01f)
                _yaw = math.fmod(MoveDirection.TurnToward(_yaw, MoveDirection.YawOf(input.Move), turnSpeed * Time.fixedDeltaTime), 360f);
            if (_grounded.Value != _body.Grounded)
                _grounded.Value = _body.Grounded;
        }

        bool TryPlaceOnGround()
        {
            float3 feet;
            float yaw = transform.eulerAngles.y;
            if (_hasSavedSpot)
            {
                // Wait for the terrain around the saved spot, then use it unless someone has built into it.
                float3 center = _savedFeet + new float3(0f, motor.HalfExtents.y + 0.001f, 0f);
                if (!IsGenerated(center - motor.HalfExtents) || !IsGenerated(center + motor.HalfExtents))
                    return false;
                _hasSavedSpot = false;
                if (!VoxelBodyMotor.Overlaps(center, motor.HalfExtents, _worldManager.World))
                {
                    PlaceAt(_savedFeet, _savedYaw);
                    return true;
                }
                Debug.Log($"[Voxelforge] {PlayerId}'s saved spot {_savedFeet} is blocked; landing on top of that column instead.", this);
                yaw = _savedYaw;
            }

            if (!SpawnPointFinder.TryFindSurface(_worldManager.World, _spawnColumn.x, _spawnColumn.y, _spawnTopY,
                    spawnClearance, out feet))
                return false;
            PlaceAt(feet, yaw);
            return true;
        }

        /// <summary>
        /// Host only: moves this hero to <paramref name="feet"/> at once, facing <paramref name="yaw"/>, e.g. hearthing home
        /// (Roadmap 3.8). Other PCs jump there too instead of sliding across the world.
        /// </summary>
        internal void ServerTeleport(float3 feet, float yaw)
        {
            if (!IsServer || !_placed)
                return;
            PlaceAt(feet, yaw);
            if (TryGetComponent(out NetworkTransform networkTransform))
                networkTransform.Teleport(feet, Quaternion.Euler(0f, yaw, 0f), transform.localScale);
        }

        bool IsGenerated(float3 position) =>
            _worldManager.World.TryGetChunk(VoxelMath.WorldToChunk(VoxelMath.WorldPositionToBlock(position)), out _);

        void PlaceAt(float3 feet, float yaw)
        {
            _body = new VoxelBody { Position = feet + new float3(0f, motor.HalfExtents.y + 0.001f, 0f) };
            _previousPosition = feet;
            _yaw = _previousYaw = yaw;
            transform.SetPositionAndRotation(feet, Quaternion.Euler(0f, yaw, 0f));
            _placed = true;
            _landed.Value = true;
            Debug.Log($"[Voxelforge] {PlayerId} (slot {LocalSlot}) landed at {feet}.", this);
        }
    }
}
