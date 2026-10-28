using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// The blocks one player has picked up (Roadmap 3.2, D-015). Only the host changes it (CLAUDE.md Law #2); every PC
    /// sees it through a <see cref="NetworkList{T}"/>, so a player's HUD can show it. <see cref="PlayerBuilder"/> places from it (3.3),
    /// it's saved with the player's <see cref="PlayerProfile"/> (3.4), and real inventory windows are 4.4.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerBlockBag : NetworkBehaviour
    {
        readonly NetworkList<BlockStack> _synced = new NetworkList<BlockStack>();
        readonly BlockStacks _server = new BlockStacks();

        /// <summary>How many slots the bag has. Some may be empty (count 0).</summary>
        public int SlotCount => _synced.Count;

        public BlockStack GetSlot(int index) => _synced[index];

        public int CountOf(ushort blockId)
        {
            for (int i = 0; i < _synced.Count; i++)
            {
                if (_synced[i].BlockId == blockId)
                    return _synced[i].Count;
            }
            return 0;
        }

        /// <summary>Host only: puts blocks in the bag. Returns how many fit (each type stops at <see cref="BlockStacks.MaxPerBlock"/>).</summary>
        public int ServerAdd(ushort blockId, int amount)
        {
            if (!IsServer)
            {
                Debug.LogError("Only the host can fill a block bag.", this);
                return 0;
            }
            int added = _server.Add(blockId, amount, out int index);
            Sync(index);
            return added;
        }

        /// <summary>Host only: takes blocks out of the bag if there are enough (for <see cref="PlayerBuilder"/>).</summary>
        public bool ServerTryTake(ushort blockId, int amount)
        {
            if (!IsServer)
            {
                Debug.LogError("Only the host can take from a block bag.", this);
                return false;
            }
            bool taken = _server.TryTake(blockId, amount, out int index);
            Sync(index);
            return taken;
        }

        /// <summary>Host only: refills the bag from a saved profile (Roadmap 3.4), keeping its slot order.</summary>
        public void ServerRestore(IEnumerable<BlockStack> stacks)
        {
            if (!IsServer)
            {
                Debug.LogError("Only the host can fill a block bag.", this);
                return;
            }
            _server.Restore(stacks);
            _synced.Clear();
            foreach (BlockStack stack in _server.Stacks)
                _synced.Add(stack);
        }

        /// <summary>Host only: a copy of every slot in order, for saving.</summary>
        public List<BlockStack> ServerSnapshot() => new List<BlockStack>(_server.Stacks);

        void Sync(int index)
        {
            if (index < 0)
                return;
            BlockStack stack = _server.Stacks[index];
            if (index < _synced.Count)
                _synced[index] = stack;
            else
                _synced.Add(stack); // Slots only ever append, so the new one is always the next index.
        }
    }
}
