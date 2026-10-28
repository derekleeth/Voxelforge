using Unity.Mathematics;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Starts the game as a <b>Host</b> (server + local client in one program) and spawns the couch players.
    /// Even with nobody online, everything still goes through the host, so online play in Phase 6 only needs a
    /// different transport (CLAUDE.md Law #2). See DECISIONS.md D-007.
    /// </summary>
    public sealed class GameSession : MonoBehaviour
    {
        [SerializeField] NetworkManager networkManager;

        [SerializeField] UnityTransport transport;

        [SerializeField] PlayerRegistry playerRegistry;

        [SerializeField] VoxelWorldManager worldManager;

        [Tooltip("How many players share this PC.")]
        [SerializeField, Range(1, 4)] int localPlayerCount = 2;

        [Tooltip("Port the host listens on. Only this PC connects for now.")]
        [SerializeField] ushort port = 7777;

        [Tooltip("Block column (x, z) that Player 1 spawns on. Later players line up along +x.")]
        [SerializeField] Vector2Int spawnColumn = new Vector2Int(256, 256);

        [Tooltip("Blocks between each player's spawn column.")]
        [SerializeField, Min(1)] int spawnSpacing = 3;

        [Tooltip("Highest block the spawn search starts from. Must be inside the generated area.")]
        [SerializeField] int spawnSearchTopY = 255;

        public PlayerRegistry Players => playerRegistry;

        public NetworkManager NetworkManager => networkManager;

        void Start()
        {
            if (networkManager == null || transport == null || playerRegistry == null || worldManager == null)
            {
                Debug.LogError($"{nameof(GameSession)} is missing a reference.", this);
                enabled = false;
                return;
            }

            transport.SetConnectionData("127.0.0.1", port);
            networkManager.NetworkConfig.PlayerPrefab = null; // We spawn players ourselves, several per connection.
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

        void HandleServerStarted()
        {
            ulong hostClientId = networkManager.LocalClientId;
            Debug.Log($"[Voxelforge] Host started on port {port} (client {hostClientId}), {localPlayerCount} local players.", this);

            for (int slot = 0; slot < localPlayerCount; slot++)
            {
                var column = new int2(spawnColumn.x + slot * spawnSpacing, spawnColumn.y);
                playerRegistry.SpawnLocalPlayer(hostClientId, (byte)slot, worldManager, column, spawnSearchTopY);
            }
        }

        void HandleServerStopped(bool wasHost) => playerRegistry.Clear();
    }
}
