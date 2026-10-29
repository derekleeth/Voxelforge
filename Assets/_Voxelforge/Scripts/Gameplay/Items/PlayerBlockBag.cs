using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// The blocks and tools one player carries (Roadmap 3.2, D-015; tools since 3.5). Only the host changes it (CLAUDE.md Law #2); every PC
    /// sees it through a <see cref="NetworkList{T}"/>, so a player's HUD can show it. <see cref="PlayerBuilder"/> places from it (3.3),
    /// it's saved with the player's <see cref="PlayerProfile"/> (3.4), and real inventory windows are 4.4.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerBlockBag : NetworkBehaviour
    {
        readonly NetworkList<ItemStack> _synced = new NetworkList<ItemStack>();
        readonly ItemStacks _server = new ItemStacks();
        readonly List<int> _changed = new List<int>();

        /// <summary>Owner only: the host put a picked-up stack in this bag (item, count), for the combat text (4.4).</summary>
        public event Action<ushort, int> PickedUp;

        /// <summary>How many slots the bag has. Some may be empty (count 0).</summary>
        public int SlotCount => _synced.Count;

        public ItemStack GetSlot(int index) => _synced[index];

        public int CountOf(ushort itemId)
        {
            for (int i = 0; i < _synced.Count; i++)
            {
                if (_synced[i].ItemId == itemId)
                    return _synced[i].Count;
            }
            return 0;
        }

        /// <summary>Host only: puts items in the bag. Returns how many fit (each type stops at <see cref="ItemStacks.MaxPerItem"/>).</summary>
        public int ServerAdd(ushort itemId, int amount)
        {
            if (!IsServer)
            {
                Debug.LogError("Only the host can fill a block bag.", this);
                return 0;
            }
            int added = _server.Add(itemId, amount, out int index);
            Sync(index);
            return added;
        }

        /// <summary>Host only: tells the owner that <paramref name="count"/> <paramref name="itemId"/> were picked up into this bag.</summary>
        public void ServerTellPickedUp(ushort itemId, int count)
        {
            if (IsServer && IsSpawned && count > 0)
                PickedUpRpc(itemId, count);
        }

        [Rpc(SendTo.Owner)]
        void PickedUpRpc(ushort itemId, int count) => PickedUp?.Invoke(itemId, count);

        /// <summary>Host only: takes items out of the bag if there are enough (for <see cref="PlayerBuilder"/>).</summary>
        public bool ServerTryTake(ushort itemId, int amount)
        {
            if (!IsServer)
            {
                Debug.LogError("Only the host can take from a block bag.", this);
                return false;
            }
            bool taken = _server.TryTake(itemId, amount, out int index);
            Sync(index);
            return taken;
        }

        /// <summary>Host only: crafting (Roadmap 3.5). Takes all of <paramref name="inputs"/> and adds <paramref name="output"/>, or nothing.</summary>
        public bool ServerTryExchange(IReadOnlyList<ItemStack> inputs, ItemStack output)
        {
            if (!IsServer)
            {
                Debug.LogError("Only the host can craft from a block bag.", this);
                return false;
            }
            if (!_server.TryExchange(inputs, output, _changed))
                return false;
            foreach (int index in _changed)
                Sync(index);
            return true;
        }

        /// <summary>Host only: refills the bag from a saved profile (Roadmap 3.4), keeping its slot order.</summary>
        public void ServerRestore(IEnumerable<ItemStack> stacks)
        {
            if (!IsServer)
            {
                Debug.LogError("Only the host can fill a block bag.", this);
                return;
            }
            _server.Restore(stacks);
            _synced.Clear();
            foreach (ItemStack stack in _server.Stacks)
                _synced.Add(stack);
        }

        /// <summary>Host only: a copy of every slot in order, for saving.</summary>
        public List<ItemStack> ServerSnapshot() => new List<ItemStack>(_server.Stacks);

        void Sync(int index)
        {
            if (index < 0)
                return;
            ItemStack stack = _server.Stacks[index];
            if (index < _synced.Count)
                _synced[index] = stack;
            else
                _synced.Add(stack); // Slots only ever append, so the new one is always the next index.
        }
    }
}
