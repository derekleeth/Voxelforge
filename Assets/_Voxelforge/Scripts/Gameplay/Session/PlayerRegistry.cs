using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using Voxelforge.Core;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>Which connection and which local slot a <see cref="PlayerId"/> belongs to.</summary>
    public readonly struct PlayerRecord
    {
        public readonly ulong OwnerClientId;
        public readonly byte LocalSlot;
        /// <summary>Who they are across sessions; their saved profile is kept under it (Roadmap 3.4).</summary>
        public readonly PlayerUuid Uuid;
        public readonly PlayerController Controller;

        public PlayerRecord(ulong ownerClientId, byte localSlot, PlayerUuid uuid, PlayerController controller)
        {
            OwnerClientId = ownerClientId;
            LocalSlot = localSlot;
            Uuid = uuid;
            Controller = controller;
        }
    }

    /// <summary>
    /// The host's list of players. It hands out <see cref="PlayerId"/>s and spawns player objects.
    /// <b>Not a singleton:</b> systems that need the players get a reference to this component (CLAUDE.md Law #1).
    /// <para>
    /// One connection can own several players: on a couch, the host's own connection owns Player 1 and Player 2,
    /// told apart by their local slot. See DECISIONS.md D-007.
    /// </para>
    /// </summary>
    public sealed class PlayerRegistry : MonoBehaviour
    {
        [SerializeField] NetworkManager networkManager;

        [SerializeField] PlayerController playerPrefab;

        [Tooltip("Loads terrain around every player.")]
        [SerializeField] ChunkLoader chunkLoader;

        [Tooltip("How many chunks around a Home Totem load while its owner hearths there (Roadmap 3.8).")]
        [SerializeField, Range(1, 4)] int hearthRadiusChunks = 2;

        [Tooltip("Spawns loot bags (Roadmap 4.5): a player's dropped stacks, and saved items that no longer fit.")]
        [SerializeField] LootBagSpawner loot;

        readonly Dictionary<PlayerId, PlayerRecord> _records = new Dictionary<PlayerId, PlayerRecord>();
        readonly List<PlayerController> _players = new List<PlayerController>();
        byte _nextId = 1;

        /// <summary>Host only: every spawned player, in the order they joined.</summary>
        public IReadOnlyList<PlayerController> Players => _players;

        public PlayerController PlayerPrefab => playerPrefab;

        /// <summary>Host only: where loot bags come from (Roadmap 4.5), for players dropping and sending things.</summary>
        public LootBagSpawner Loot => loot;

        public bool TryGet(PlayerId playerId, out PlayerRecord record) => _records.TryGetValue(playerId, out record);

        /// <summary>Host only: true if <paramref name="ownerClientId"/> already has a player in <paramref name="localSlot"/>.</summary>
        public bool IsSlotTaken(ulong ownerClientId, byte localSlot)
        {
            foreach (PlayerRecord record in _records.Values)
            {
                if (record.OwnerClientId == ownerClientId && record.LocalSlot == localSlot)
                    return true;
            }
            return false;
        }

        /// <summary>Host only: true if a player with this identity is already in the session.</summary>
        public bool IsInSession(PlayerUuid uuid)
        {
            foreach (PlayerRecord record in _records.Values)
            {
                if (record.Uuid == uuid)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Host only: creates a player owned by <paramref name="ownerClientId"/> in <paramref name="localSlot"/>, who
        /// will appear on the ground of the given block column once its terrain is generated.
        /// </summary>
        /// <param name="profile">
        /// Optional: this player's saved profile (Roadmap 3.4). They come back where they stood, with their block bag,
        /// and <paramref name="spawnColumn"/> is ignored.
        /// </param>
        public PlayerController SpawnLocalPlayer(ulong ownerClientId, byte localSlot, PlayerUuid uuid,
            VoxelWorldManager worldManager, int2 spawnColumn, int spawnTopY, PlayerProfile profile = null)
        {
            if (!networkManager.IsServer)
            {
                Debug.LogError($"Only the host can spawn players.", this);
                return null;
            }

            var playerId = new PlayerId(_nextId++);
            PlayerController player = Instantiate(playerPrefab);
            player.ServerInitialize(playerId, localSlot, uuid, worldManager, this, spawnColumn, spawnTopY, profile);
            player.NetworkObject.SpawnWithOwnership(ownerClientId, destroyWithScene: true);
            if (profile != null && player.TryGetComponent(out PlayerBlockBag bag))
            {
                // After spawning: the bag's list only syncs once it's on the network.
                List<ItemStack> lost = bag.ServerRestore(profile.Bag, profile.Bags);
                if (lost.Count > 0)
                {
                    // Rare (a bag's size changed): leave them in a loot bag where the player comes back.
                    Debug.LogWarning($"{lost.Count} saved stacks didn't fit in {uuid}'s bag; they're in a loot bag at their feet.", this);
                    if (loot != null)
                        loot.ServerSpawn((float3)player.transform.position + new float3(0f, 1f, 0f), lost, float3.zero);
                }
            }
            player.TryGetComponent(out PlayerHearth hearth);
            if (profile != null && hearth != null)
                hearth.ServerRestore(profile.HasHome, profile.HomeBlock);
            if (chunkLoader != null)
            {
                chunkLoader.Register(player);
                if (hearth != null)
                    chunkLoader.Register(hearth, hearthRadiusChunks); // Only has a position while hearthing.
            }

            _records.Add(playerId, new PlayerRecord(ownerClientId, localSlot, uuid, player));
            _players.Add(player);
            Debug.Log($"[Voxelforge] Spawned {playerId} (client {ownerClientId}, slot {localSlot}, " +
                      $"{(profile != null ? "returning" : "new")} player {uuid}).", this);
            return player;
        }

        /// <summary>Forgets every player. Called when the host stops.</summary>
        public void Clear()
        {
            if (chunkLoader != null)
            {
                foreach (PlayerController player in _players)
                {
                    chunkLoader.Unregister(player);
                    if (player != null && player.TryGetComponent(out PlayerHearth hearth))
                        chunkLoader.Unregister(hearth);
                }
            }
            _records.Clear();
            _players.Clear();
            _nextId = 1;
        }
    }
}
