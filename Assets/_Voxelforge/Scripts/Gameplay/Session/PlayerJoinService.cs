using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Lets any connection ask the host for a player in one of its local slots. The couch on the host PC uses it
    /// today, and remote couches will use the same request in Phase 6. The host checks every request before
    /// spawning (CLAUDE.md Law #2).
    /// <para>
    /// Lives on its own scene object with a <c>NetworkObject</c>, because NGO doesn't allow one on the NetworkManager.
    /// </para>
    /// </summary>
    public sealed class PlayerJoinService : NetworkBehaviour, ILocalJoinRequester
    {
        [SerializeField] PlayerRegistry playerRegistry;

        [SerializeField] VoxelWorldManager worldManager;

        [Tooltip("Most players one connection (one PC) may bring.")]
        [SerializeField, Range(1, 4)] int maxLocalPlayersPerConnection = 4;

        [Tooltip("Most players in the whole session.")]
        [SerializeField, Range(1, 16)] int maxPlayers = 4;

        [Tooltip("Block column (x, z) the first player spawns on. Later players line up along +x.")]
        [SerializeField] Vector2Int spawnColumn = new Vector2Int(256, 256);

        [Tooltip("Blocks between each player's spawn column.")]
        [SerializeField, Min(1)] int spawnSpacing = 3;

        [Tooltip("Highest block the spawn search starts from. Must be below the top of the world.")]
        [SerializeField] int spawnSearchTopY = 255;

        public void RequestJoin(byte localSlot)
        {
            if (!IsSpawned)
            {
                Debug.LogWarning($"[Voxelforge] Can't join slot {localSlot} yet: not connected to a host.", this);
                return;
            }
            RequestJoinRpc(localSlot);
        }

        [Rpc(SendTo.Server)]
        void RequestJoinRpc(byte localSlot, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            string refusal =
                localSlot >= maxLocalPlayersPerConnection ? $"slot {localSlot} is above the limit of {maxLocalPlayersPerConnection}" :
                playerRegistry.IsSlotTaken(sender, localSlot) ? $"slot {localSlot} already has a player" :
                playerRegistry.Players.Count >= maxPlayers ? $"the session is full ({maxPlayers} players)" :
                null;
            if (refusal != null)
            {
                Debug.LogWarning($"[Voxelforge] Refused join from client {sender}: {refusal}.", this);
                return;
            }

            var column = new int2(spawnColumn.x + playerRegistry.Players.Count * spawnSpacing, spawnColumn.y);
            playerRegistry.SpawnLocalPlayer(sender, localSlot, worldManager, column, spawnSearchTopY);
        }
    }
}
