using System;
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

        public bool Equals(EquippedGear other) =>
            Head == other.Head && Chest == other.Chest && Legs == other.Legs && Feet == other.Feet &&
            MainHand == other.MainHand && OffHand == other.OffHand;

        public override bool Equals(object obj) => obj is EquippedGear other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Head, Chest, Legs, Feet, MainHand, OffHand);
    }
}
