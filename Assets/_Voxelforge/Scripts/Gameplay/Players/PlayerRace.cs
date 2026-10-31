using System;
using Unity.Netcode;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// This hero's race (Roadmap 4.7): Wayfarer or Thunderhoof. <b>The host</b> decides it: the owner only asks
    /// (<see cref="RequestChoose"/>, from the race window that opens on their first join), and the host accepts it only
    /// while the hero hasn't chosen yet, then locks it (CLAUDE.md Law #2). The race adds to the hero's pools and puts its
    /// racial ability in slot 9, through <see cref="PlayerClass"/>. Every PC sees the race through a network variable and
    /// draws the right body (<see cref="HeroBody"/>). It's saved in the player's profile and comes back with them
    /// (<see cref="PlayerRegistry"/>).
    /// </summary>
    [RequireComponent(typeof(PlayerController), typeof(PlayerClass))]
    public sealed class PlayerRace : NetworkBehaviour
    {
        [Tooltip("Data/Races/RaceBook.asset: every race.")]
        [SerializeField] RaceBook races;

        readonly NetworkVariable<RaceId> _race = new NetworkVariable<RaceId>();
        readonly NetworkVariable<bool> _chosen = new NetworkVariable<bool>();

        PlayerClass _class;

        public RaceBook Book => races;

        /// <summary>Every PC: the hero's race, as the host shared it. Wayfarer until they choose.</summary>
        public RaceId Race => _race.Value;

        /// <summary>Every PC: the hero has chosen their race, and it can't change any more.</summary>
        public bool HasChosen => _chosen.Value;

        /// <summary>Every PC: the definition of <see cref="Race"/>, or null if the book doesn't have it.</summary>
        public RaceDefinition Definition => races != null && races.TryGet(_race.Value, out RaceDefinition definition)
            ? definition
            : null;

        /// <summary>Every PC: the hero's race changed (not when they first spawn with it).</summary>
        public event Action<RaceId> Changed;

        /// <summary>Every PC: the hero chose (true) or, from the dev menu, may choose again (false).</summary>
        public event Action<bool> ChosenChanged;

        void Awake() => _class = GetComponent<PlayerClass>();

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _race.OnValueChanged += OnRaceChanged;
            _chosen.OnValueChanged += OnChosenChanged;
        }

        public override void OnNetworkDespawn()
        {
            _race.OnValueChanged -= OnRaceChanged;
            _chosen.OnValueChanged -= OnChosenChanged;
            base.OnNetworkDespawn();
        }

        void OnRaceChanged(RaceId before, RaceId after) => Changed?.Invoke(after);

        void OnChosenChanged(bool before, bool after) => ChosenChanged?.Invoke(after);

        /// <summary>
        /// Host only, right after spawning and before <see cref="PlayerClass.ServerRestore"/> (which adds the race in):
        /// gives the hero their saved race, or makes them a Wayfarer if it's unknown. A hero who hasn't
        /// <paramref name="chosen"/> gets the race window.
        /// </summary>
        public void ServerRestore(RaceId id, bool chosen)
        {
            if (!IsServer)
                return;
            if (races == null || !races.TryGet(id, out _))
            {
                if (races == null || !races.TryGet(RaceId.Wayfarer, out _))
                    Debug.LogError("PlayerRace has no RaceBook with a Wayfarer (Voxelforge > Build Abilities).", this);
                id = RaceId.Wayfarer;
                chosen = false;
            }
            _race.Value = id;
            _chosen.Value = chosen;
        }

        /// <summary>Owner only: asks the host to make this hero a <paramref name="id"/>, once.</summary>
        public void RequestChoose(RaceId id)
        {
            if (IsOwner && !_chosen.Value)
                ChooseRpc(id);
        }

        [Rpc(SendTo.Server)]
        void ChooseRpc(RaceId id, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || _chosen.Value || races == null ||
                !races.TryGet(id, out _))
                return;
            _race.Value = id;
            _chosen.Value = true;
            _class.ServerReapply();
        }

        /// <summary>Host only, for testing (Voxelforge &gt; Debug): lets the hero choose their race again.</summary>
        public void ServerResetChoice()
        {
            if (IsServer)
                _chosen.Value = false;
        }
    }

    /// <summary>
    /// For testing (Voxelforge &gt; Debug, in Play on the host): lets every hero choose their race again. Kept off
    /// <see cref="PlayerRace"/> so the editor menu doesn't need to know about networking.
    /// </summary>
    public static class RaceDebug
    {
        /// <summary>Returns how many heroes may choose again.</summary>
        public static int ServerResetAllChoices()
        {
            int reset = 0;
            foreach (PlayerRace race in UnityEngine.Object.FindObjectsByType<PlayerRace>())
            {
                if (!race.IsServer)
                    continue;
                race.ServerResetChoice();
                reset++;
            }
            return reset;
        }
    }
}
