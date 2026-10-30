using System;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Everything one player carries (Roadmap 3.2, D-015; tools since 3.5; slots and worn bags since 4.5, D-028). Only
    /// the host changes it (CLAUDE.md Law #2); every PC sees the slots through a <see cref="NetworkList{T}"/> and the worn
    /// bags through a <see cref="NetworkVariable{T}"/>, so each player's inventory window can show them. The owning PC asks
    /// for moves, sorting, bag swaps, dropping a stack as a <see cref="LootBag"/> and Send to Partner with RPCs that the
    /// host checks. Saved with the player's <see cref="PlayerProfile"/>.
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
        float _lastFullTold = float.NegativeInfinity;
        const float BagFullRepeatSeconds = 4f;
        // How high a dropped stack leaves the hero, and how hard it's thrown forward and up.
        const float DropHeight = 1.1f;
        const float DropForward = 2.5f;
        const float DropUp = 3f;
        PlayerController _player;

        /// <summary>Owner only: the host put a picked-up stack in this bag (item, count), for the combat text (4.4).</summary>
        public event Action<ushort, int> PickedUp;

        /// <summary>Owner only: the host said no to something (a full bag), for the combat text.</summary>
        public event Action<BagRefusal> Refused;

        /// <summary>Owner only: the host sent a stack to a partner (item, count), for the combat text.</summary>
        public event Action<ushort, int> Sent;

        /// <summary>
        /// Every PC: one hero tossed something to another with Send to Partner (from, to, item), so the toss can be drawn
        /// (<see cref="LootBagSpawner"/>). The items have already moved.
        /// </summary>
        public static event Action<PlayerBlockBag, PlayerBlockBag, ushort> Tossed;

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

        void Awake()
        {
            _server = new ItemStacks(BaseSlots, Rules);
            _player = GetComponent<PlayerController>();
        }

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

        /// <summary>Host only: the hero is standing on loot the full bag can't take. Says so now and then, not every frame.</summary>
        public void ServerTellBagFull()
        {
            if (Time.time - _lastFullTold < BagFullRepeatSeconds)
                return;
            _lastFullTold = Time.time;
            ServerTellRefused(BagRefusal.BagFull);
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
        /// Host only: Send to Partner (Roadmap 4.5). Gives slot <paramref name="index"/> to the nearest other hero,
        /// wherever they are; what their bag can't take lands at their feet as a loot bag.
        /// </summary>
        public bool ServerSendToPartner(int index)
        {
            if (!CheckServer("send from") || index < 0 || index >= _server.Capacity || _server.Stacks[index].IsEmpty)
                return false;
            PlayerController partner = NearestPartner();
            if (partner == null || !partner.TryGetComponent(out PlayerBlockBag partnerBag))
            {
                ServerTellRefused(BagRefusal.NoPartner);
                return false;
            }

            ItemStack stack = ServerTakeSlot(index);
            int given = partnerBag.ServerAdd(stack.ItemId, stack.Count);
            if (given < stack.Count)
            {
                var rest = new[] { new ItemStack(stack.ItemId, (ushort)(stack.Count - given)) };
                LootBagSpawner loot = _player.Registry != null ? _player.Registry.Loot : null;
                if (loot != null)
                    loot.ServerSpawn((float3)partner.transform.position + new float3(0f, 1f, 0f), rest, new float3(0f, 2f, 0f));
                else
                    _server.Add(rest[0].ItemId, rest[0].Count); // Nowhere to put it: keep it rather than lose it.
            }
            if (given > 0)
                partnerBag.ServerTellPickedUp(stack.ItemId, given);
            SentRpc(stack.ItemId, stack.Count);
            TossRpc(partnerBag.NetworkObject, stack.ItemId);
            return true;
        }

        /// <summary>Host only: drops slot <paramref name="index"/> in front of the hero as a loot bag (Roadmap 4.5).</summary>
        public bool ServerDrop(int index)
        {
            LootBagSpawner loot = _player.Registry != null ? _player.Registry.Loot : null;
            if (!CheckServer("drop from") || loot == null || index < 0 || index >= _server.Capacity || _server.Stacks[index].IsEmpty)
                return false;
            ItemStack stack = ServerTakeSlot(index);
            float3 forward = transform.forward;
            float3 at = (float3)transform.position + new float3(0f, DropHeight, 0f) + forward * 0.4f;
            loot.ServerSpawn(at, new[] { stack }, forward * DropForward + new float3(0f, DropUp, 0f), _player.PlayerId);
            return true;
        }

        /// <summary>Host: the closest other hero in the world, or null when playing alone.</summary>
        PlayerController NearestPartner()
        {
            PlayerRegistry registry = _player.Registry;
            if (registry == null)
                return null;
            PlayerController nearest = null;
            float best = float.PositiveInfinity;
            foreach (PlayerController other in registry.Players)
            {
                if (other == null || other == _player || !other.HasLanded)
                    continue;
                float distance = math.distancesq(other.transform.position, transform.position);
                if (distance < best)
                {
                    best = distance;
                    nearest = other;
                }
            }
            return nearest;
        }

        [Rpc(SendTo.Owner)]
        void SentRpc(ushort itemId, ushort count) => Sent?.Invoke(itemId, count);

        [Rpc(SendTo.Everyone)]
        void TossRpc(NetworkObjectReference to, ushort itemId)
        {
            if (to.TryGet(out NetworkObject target) && target.TryGetComponent(out PlayerBlockBag toBag))
                Tossed?.Invoke(this, toBag, itemId);
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

        /// <summary>
        /// Host only: the slots themselves, for a Storage Chest (Roadmap 4.5, <see cref="PlayerChest"/>) to move stacks
        /// in and out of. Call <see cref="ServerSyncSlots"/> with the slots it changed.
        /// </summary>
        internal ItemStacks ServerStacks => IsServer ? _server : null;

        /// <summary>Host only: sends slots changed through <see cref="ServerStacks"/> to every PC.</summary>
        internal void ServerSyncSlots(IReadOnlyList<int> changed)
        {
            if (!CheckServer("change"))
                return;
            _changed.Clear();
            for (int i = 0; i < changed.Count; i++)
                _changed.Add(changed[i]);
            Sync();
        }

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

        public void RequestSendToPartner(int index)
        {
            if (index >= 0 && index < SlotCount)
                SendToPartnerRpc((byte)index);
        }

        public void RequestDrop(int index)
        {
            if (index >= 0 && index < SlotCount)
                DropRpc((byte)index);
        }

        [Rpc(SendTo.Server)]
        void SendToPartnerRpc(byte index, RpcParams rpcParams = default)
        {
            if (Allowed(rpcParams))
                ServerSendToPartner(index);
        }

        [Rpc(SendTo.Server)]
        void DropRpc(byte index, RpcParams rpcParams = default)
        {
            if (Allowed(rpcParams) && _player.HasLanded)
                ServerDrop(index);
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
        /// <summary>Send to Partner with nobody else playing.</summary>
        NoPartner,
        /// <summary>The Storage Chest is full.</summary>
        ChestFull,
    }
}
