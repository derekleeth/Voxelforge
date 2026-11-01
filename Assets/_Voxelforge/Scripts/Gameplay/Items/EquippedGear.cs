using System;
using System.Collections.Generic;
using Unity.Netcode;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// The gear a player wears (Roadmap 5.5, D-036): one socket per <see cref="EquipSlot"/> (Head, Chest, Legs, Feet,
    /// Main Hand, Off Hand), each an item ID or 0 for empty. Sent as raw bytes and saved in the player's profile, like
    /// <see cref="EquippedBags"/>.
    /// </summary>
    public struct EquippedGear : INetworkSerializeByMemcpy, IEquatable<EquippedGear>
    {
        public const int SocketCount = 6;

        public ushort Head;
        public ushort Chest;
        public ushort Legs;
        public ushort Feet;
        public ushort MainHand;
        public ushort OffHand;

        public ushort this[int socket]
        {
            get => socket switch
            {
                0 => Head,
                1 => Chest,
                2 => Legs,
                3 => Feet,
                4 => MainHand,
                5 => OffHand,
                _ => throw new ArgumentOutOfRangeException(nameof(socket)),
            };
            set
            {
                switch (socket)
                {
                    case 0: Head = value; break;
                    case 1: Chest = value; break;
                    case 2: Legs = value; break;
                    case 3: Feet = value; break;
                    case 4: MainHand = value; break;
                    case 5: OffHand = value; break;
                    default: throw new ArgumentOutOfRangeException(nameof(socket));
                }
            }
        }

        /// <summary>The socket a slot's gear is worn in, or -1 for <see cref="EquipSlot.None"/>.</summary>
        public static int SocketOf(EquipSlot slot) => slot == EquipSlot.None ? -1 : (int)slot - 1;

        /// <summary>The slot worn in <paramref name="socket"/>.</summary>
        public static EquipSlot SlotOf(int socket) => (EquipSlot)(socket + 1);

        /// <summary>
        /// Wears the gear in bag slot <paramref name="index"/> in its slot's socket. Whatever was worn there goes back into
        /// the bag, into the slot just freed, so it's a straight swap. False (and nothing changes) if it isn't worn gear.
        /// <paramref name="changed"/> gets the bag slots it touched.
        /// </summary>
        public static bool TryEquip(ItemStacks bag, ref EquippedGear gear, int index, List<int> changed)
        {
            changed.Clear();
            if (index < 0 || index >= bag.Capacity)
                return false;
            ushort itemId = bag.Stacks[index].ItemId;
            int socket = SocketOf(bag.Rules.EquipSlotOf(itemId));
            if (itemId == 0 || socket < 0)
                return false;
            ushort old = gear[socket];
            bag.TakeSlot(index, changed);
            if (old != 0)
            {
                // Always fits (the slot just freed is empty), wherever Add puts it; then into that freed slot.
                var added = new List<int>();
                bag.Add(old, 1, added);
                if (added.Count > 0 && added[0] != index)
                    bag.Move(added[0], index, added);
                changed.Add(index);
                foreach (int slot in added)
                {
                    if (!changed.Contains(slot))
                        changed.Add(slot);
                }
            }
            gear[socket] = itemId;
            return true;
        }

        /// <summary>Takes off the gear in <paramref name="socket"/> into the bag. False if the bag has no room or it's empty.</summary>
        public static bool TryUnequip(ItemStacks bag, ref EquippedGear gear, int socket, List<int> changed)
        {
            changed.Clear();
            ushort itemId = gear[socket];
            if (itemId == 0 || bag.RoomFor(itemId) < 1)
                return false;
            bag.Add(itemId, 1, changed);
            gear[socket] = 0;
            return true;
        }

        public bool Equals(EquippedGear other) =>
            Head == other.Head && Chest == other.Chest && Legs == other.Legs && Feet == other.Feet &&
            MainHand == other.MainHand && OffHand == other.OffHand;

        public override bool Equals(object obj) => obj is EquippedGear other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Head, Chest, Legs, Feet, MainHand, OffHand);
    }
}
