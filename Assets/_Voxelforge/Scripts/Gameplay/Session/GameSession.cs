using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Starts the game as a <b>Host</b> (server + local client in one program). Even with nobody online, everything
    /// still goes through the host, so online play in Phase 6 only needs a different transport (CLAUDE.md Law #2).
    /// Players appear when someone presses Join (see <see cref="LocalPlayerManager"/> and <see cref="PlayerJoinService"/>).
    /// See DECISIONS.md D-007 and D-008.
    /// </summary>
    public sealed class GameSession : MonoBehaviour
    {
        [SerializeField] NetworkManager networkManager;

        [SerializeField] UnityTransport transport;

        [SerializeField] PlayerRegistry playerRegistry;

        [Tooltip("Optional: shows join prompts per local slot in the debug overlay.")]
        [SerializeField] LocalPlayerManager localPlayers;

        [Tooltip("Port the host listens on. Only this PC connects for now.")]
        [SerializeField] ushort port = 7777;

        public PlayerRegistry Players => playerRegistry;

        public LocalPlayerManager LocalPlayers => localPlayers;

        public NetworkManager NetworkManager => networkManager;

        void Start()
        {
            if (networkManager == null || transport == null || playerRegistry == null)
            {
                Debug.LogError($"{nameof(GameSession)} is missing a reference.", this);
                enabled = false;
                return;
            }

            transport.SetConnectionData("127.0.0.1", port);
            networkManager.NetworkConfig.PlayerPrefab = null; // Players are spawned on Join, several per connection.
            GameObject playerPrefab = playerRegistry.PlayerPrefab.gameObject;
            if (!networkManager.NetworkConfig.Prefabs.Contains(playerPrefab))
                networkManager.AddNetworkPrefab(playerPrefab);
            networkManager.OnServerStarted += HandleServerStarted;
            networkManager.OnServerStopped += HandleServerStopped;

            if (!networkManager.StartHost())
                Debug.LogError("[Voxelforge] Could not start the host.", this);
        }

        void OnDestroy()
        {
            if (networkManager == null)
                return;

            networkManager.OnServerStarted -= HandleServerStarted;
            networkManager.OnServerStopped -= HandleServerStopped;
        }

        void HandleServerStarted() =>
            Debug.Log($"[Voxelforge] Host started on port {port} (client {networkManager.LocalClientId}). Press Start / Enter to join.", this);

        void HandleServerStopped(bool wasHost) => playerRegistry.Clear();
    }
}
