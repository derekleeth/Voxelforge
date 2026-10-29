using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Everything one player carries (Roadmap 3.2, D-015; tools since 3.5; slots and worn bags since 4.5, D-028). Only
    /// the host changes it (CLAUDE.md Law #2); every PC sees the slots through a <see cref="NetworkList{T}"/> and the worn
    /// bags through a <see cref="NetworkVariable{T}"/>, so each player's inventory window can show them. The owning PC asks
    /// for moves, sorting and bag swaps with RPCs that the host checks. Saved with the player's <see cref="PlayerProfile"/>.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerBlockBag : NetworkBehaviour
    {
        /// <summary>Slots before any bags are worn: generous, so a new player never feels cramped.</summary>
        public const int BaseSlots = 40;

        [Tooltip("Data/Items/ItemRegistry.asset: stack sizes, tabs and bag sizes.")]
        [SerializeField] ItemRegistry items;

        [Tooltip("Host: the shortest time between two inventory requests from the owner, in seconds.")]
        [SerializeField, Range(0f, 0.5f)] float minRequestInterval = 0.05f;

        readonly NetworkList<ItemStack> _synced = new NetworkList<ItemStack>();
        readonly NetworkVariable<EquippedBags> _bags = new NetworkVariable<EquippedBags>();
        ItemStacks _server;
        readonly List<int> _changed = new List<int>();
        float _lastRequestTime = float.NegativeInfinity;

        /// <summary>Owner only: the host put a picked-up stack in this bag (item, count), for the combat text (4.4).</summary>
        public event Action<ushort, int> PickedUp;

        /// <summary>Owner only: the host said no to something (a full bag), for the combat text.</summary>
        public event Action<BagRefusal> Refused;

        /// <summary>How many slots the bag has. Some may be empty.</summary>
        public int SlotCount => _synced.Count;

        public ItemStack GetSlot(int index) => _synced[index];

        /// <summary>The bags this player wears.</summary>
        public EquippedBags Bags => _bags.Value;

        public IItemRules Rules => items != null ? items : ItemRules.Default;

        public ItemRegistry Items => items;

        public int CountOf(ushort itemId)
        {
            int total = 0;
            for (int i = 0; i < _synced.Count; i++)
            {
                if (_synced[i].ItemId == itemId)
                    total += _synced[i].Count;
            }
            return total;
        }

        void Awake() => _server = new ItemStacks(BaseSlots, Rules);

        public override void OnNetworkSpawn()
        {
            if (IsServer && _synced.Count == 0)
                SyncAll();
        }

        // ---- Host ----

        /// <summary>Host only: how many more of the item fit.</summary>
        public int ServerRoomFor(ushort itemId) => IsServer ? _server.RoomFor(itemId) : 0;

        /// <summary>Host only: puts items in the bag. Returns how many fit.</summary>
        public int ServerAdd(ushort itemId, int amount)
        {
            if (!CheckServer("fill"))
                return 0;
            int added = _server.Add(itemId, amount, _changed);
            Sync();
            return added;
        }

        /// <summary>Host only: tells the owner that <paramref name="count"/> <paramref name="itemId"/> were picked up into this bag.</summary>
        public void ServerTellPickedUp(ushort itemId, int count)
        {
            if (IsServer && IsSpawned && count > 0)
                PickedUpRpc(itemId, count);
        }

        /// <summary>Host only: tells the owner the bag couldn't take something.</summary>
        public void ServerTellRefused(BagRefusal reason)
        {
            if (IsServer && IsSpawned)
                RefusedRpc(reason);
        }

        [Rpc(SendTo.Owner)]
        void PickedUpRpc(ushort itemId, int count) => PickedUp?.Invoke(itemId, count);

        [Rpc(SendTo.Owner)]
        void RefusedRpc(BagRefusal reason) => Refused?.Invoke(reason);

        /// <summary>Host only: takes items out of the bag if there are enough (for <see cref="PlayerBuilder"/>).</summary>
        public bool ServerTryTake(ushort itemId, int amount)
        {
            if (!CheckServer("take from"))
                return false;
            bool taken = _server.TryTake(itemId, amount, _changed);
            Sync();
            return taken;
        }

        /// <summary>Host only: crafting (Roadmap 3.5). Takes all of <paramref name="inputs"/> and adds <paramref name="output"/>, or nothing.</summary>
        public bool ServerTryExchange(IReadOnlyList<ItemStack> inputs, ItemStack output)
        {
            if (!CheckServer("craft from"))
                return false;
            if (!_server.TryExchange(inputs, output, _changed))
                return false;
            Sync();
            return true;
        }

        /// <summary>Host only: empties slot <paramref name="index"/> and returns what was in it.</summary>
        public ItemStack ServerTakeSlot(int index)
        {
            if (!CheckServer("take from"))
                return ItemStack.Empty;
            ItemStack stack = _server.TakeSlot(index, _changed);
            Sync();
            return stack;
        }

        /// <summary>Host only: moves or swaps two slots (Roadmap 4.5).</summary>
        public bool ServerMove(int from, int to)
        {
            if (!CheckServer("rearrange"))
                return false;
            bool moved = _server.Move(from, to, _changed);
            Sync();
            return moved;
        }

        /// <summary>Host only: Auto-Sort (Roadmap 4.5).</summary>
        public void ServerSort()
        {
            if (!CheckServer("sort"))
                return;
            _server.Sort(_changed);
            Sync();
        }

        /// <summary>
        /// Host only: wears the bag in slot <paramref name="index"/> (Roadmap 4.5) in the first empty socket. With every
        /// socket full it replaces the smallest worn bag, if the new one is bigger, and that bag takes the freed slot.
        /// </summary>
        public bool ServerEquipBag(int index)
        {
            if (!CheckServer("equip from") || index < 0 || index >= _server.Capacity)
                return false;
            ushort itemId = _server.Stacks[index].ItemId;
            int size = Rules.BagSlotsOf(itemId);
            if (size <= 0)
                return false;

            EquippedBags bags = _bags.Value;
            int socket = -1;
            for (int i = 0; i < EquippedBags.SocketCount && socket < 0; i++)
            {
                if (bags[i] == 0)
                    socket = i;
            }
            if (socket < 0)
            {
                socket = 0;
                for (int i = 1; i < EquippedBags.SocketCount; i++)
                {
                    if (Rules.BagSlotsOf(bags[i]) < Rules.BagSlotsOf(bags[socket]))
                        socket = i;
                }
                if (Rules.BagSlotsOf(bags[socket]) >= size)
                    return false;
            }

            ushort old = bags[socket];
            _server.TakeSlot(index, _changed);
            Sync();
            if (old != 0)
            {
                _server.Add(old, 1, _changed); // Goes into the slot just freed, at the latest.
                Sync();
            }
            bags[socket] = itemId;
            _bags.Value = bags;
            Resize();
            return true;
        }

        /// <summary>Host only: takes off the bag in <paramref name="socket"/>, if what it held fits in the rest.</summary>
        public bool ServerUnequipBag(int socket)
        {
            if (!CheckServer("unequip from") || socket < 0 || socket >= EquippedBags.SocketCount)
                return false;
            EquippedBags bags = _bags.Value;
            ushort itemId = bags[socket];
            if (itemId == 0)
                return false;

            int capacity = BaseSlots + bags.ExtraSlots(Rules) - Rules.BagSlotsOf(itemId);
            // The bag itself needs a slot too.
            if (_server.UsedSlots + 1 > capacity || !_server.TryResize(capacity, _changed))
            {
                ServerTellRefused(BagRefusal.NoRoom);
                return false;
            }
            Sync();
            ResizeSynced();
            bags[socket] = 0;
            _bags.Value = bags;
            _server.Add(itemId, 1, _changed);
            Sync();
            return true;
        }

        /// <summary>
        /// Host only: refills the bag from a saved profile (Roadmap 3.4): worn bags first, so the slots exist, then
        /// every slot in its place.
        /// </summary>
        /// <returns>What didn't fit, for the caller to drop. Usually empty.</returns>
        public List<ItemStack> ServerRestore(IEnumerable<ItemStack> stacks, EquippedBags bags)
        {
            if (!CheckServer("fill"))
                return new List<ItemStack>();
            _bags.Value = bags;
            List<ItemStack> lost = _server.Restore(stacks, BaseSlots + bags.ExtraSlots(Rules));
            SyncAll();
            return lost;
        }

        /// <summary>Host only: a copy of every slot in order, for saving.</summary>
        public List<ItemStack> ServerSnapshot() => new List<ItemStack>(_server.Stacks);

        // ---- Owner requests (Law #2: the host decides) ----

        public void RequestMove(int from, int to)
        {
            if (from >= 0 && to >= 0 && from < SlotCount && to < SlotCount)
                MoveRpc((byte)from, (byte)to);
        }

        public void RequestSort() => SortRpc();

        public void RequestEquip(int index)
        {
            if (index >= 0 && index < SlotCount)
                EquipRpc((byte)index);
        }

        public void RequestUnequip(int socket)
        {
            if (socket >= 0 && socket < EquippedBags.SocketCount)
                UnequipRpc((byte)socket);
        }

        [Rpc(SendTo.Server)]
        void MoveRpc(byte from, byte to, RpcParams rpcParams = default)
        {
            if (Allowed(rpcParams))
                ServerMove(from, to);
        }

        [Rpc(SendTo.Server)]
        void SortRpc(RpcParams rpcParams = default)
        {
            if (Allowed(rpcParams))
                ServerSort();
        }

        [Rpc(SendTo.Server)]
        void EquipRpc(byte index, RpcParams rpcParams = default)
        {
            if (Allowed(rpcParams))
                ServerEquipBag(index);
        }

        [Rpc(SendTo.Server)]
        void UnequipRpc(byte socket, RpcParams rpcParams = default)
        {
            if (Allowed(rpcParams))
                ServerUnequipBag(socket);
        }

        /// <summary>Host: the request came from this bag's owner, and not too soon after the last one.</summary>
        bool Allowed(RpcParams rpcParams)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || Time.time - _lastRequestTime < minRequestInterval)
                return false;
            _lastRequestTime = Time.time;
            return true;
        }

        bool CheckServer(string what)
        {
            if (IsServer)
                return true;
            Debug.LogError($"Only the host can {what} a bag.", this);
            return false;
        }

        void Resize()
        {
            int capacity = BaseSlots + _bags.Value.ExtraSlots(Rules);
            if (_server.TryResize(capacity, _changed))
                Sync();
            ResizeSynced();
        }

        void ResizeSynced()
        {
            while (_synced.Count > _server.Capacity)
                _synced.RemoveAt(_synced.Count - 1);
            while (_synced.Count < _server.Capacity)
                _synced.Add(_server.Stacks[_synced.Count]);
        }

        void Sync()
        {
            foreach (int index in _changed)
            {
                if (index < _synced.Count)
                    _synced[index] = _server.Stacks[index];
            }
            ResizeSynced();
        }

        void SyncAll()
        {
            _synced.Clear();
            foreach (ItemStack stack in _server.Stacks)
                _synced.Add(stack);
        }
    }

    /// <summary>Why the host refused an inventory request (Roadmap 4.5), for the owner's combat text.</summary>
    public enum BagRefusal : byte
    {
        /// <summary>The bag is full.</summary>
        BagFull,
        /// <summary>Taking off a bag would leave its items nowhere to go.</summary>
        NoRoom,
    }
}
