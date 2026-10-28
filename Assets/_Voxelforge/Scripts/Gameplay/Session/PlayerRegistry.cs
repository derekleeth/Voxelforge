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
        public readonly PlayerController Controller;

        public PlayerRecord(ulong ownerClientId, byte localSlot, PlayerController controller)
        {
            OwnerClientId = ownerClientId;
            LocalSlot = localSlot;
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

        readonly Dictionary<PlayerId, PlayerRecord> _records = new Dictionary<PlayerId, PlayerRecord>();
        readonly List<PlayerController> _players = new List<PlayerController>();
        byte _nextId = 1;

        /// <summary>Host only: every spawned player, in the order they joined.</summary>
        public IReadOnlyList<PlayerController> Players => _players;

        public PlayerController PlayerPrefab => playerPrefab;

        public bool TryGet(PlayerId playerId, out PlayerRecord record) => _records.TryGetValue(playerId, out record);

        /// <summary>
        /// Host only: creates a player owned by <paramref name="ownerClientId"/> in <paramref name="localSlot"/>, who
        /// will appear on the ground of the given block column once its terrain is generated.
        /// </summary>
        public PlayerController SpawnLocalPlayer(ulong ownerClientId, byte localSlot, VoxelWorldManager worldManager,
            int2 spawnColumn, int spawnTopY)
        {
            if (!networkManager.IsServer)
            {
                Debug.LogError($"Only the host can spawn players.", this);
                return null;
            }

            var playerId = new PlayerId(_nextId++);
            PlayerController player = Instantiate(playerPrefab);
            player.ServerInitialize(playerId, localSlot, worldManager, spawnColumn, spawnTopY);
            player.NetworkObject.SpawnWithOwnership(ownerClientId, destroyWithScene: true);

            _records.Add(playerId, new PlayerRecord(ownerClientId, localSlot, player));
            _players.Add(player);
            Debug.Log($"[Voxelforge] Spawned {playerId} (client {ownerClientId}, slot {localSlot}).", this);
            return player;
        }

        /// <summary>Forgets every player. Called when the host stops.</summary>
        public void Clear()
        {
            _records.Clear();
            _players.Clear();
            _nextId = 1;
        }
    }
}
