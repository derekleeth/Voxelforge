using System;
using Unity.Netcode;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// The bags a player wears (Roadmap 4.5): four sockets, each an item ID or 0 for empty. Every bag adds its
    /// <see cref="IItemRules.BagSlotsOf"/> slots to the <see cref="PlayerBlockBag"/>. Sent as raw bytes and saved in
    /// the player's profile.
    /// </summary>
    public struct EquippedBags : INetworkSerializeByMemcpy, IEquatable<EquippedBags>
    {
        public const int SocketCount = 4;

        public ushort Socket0;
        public ushort Socket1;
        public ushort Socket2;
        public ushort Socket3;

        public ushort this[int socket]
        {
            get => socket switch
            {
                0 => Socket0,
                1 => Socket1,
                2 => Socket2,
                3 => Socket3,
                _ => throw new ArgumentOutOfRangeException(nameof(socket)),
            };
            set
            {
                switch (socket)
                {
                    case 0: Socket0 = value; break;
                    case 1: Socket1 = value; break;
                    case 2: Socket2 = value; break;
                    case 3: Socket3 = value; break;
                    default: throw new ArgumentOutOfRangeException(nameof(socket));
                }
            }
        }

        /// <summary>The slots all the worn bags add up to.</summary>
        public int ExtraSlots(IItemRules rules)
        {
            int extra = 0;
            for (int i = 0; i < SocketCount; i++)
            {
                if (this[i] != 0)
                    extra += rules.BagSlotsOf(this[i]);
            }
            return extra;
        }

        public bool Equals(EquippedBags other) =>
            Socket0 == other.Socket0 && Socket1 == other.Socket1 && Socket2 == other.Socket2 && Socket3 == other.Socket3;

        public override bool Equals(object obj) => obj is EquippedBags other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Socket0, Socket1, Socket2, Socket3);
    }
}
