using NUnit.Framework;
using Voxelforge.Gameplay;

namespace Voxelforge.Tests
{
    public class LocalSlotAllocatorTests
    {
        [Test]
        public void FirstDevice_GetsSlotZero()
        {
            var slots = new LocalSlotAllocator(4);

            Assert.AreEqual(0, slots.ChooseSlotForNewDevice());
        }

        [Test]
        public void TakenSlots_AreSkipped()
        {
            var slots = new LocalSlotAllocator(4) { [0] = LocalSlotState.Active, [1] = LocalSlotState.Joining };

            Assert.AreEqual(2, slots.ChooseSlotForNewDevice());
        }

        [Test]
        public void DisconnectedSlot_IsReclaimedBeforeAFreeOne()
        {
            var slots = new LocalSlotAllocator(4) { [0] = LocalSlotState.Active, [1] = LocalSlotState.Disconnected };

            Assert.AreEqual(1, slots.ChooseSlotForNewDevice());
        }

        [Test]
        public void FullRoster_RefusesNewDevices()
        {
            var slots = new LocalSlotAllocator(2) { [0] = LocalSlotState.Active, [1] = LocalSlotState.Active };

            Assert.AreEqual(-1, slots.ChooseSlotForNewDevice());
        }
    }
}
