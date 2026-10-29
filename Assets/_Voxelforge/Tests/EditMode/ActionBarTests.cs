using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using Voxelforge.Gameplay;
using Object = UnityEngine.Object;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 4.4: the action bar's slots, the buttons that cast them and how a gamepad reaches all nine.</summary>
    public class ActionBarTests
    {
        [Test]
        public void ActionSlotButtons_AreNineBitsInOrder()
        {
            Assert.AreEqual(PlayerButtons.ActionSlot1, ActionSlotButtons.For(0));
            Assert.AreEqual(PlayerButtons.ActionSlot5, ActionSlotButtons.For(4));
            Assert.AreEqual(PlayerButtons.ActionSlot9, ActionSlotButtons.For(8));
            // Nothing else shares a bit with them.
            var others = PlayerButtons.Jump | PlayerButtons.Mine | PlayerButtons.Place | PlayerButtons.SelectNext |
                         PlayerButtons.SelectPrevious | PlayerButtons.ToggleGhost | PlayerButtons.Interact | PlayerButtons.Home;
            for (int i = 0; i < PlayerActionBar.SlotCount; i++)
                Assert.AreEqual(PlayerButtons.None, others & ActionSlotButtons.For(i));
        }

        [Test]
        public void Pad_DpadIsSlotsOneToFour()
        {
            var chord = new PadSlotChord();
            Assert.AreEqual(0, chord.Update(false, 0, 0f));
            Assert.AreEqual(1, chord.Update(false, 1, 0.1f));
            Assert.AreEqual(4, chord.Update(false, 4, 0.2f));
        }

        [Test]
        public void Pad_HoldingBTurnsTheDpadIntoFiveToEight_AndNeverAlsoFiresNine()
        {
            var chord = new PadSlotChord();
            Assert.AreEqual(0, chord.Update(true, 0, 0f), "B alone does nothing while it's held.");
            Assert.AreEqual(5, chord.Update(true, 1, 0.05f));
            Assert.AreEqual(8, chord.Update(true, 4, 0.1f));
            Assert.AreEqual(0, chord.Update(true, 0, 0.15f));
            Assert.AreEqual(0, chord.Update(false, 0, 0.2f), "Letting go of B after using it is not a tap.");
            Assert.AreEqual(0, chord.Update(false, 0, 0.25f));
        }

        [Test]
        public void Pad_TappingBIsSlotNine_HeldLongEnoughForTheHost()
        {
            var chord = new PadSlotChord();
            chord.Update(true, 0, 0f);
            Assert.AreEqual(9, chord.Update(false, 0, 0.05f));
            Assert.AreEqual(9, chord.Update(false, 0, 0.05f + PadSlotChord.TapHoldSeconds * 0.9f));
            Assert.AreEqual(0, chord.Update(false, 0, 0.05f + PadSlotChord.TapHoldSeconds * 1.1f));
            // A new hold starts fresh.
            chord.Update(true, 2, 1f);
            Assert.AreEqual(0, chord.Update(false, 0, 1.05f));
        }

        [Test]
        public void Slots_SurviveTheWire()
        {
            var slots = new ActionBarSlots { Slot1 = AbilityIds.ArcaneOrb, Slot3 = AbilityIds.EarthShatter, Slot9 = 42 };
            using var writer = new FastBufferWriter(64, Allocator.Temp);
            writer.WriteValueSafe(slots);
            using var reader = new FastBufferReader(writer, Allocator.Temp);
            reader.ReadValueSafe(out ActionBarSlots read);
            Assert.AreEqual(slots, read);
            Assert.AreEqual(AbilityIds.EarthShatter, read[2]);
            Assert.AreEqual(AbilityIds.None, read[1]);
        }

        [Test]
        public void Loadout_BecomesSlotIds()
        {
            AbilityDefinition orb = AbilityDefinition.Create(AbilityIds.ArcaneOrb, "Arcane Orb");
            AbilityDefinition pyro = AbilityDefinition.Create(AbilityIds.Pyroblast, "Pyroblast");
            ActionBarLoadout loadout = ActionBarLoadout.Create(orb, null, pyro);
            try
            {
                ActionBarSlots slots = loadout.ToSlots();
                Assert.AreEqual(AbilityIds.ArcaneOrb, slots[0]);
                Assert.AreEqual(AbilityIds.None, slots[1]);
                Assert.AreEqual(AbilityIds.Pyroblast, slots[2]);
                Assert.AreEqual(AbilityIds.None, slots[8]);
            }
            finally
            {
                Object.DestroyImmediate(loadout);
                Object.DestroyImmediate(orb);
                Object.DestroyImmediate(pyro);
            }
        }

        [TestCase("KeyboardMouse", 0, "1")]
        [TestCase("KeyboardLeft", 8, "9")]
        [TestCase("KeyboardRight", 3, "N4")]
        [TestCase("Gamepad", 0, "▲")]
        [TestCase("Gamepad", 5, "B+►")]
        [TestCase("Gamepad", 8, "B")]
        public void HotkeyLabels_MatchEachPlayersControls(string scheme, int slot, string label) =>
            Assert.AreEqual(label, ActionBarView.HotkeyLabel(scheme, slot));
    }
}
