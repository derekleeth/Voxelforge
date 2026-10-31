using System;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>Why the host said no to hearthing home.</summary>
    public enum HearthRefusal : byte
    {
        NoHome,
        TotemGone,
        Busy,
        Interrupted,
        TooFar,
    }

    /// <summary>
    /// Home Totems and hearthing home (Roadmap 3.8). Placing a Home Totem makes it this player's home (one each: a new one
    /// breaks the old one, which drops). The Home button opens a prompt in this player's own view (Law #5); Place confirms
    /// and sends <see cref="RequestHearthRpc"/>. <b>The host</b> checks the totem still stands, runs a short cast that
    /// moving cancels, streams the terrain around the totem in, and moves the hero beside it (Laws #2 and #3).
    /// <para>Every PC hears the cast start and end, to draw it (<see cref="HearthView"/>).</para>
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerHearth : NetworkBehaviour, IChunkViewerSource
    {
        [Tooltip("Seconds of casting before the hero leaves.")]
        [SerializeField, Range(0.5f, 6f)] float castSeconds = 2f;

        [Tooltip("Extra seconds the host waits for far-away terrain to load before giving up.")]
        [SerializeField, Range(1f, 20f)] float loadWaitSeconds = 10f;

        [Tooltip("The prompt closes by itself after this many seconds.")]
        [SerializeField, Range(2f, 15f)] float promptSeconds = 6f;

        [Tooltip("How long a message stays up, in seconds.")]
        [SerializeField, Range(0.5f, 6f)] float messageSeconds = 3f;

        // Moving the stick further than this, or stepping this far, cancels the cast.
        const float MoveDeadzone = 0.2f;
        const float DriftAllowed = 0.35f;

        readonly NetworkVariable<bool> _hasHome = new NetworkVariable<bool>();

        PlayerController _player;
        PlayerCrafter _crafter;
        PlayerRace _race;

        // Host state.
        int3 _homeBlock;
        bool _casting;
        float _castStart;
        float3 _castFeet;
        float _nextHomeCheck;

        // Every PC.
        float _castStartLocal = float.NegativeInfinity;
        bool _castingLocal;

        // Owner state.
        PlayerButtons _previousButtons;
        bool _promptOpen;
        float _promptUntil;
        bool _placeHeld;
        string _message;
        float _messageUntil;

        /// <summary>Every PC: this player has a Home Totem.</summary>
        public bool HasHome => _hasHome.Value;

        /// <summary>Host only: where this player's Home Totem stands (with <see cref="HasHome"/>).</summary>
        public int3 ServerHomeBlock => _homeBlock;

        /// <summary>Owner only: the "Teleport home?" prompt is showing.</summary>
        public bool IsPromptOpen => _promptOpen;

        /// <summary>
        /// Owner only: Place belongs to the prompt right now (it's open, or Place is still held from confirming it), so it
        /// mustn't build a block too.
        /// </summary>
        public bool IsUsingPlace => _promptOpen || _placeHeld;

        /// <summary>Every PC: the hero is casting, and how far through it they are (0-1).</summary>
        public bool IsCasting => _castingLocal;

        public float CastProgress => _castingLocal ? Mathf.Clamp01((Time.time - _castStartLocal) / CastSeconds) : 0f;

        /// <summary>How long this hero's hearth cast takes: shorter for a Hearthbound race (Roadmap 4.7).</summary>
        float CastSeconds => RaceRules.HearthSeconds(castSeconds, _race != null ? _race.Definition : null);

        /// <summary>Owner only: a short message, or null.</summary>
        public string Message => Time.time < _messageUntil ? _message : null;

        /// <summary>Every PC: the cast started (where the hero stands).</summary>
        public event Action<Vector3> CastStarted;

        /// <summary>Every PC: the cast ended. True with where they landed if they went home; false if it was cancelled.</summary>
        public event Action<bool, Vector3> CastEnded;

        void Awake()
        {
            _player = GetComponent<PlayerController>();
            _crafter = GetComponent<PlayerCrafter>();
            _race = GetComponent<PlayerRace>();
        }

        /// <summary>Host, just after spawning: the home this player had last time (Roadmap 3.4 profile).</summary>
        public void ServerRestore(bool hasHome, int3 homeBlock)
        {
            _homeBlock = homeBlock;
            _hasHome.Value = hasHome;
        }

        /// <summary>
        /// Host, after this player placed a Home Totem at <paramref name="cell"/>: it's their home now. Their old totem, if
        /// it still stands, breaks (an ordinary edit, so it drops for them to pick up).
        /// </summary>
        public void ServerSetHome(int3 cell)
        {
            if (!IsServer)
                return;
            VoxelWorldManager world = _player.WorldManager;
            if (_hasHome.Value && !_homeBlock.Equals(cell) && world != null &&
                HomeTotems.IsStanding(world.World, world.DeltaLog, _homeBlock))
                world.RequestModifyVoxel(_homeBlock, BlockIds.Air, _player.PlayerId);
            _homeBlock = cell;
            _hasHome.Value = true;
            MessageRpc("Home Totem placed! Press Home to come back here.");
        }

        /// <summary>The chunk loader streams the home's terrain in while this player hearths there.</summary>
        public bool TryGetViewerPosition(out float3 position)
        {
            position = (float3)_homeBlock + 0.5f;
            return IsServer && IsSpawned && _casting;
        }

        public override void OnNetworkDespawn()
        {
            _casting = _castingLocal = false;
            base.OnNetworkDespawn();
        }

        void Update()
        {
            if (!IsSpawned || !IsOwner)
                return;

            PlayerButtons buttons = _player.OwnerInput.Buttons;
            PlayerButtons pressed = buttons & ~_previousButtons;
            _previousButtons = buttons;
            if ((buttons & PlayerButtons.Place) == 0)
                _placeHeld = false;

            if (_promptOpen && (Time.time > _promptUntil || (pressed & (PlayerButtons.Home | PlayerButtons.Interact)) != 0 ||
                                (_crafter != null && _crafter.IsMenuOpen)))
            {
                _promptOpen = false;
                pressed &= ~PlayerButtons.Home; // This press closed it.
            }
            else if (_promptOpen && (pressed & PlayerButtons.Place) != 0)
            {
                _promptOpen = false;
                _placeHeld = true;
                RequestHearthRpc();
            }

            if ((pressed & PlayerButtons.Home) != 0 && !_castingLocal && _player.HasLanded &&
                (_crafter == null || !_crafter.IsMenuOpen))
            {
                if (_hasHome.Value)
                {
                    _promptOpen = true;
                    _promptUntil = Time.time + promptSeconds;
                }
                else
                {
                    Say(Explain(HearthRefusal.NoHome));
                }
            }
        }

        void FixedUpdate()
        {
            if (!IsServer || !IsSpawned)
                return;
            if (!_casting)
            {
                // Someone may have mined the totem: check now and then, so the HUD stops showing a home.
                if (_hasHome.Value && Time.time >= _nextHomeCheck)
                {
                    _nextHomeCheck = Time.time + 1f;
                    VoxelWorldManager manager = _player.WorldManager;
                    if (manager != null && manager.World != null && !HomeTotems.IsStanding(manager.World, manager.DeltaLog, _homeBlock))
                    {
                        _hasHome.Value = false;
                        MessageRpc("Your Home Totem was broken! Place a new one.");
                    }
                }
                return;
            }

            PlayerInputFrame input = _player.ServerInput;
            bool moved = math.length(input.Move) > MoveDeadzone ||
                         (input.Buttons & (PlayerButtons.Jump | PlayerButtons.Mine)) != 0 ||
                         math.distance(((float3)transform.position).xz, _castFeet.xz) > DriftAllowed;
            if (moved)
            {
                EndCast(HearthRefusal.Interrupted);
                return;
            }

            float elapsed = Time.time - _castStart;
            if (elapsed < CastSeconds)
                return;

            VoxelWorldManager world = _player.WorldManager;
            if (world == null || !HomeTotems.IsStanding(world.World, world.DeltaLog, _homeBlock))
            {
                _hasHome.Value = false;
                EndCast(HearthRefusal.TotemGone);
                return;
            }
            if (!HomeTotems.IsReady(world.World, _homeBlock))
            {
                if (elapsed > CastSeconds + loadWaitSeconds)
                    EndCast(HearthRefusal.TooFar);
                return; // Still streaming in.
            }
            if (!HomeTotems.TryFindLanding(world.World, _homeBlock, _player.Motor.HalfExtents, out float3 feet, out float yaw))
            {
                EndCast(HearthRefusal.TotemGone);
                return;
            }

            _casting = false;
            _player.ServerTeleport(feet, yaw);
            CastEndedRpc(true, feet);
        }

        void EndCast(HearthRefusal reason)
        {
            _casting = false;
            CastEndedRpc(false, transform.position);
            RefusedRpc(reason);
        }

        [Rpc(SendTo.Server)]
        void RequestHearthRpc(RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || !_player.HasLanded)
                return;
            if (_casting)
            {
                RefusedRpc(HearthRefusal.Busy);
                return;
            }
            VoxelWorldManager world = _player.WorldManager;
            if (!_hasHome.Value)
            {
                RefusedRpc(HearthRefusal.NoHome);
                return;
            }
            if (world == null || !HomeTotems.IsStanding(world.World, world.DeltaLog, _homeBlock))
            {
                _hasHome.Value = false;
                RefusedRpc(HearthRefusal.TotemGone);
                return;
            }

            _casting = true;
            _castStart = Time.time;
            _castFeet = transform.position;
            CastStartedRpc(transform.position);
        }

        [Rpc(SendTo.ClientsAndHost)]
        void CastStartedRpc(Vector3 at)
        {
            _castingLocal = true;
            _castStartLocal = Time.time;
            CastStarted?.Invoke(at);
        }

        [Rpc(SendTo.ClientsAndHost)]
        void CastEndedRpc(bool arrived, Vector3 at)
        {
            _castingLocal = false;
            CastEnded?.Invoke(arrived, at);
            if (arrived && IsOwner)
                Say("Welcome home!");
        }

        [Rpc(SendTo.Owner)]
        void RefusedRpc(HearthRefusal reason) => Say(Explain(reason));

        [Rpc(SendTo.Owner)]
        void MessageRpc(string message) => Say(message);

        void Say(string message)
        {
            _message = message;
            _messageUntil = Time.time + messageSeconds;
        }

        static string Explain(HearthRefusal reason)
        {
            switch (reason)
            {
                case HearthRefusal.NoHome:
                    return "You don't have a Home Totem yet. Craft one at a Workbench!";
                case HearthRefusal.TotemGone:
                    return "Your Home Totem is gone. Place a new one!";
                case HearthRefusal.Interrupted:
                    return "Teleport cancelled: stand still while it charges.";
                case HearthRefusal.TooFar:
                    return "Couldn't reach your home. Try again.";
                default:
                    return "Already on your way home!";
            }
        }
    }
}
