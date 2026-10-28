using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using Voxelforge.Core;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Lets any connection ask the host for a player in one of its local slots. The couch on the host PC uses it
    /// today, and remote couches will use the same request in Phase 6. The host checks every request before
    /// spawning (CLAUDE.md Law #2).
    /// <para>
    /// Each request carries the slot's <see cref="PlayerUuid"/> from this PC's <see cref="LocalPlayerIdentities"/>, so
    /// the host can bring back that player's saved profile (Roadmap 3.4, D-017).
    /// </para>
    /// <para>
    /// Lives on its own scene object with a <c>NetworkObject</c>, because NGO doesn't allow one on the NetworkManager.
    /// </para>
    /// </summary>
    public sealed class PlayerJoinService : NetworkBehaviour, ILocalJoinRequester
    {
        [SerializeField] PlayerRegistry playerRegistry;

        [SerializeField] VoxelWorldManager worldManager;

        [Tooltip("Optional: brings returning players back where they were, with their block bag.")]
        [SerializeField] WorldSaveManager saves;

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

        LocalPlayerIdentities _identities;

        public void RequestJoin(byte localSlot)
        {
            if (!IsSpawned)
            {
                Debug.LogWarning($"[Voxelforge] Can't join slot {localSlot} yet: not connected to a host.", this);
                return;
            }
            _identities ??= new LocalPlayerIdentities(LocalPlayerIdentities.DefaultPath);
            PlayerUuid uuid = _identities.Get(localSlot);
            RequestJoinRpc(localSlot, uuid.High, uuid.Low);
        }

        [Rpc(SendTo.Server)]
        void RequestJoinRpc(byte localSlot, ulong uuidHigh, ulong uuidLow, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            var uuid = new PlayerUuid(uuidHigh, uuidLow);
            string refusal =
                localSlot >= maxLocalPlayersPerConnection ? $"slot {localSlot} is above the limit of {maxLocalPlayersPerConnection}" :
                playerRegistry.IsSlotTaken(sender, localSlot) ? $"slot {localSlot} already has a player" :
                playerRegistry.Players.Count >= maxPlayers ? $"the session is full ({maxPlayers} players)" :
                !uuid.IsValid ? "it has no player identity" :
                playerRegistry.IsInSession(uuid) ? $"player {uuid} is already playing" :
                null;
            if (refusal != null)
            {
                Debug.LogWarning($"[Voxelforge] Refused join from client {sender}: {refusal}.", this);
                return;
            }

            var column = new int2(spawnColumn.x + playerRegistry.Players.Count * spawnSpacing, spawnColumn.y);
            PlayerProfile profile = saves != null && saves.TryGetProfile(uuid, out PlayerProfile saved) ? saved : null;
            playerRegistry.SpawnLocalPlayer(sender, localSlot, uuid, worldManager, column, spawnSearchTopY, profile);
        }
    }
}
