using System;
using Unity.Netcode;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// The nine ability IDs on one hero's action bar (Roadmap 4.4), <see cref="AbilityIds.None"/> for an empty slot.
    /// Sent as raw bytes, so it stays small and unmanaged.
    /// </summary>
    public struct ActionBarSlots : INetworkSerializeByMemcpy, IEquatable<ActionBarSlots>
    {
        public ushort Slot1, Slot2, Slot3, Slot4, Slot5, Slot6, Slot7, Slot8, Slot9;

        /// <param name="index">0-8.</param>
        public ushort this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return Slot1;
                    case 1: return Slot2;
                    case 2: return Slot3;
                    case 3: return Slot4;
                    case 4: return Slot5;
                    case 5: return Slot6;
                    case 6: return Slot7;
                    case 7: return Slot8;
                    case 8: return Slot9;
                    default: throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
            set
            {
                switch (index)
                {
                    case 0: Slot1 = value; break;
                    case 1: Slot2 = value; break;
                    case 2: Slot3 = value; break;
                    case 3: Slot4 = value; break;
                    case 4: Slot5 = value; break;
                    case 5: Slot6 = value; break;
                    case 6: Slot7 = value; break;
                    case 7: Slot8 = value; break;
                    case 8: Slot9 = value; break;
                    default: throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
        }

        public bool Equals(ActionBarSlots other)
        {
            for (int i = 0; i < PlayerActionBar.SlotCount; i++)
            {
                if (this[i] != other[i])
                    return false;
            }
            return true;
        }

        public override bool Equals(object obj) => obj is ActionBarSlots other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(HashCode.Combine(Slot1, Slot2, Slot3, Slot4, Slot5), HashCode.Combine(Slot6, Slot7, Slot8, Slot9));
    }
}
