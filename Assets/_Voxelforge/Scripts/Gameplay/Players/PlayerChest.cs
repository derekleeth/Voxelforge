using System;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>Which Storage Chest a hero has open (Roadmap 4.5), if any. Sent as raw bytes.</summary>
    public struct ChestLink : INetworkSerializeByMemcpy, IEquatable<ChestLink>
    {
        public int3 Block;
        public bool IsOpen;

        public bool Equals(ChestLink other) => IsOpen == other.IsOpen && Block.Equals(other.Block);
        public override bool Equals(object obj) => obj is ChestLink other && Equals(other);
        public override int GetHashCode() => IsOpen ? Block.GetHashCode() : 0;
    }

    /// <summary>
    /// One hero's side of the Storage Chests (Roadmap 4.5, D-028). Interact while aiming at a chest in reach opens it
    /// (<see cref="PlayerCrafter"/> asks <see cref="OwnerTryOpenAimed"/> first) along with the inventory window, which
    /// shows the chest's slots above the bag (<see cref="PlayerInventory"/>). Every change is a request: <b>the host</b>
    /// checks that the chest still stands within reach, moves the stacks between <see cref="StorageChests"/> and the
    /// <see cref="PlayerBlockBag"/> (Law #2), and sends the open chest's slots to this hero's owner through a
    /// <see cref="NetworkList{T}"/>. Which chest is open is synced to every PC, so the lid opens for everyone
    /// (<see cref="ChestLids"/>). Two heroes can use one chest at once and see each other's changes.
    /// </summary>
    [RequireComponent(typeof(PlayerController), typeof(PlayerBlockBag))]
    public sealed class PlayerChest : NetworkBehaviour
    {
        [Tooltip("Host: the shortest time between two chest requests from the owner, in seconds.")]
        [SerializeField, Range(0f, 0.5f)] float minRequestInterval = 0.05f;

        readonly NetworkVariable<ChestLink> _link = new NetworkVariable<ChestLink>();
        readonly NetworkList<ItemStack> _slots = new NetworkList<ItemStack>();
        readonly List<int> _bagChanged = new List<int>();
        readonly List<int> _chestChanged = new List<int>();
        PlayerController _player;
        PlayerBlockBag _bag;
        PlayerInventory _inventory;
        float _lastRequestTime = float.NegativeInfinity;
        float _closeAskedAt = float.NegativeInfinity;
        // Owner: how long to wait for the host to say a chest asked for is open before treating the window as bag-only.
        const float OpenWaitSeconds = 0.5f;

        /// <summary>Every PC: this hero has a chest open.</summary>
        public bool IsOpen => _link.Value.IsOpen;

        /// <summary>Every PC: the open chest's block, when <see cref="IsOpen"/>.</summary>
        public int3 Block => _link.Value.Block;

        /// <summary>Owner and host: the open chest's slots, or 0 while none is open.</summary>
        public int SlotCount => IsOpen ? _slots.Count : 0;

        public ItemStack GetSlot(int index) => _slots[index];

        void Awake()
        {
            _player = GetComponent<PlayerController>();
            _bag = GetComponent<PlayerBlockBag>();
            _inventory = GetComponent<PlayerInventory>();
        }

        void Update()
        {
            if (!IsSpawned)
                return;

            // Host: a hero who walks away from the chest (or is carried off by a hearth) lets go of it.
            if (IsServer && IsOpen)
            {
                StorageChests chests = Chests;
                if (chests == null || !chests.ServerIsChest(Block) || !_player.Aim.InReach(transform.position, Block))
                    ServerClose();
            }

            // Owner: the window closed (or never opened) while the host still has the chest open for us.
            if (IsOwner && IsOpen && (_inventory == null || !_inventory.IsOpen) && Time.unscaledTime - _closeAskedAt > OpenWaitSeconds)
                RequestClose();
        }

        // ---- Owner ----

        /// <summary>
        /// Owner only: if the hero is aiming at a Storage Chest, asks the host to open it and opens the inventory window
        /// to show it. False if they aren't (so Interact opens crafting instead). Only a PC with the world can tell.
        /// </summary>
        public bool OwnerTryOpenAimed()
        {
            if (!IsOwner || !_player.HasLanded)
                return false;
            VoxelWorld world = _player.World;
            if (!_player.Aim.TryAim(world, transform.position, _player.OwnerRawInput, out VoxelHit hit) ||
                world.GetVoxel(hit.Block).BlockId != BlockIds.Chest)
                return false;
            OpenRpc(new ChestLink { Block = hit.Block, IsOpen = true });
            // Give the host a moment to answer before Update decides the window is bag-only.
            _closeAskedAt = Time.unscaledTime;
            if (_inventory != null)
                _inventory.OwnerOpen();
            return true;
        }

        public void RequestClose()
        {
            _closeAskedAt = Time.unscaledTime;
            CloseRpc();
        }

        /// <summary>Moves or swaps two of the chest's slots.</summary>
        public void RequestMove(int from, int to)
        {
            if (InChest(from) && InChest(to))
                MoveRpc((byte)from, (byte)to);
        }

        /// <summary>Puts bag slot <paramref name="bagSlot"/> on chest slot <paramref name="chestSlot"/> (into, onto or swapped).</summary>
        public void RequestPut(int bagSlot, int chestSlot)
        {
            if (InBag(bagSlot) && InChest(chestSlot))
                PutRpc((byte)bagSlot, (byte)chestSlot);
        }

        /// <summary>Puts chest slot <paramref name="chestSlot"/> on bag slot <paramref name="bagSlot"/> (into, onto or swapped).</summary>
        public void RequestTake(int chestSlot, int bagSlot)
        {
            if (InBag(bagSlot) && InChest(chestSlot))
                TakeRpc((byte)chestSlot, (byte)bagSlot);
        }

        /// <summary>Stores all of bag slot <paramref name="bagSlot"/> that fits, wherever it fits.</summary>
        public void RequestStore(int bagSlot)
        {
            if (InBag(bagSlot))
                StoreRpc((byte)bagSlot);
        }

        /// <summary>Takes all of chest slot <paramref name="chestSlot"/> that fits into the bag.</summary>
        public void RequestTakeAll(int chestSlot)
        {
            if (InChest(chestSlot))
                TakeAllRpc((byte)chestSlot);
        }

        public void RequestSort()
        {
            if (IsOpen)
                SortRpc();
        }

        bool InChest(int index) => index >= 0 && index < SlotCount;

        bool InBag(int index) => index >= 0 && index < _bag.SlotCount;

        // ---- Host ----

        StorageChests Chests => _player.Registry != null ? _player.Registry.Chests : null;

        /// <summary>Host only: does this hero have the chest at <paramref name="block"/> open?</summary>
        internal bool ServerIsLookingInto(int3 block) => IsServer && IsOpen && Block.Equals(block);

        /// <summary>Host only: shows this hero's owner the chest's changed slots.</summary>
        internal void ServerShow(ItemStacks slots, IReadOnlyList<int> changed)
        {
            for (int i = 0; i < changed.Count; i++)
            {
                int index = changed[i];
                if (index < _slots.Count && index < slots.Capacity)
                    _slots[index] = slots.Stacks[index];
            }
        }

        /// <summary>Host only: lets go of the chest (broken, out of reach, or the window closed).</summary>
        internal void ServerClose()
        {
            if (!IsServer || !IsOpen)
                return;
            _link.Value = default;
            _slots.Clear();
        }

        void ServerOpen(int3 block)
        {
            StorageChests chests = Chests;
            ItemStacks slots = chests != null ? chests.ServerSlots(block) : null;
            if (slots == null || !_player.Aim.InReach(transform.position, block))
                return;
            _link.Value = new ChestLink { Block = block, IsOpen = true };
            _slots.Clear();
            foreach (ItemStack stack in slots.Stacks)
                _slots.Add(stack);
        }

        /// <summary>Host: the open chest's slots, if it still stands, plus the bag's.</summary>
        bool TryServerSlots(out ItemStacks chest, out ItemStacks bag)
        {
            StorageChests chests = Chests;
            chest = IsOpen && chests != null ? chests.ServerSlots(Block) : null;
            bag = _bag.ServerStacks;
            return chest != null && bag != null;
        }

        void ServerSync()
        {
            if (_bagChanged.Count > 0)
                _bag.ServerSyncSlots(_bagChanged);
            if (_chestChanged.Count > 0)
                Chests.ServerChanged(Block, _chestChanged);
        }

        [Rpc(SendTo.Server)]
        void OpenRpc(ChestLink chest, RpcParams rpcParams = default)
        {
            if (Allowed(rpcParams) && _player.HasLanded)
                ServerOpen(chest.Block);
        }

        [Rpc(SendTo.Server)]
        void CloseRpc(RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId == OwnerClientId)
                ServerClose();
        }

        [Rpc(SendTo.Server)]
        void MoveRpc(byte from, byte to, RpcParams rpcParams = default)
        {
            if (!Allowed(rpcParams) || !TryServerSlots(out ItemStacks chest, out _))
                return;
            _bagChanged.Clear();
            if (chest.Move(from, to, _chestChanged))
                ServerSync();
        }

        [Rpc(SendTo.Server)]
        void PutRpc(byte bagSlot, byte chestSlot, RpcParams rpcParams = default)
        {
            if (Allowed(rpcParams) && TryServerSlots(out ItemStacks chest, out ItemStacks bag) &&
                bag.MoveTo(bagSlot, chest, chestSlot, _bagChanged, _chestChanged))
                ServerSync();
        }

        [Rpc(SendTo.Server)]
        void TakeRpc(byte chestSlot, byte bagSlot, RpcParams rpcParams = default)
        {
            if (Allowed(rpcParams) && TryServerSlots(out ItemStacks chest, out ItemStacks bag) &&
                chest.MoveTo(chestSlot, bag, bagSlot, _chestChanged, _bagChanged))
                ServerSync();
        }

        [Rpc(SendTo.Server)]
        void StoreRpc(byte bagSlot, RpcParams rpcParams = default)
        {
            if (!Allowed(rpcParams) || !TryServerSlots(out ItemStacks chest, out ItemStacks bag))
                return;
            if (bag.MoveAllTo(bagSlot, chest, _bagChanged, _chestChanged) > 0)
                ServerSync();
            else if (bagSlot < bag.Capacity && !bag.Stacks[bagSlot].IsEmpty)
                _bag.ServerTellRefused(BagRefusal.ChestFull);
        }

        [Rpc(SendTo.Server)]
        void TakeAllRpc(byte chestSlot, RpcParams rpcParams = default)
        {
            if (!Allowed(rpcParams) || !TryServerSlots(out ItemStacks chest, out ItemStacks bag))
                return;
            if (chest.MoveAllTo(chestSlot, bag, _chestChanged, _bagChanged) > 0)
                ServerSync();
            else if (chestSlot < chest.Capacity && !chest.Stacks[chestSlot].IsEmpty)
                _bag.ServerTellRefused(BagRefusal.BagFull);
        }

        [Rpc(SendTo.Server)]
        void SortRpc(RpcParams rpcParams = default)
        {
            if (!Allowed(rpcParams) || !TryServerSlots(out ItemStacks chest, out _))
                return;
            _bagChanged.Clear();
            chest.Sort(_chestChanged);
            ServerSync();
        }

        /// <summary>Host: the request came from this hero's owner, and not too soon after the last one.</summary>
        bool Allowed(RpcParams rpcParams)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || Time.time - _lastRequestTime < minRequestInterval)
                return false;
            _lastRequestTime = Time.time;
            return true;
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer)
                ServerClose();
        }
    }
}
