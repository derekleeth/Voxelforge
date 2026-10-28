namespace Voxelforge.Gameplay
{
    public enum LocalSlotState : byte
    {
        /// <summary>Nobody here. The next device to press Join can take it.</summary>
        Free,
        /// <summary>A device claimed the slot; waiting for the host to spawn the player.</summary>
        Joining,
        /// <summary>Playing, with a connected device.</summary>
        Active,
        /// <summary>The player exists but their device was unplugged. They stand still until it's back.</summary>
        Disconnected,
    }

    /// <summary>
    /// Keeps track of the player slots on this PC (0 = Player 1) and decides which slot a newly joining device gets.
    /// Plain C# so tests can check the rules without any devices.
    /// </summary>
    public sealed class LocalSlotAllocator
    {
        readonly LocalSlotState[] _states;

        public LocalSlotAllocator(int maxSlots) => _states = new LocalSlotState[maxSlots];

        public int MaxSlots => _states.Length;

        public LocalSlotState this[int slot]
        {
            get => _states[slot];
            set => _states[slot] = value;
        }

        /// <summary>
        /// The slot a new device should take: a <b>disconnected</b> player first (a new controller replaces a lost
        /// one), otherwise the lowest free slot. Returns -1 when every slot is in use.
        /// </summary>
        public int ChooseSlotForNewDevice()
        {
            for (int slot = 0; slot < _states.Length; slot++)
            {
                if (_states[slot] == LocalSlotState.Disconnected)
                    return slot;
            }
            for (int slot = 0; slot < _states.Length; slot++)
            {
                if (_states[slot] == LocalSlotState.Free)
                    return slot;
            }
            return -1;
        }
    }
}
